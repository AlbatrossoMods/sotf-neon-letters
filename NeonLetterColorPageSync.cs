#nullable enable

namespace SOTFNeonLetters;

internal static class NeonLetterColorPageProtocol
{
    internal const byte ProtocolVersion =
        NeonLetterNetworkProtocol.CurrentVersion;
    internal const int MaxPageEntries = 64;
    internal const int MaxPendingEntries = 65_536;
    internal const int MaxResponsePacketBytes = 2_048;
    internal const double RetryIntervalSeconds = 2d;
}

internal static class NeonLetterColorPageDeliveryProtocol
{
    internal const int MaxPagesPerUpdate = 4;
    internal const int MaxPendingPeers = 256;
}

internal readonly record struct NeonLetterColorPageEntry<TKey>(
    TKey Identity,
    ulong EntityRevision,
    NeonRgba Color)
    where TKey : notnull;

internal readonly record struct NeonLetterAuthoritativeColorPage<TKey>(
    ulong WatermarkChangeSerial,
    ulong NextCursorChangeSerial,
    bool Complete,
    IReadOnlyList<NeonLetterColorPageEntry<TKey>> Entries)
    where TKey : notnull;

internal readonly record struct NeonLetterColorPageRequest(
    byte ProtocolVersion,
    ulong SyncId,
    ulong CursorChangeSerial,
    ulong WatermarkChangeSerial);

internal readonly record struct NeonLetterColorPageResponse<TKey>(
    byte ProtocolVersion,
    ulong SyncId,
    ulong Sequence,
    ulong WatermarkChangeSerial,
    ulong NextCursorChangeSerial,
    bool Complete,
    IReadOnlyList<NeonLetterColorPageEntry<TKey>> Entries)
    where TKey : notnull;

internal interface INeonLetterColorPageWireReader<TKey>
    where TKey : notnull
{
    bool IsFullyConsumed { get; }

    byte ReadByte();

    ulong ReadUInt64();

    int ReadInt32();

    TKey ReadIdentity();

    NeonRgba ReadColor(byte protocolVersion);
}

internal static class NeonLetterColorPageWireParser
{
    internal static NeonLetterColorPageResponse<TKey> ReadResponse<
        TReader,
        TKey>(ref TReader reader)
        where TReader : struct, INeonLetterColorPageWireReader<TKey>
        where TKey : notnull
    {
        byte version = reader.ReadByte();
        if (version != NeonLetterColorPageProtocol.ProtocolVersion)
        {
            throw new InvalidDataException(
                "The color page protocol version is invalid.");
        }

        ulong syncId = reader.ReadUInt64();
        ulong sequence = reader.ReadUInt64();
        ulong watermark = reader.ReadUInt64();
        ulong nextCursor = reader.ReadUInt64();
        int count = reader.ReadInt32();
        byte completeValue = reader.ReadByte();
        if (syncId == 0 ||
            sequence == 0 ||
            count < 0 ||
            count > NeonLetterColorPageProtocol.MaxPageEntries ||
            completeValue > 1)
        {
            throw new InvalidDataException(
                "The color page response header is invalid.");
        }

        var entries = new NeonLetterColorPageEntry<TKey>[count];
        for (int index = 0; index < entries.Length; index++)
        {
            TKey identity = reader.ReadIdentity();
            ulong entityRevision = reader.ReadUInt64();
            if (EqualityComparer<TKey>.Default.Equals(identity, default!) ||
                entityRevision == 0)
            {
                throw new InvalidDataException(
                    "The color page response entry is invalid.");
            }

            entries[index] = new NeonLetterColorPageEntry<TKey>(
                identity,
                entityRevision,
                reader.ReadColor(version));
        }

        if (!reader.IsFullyConsumed)
        {
            throw new InvalidDataException(
                "The color page response contains trailing data.");
        }

        return new NeonLetterColorPageResponse<TKey>(
            version,
            syncId,
            sequence,
            watermark,
            nextCursor,
            Complete: completeValue == 1,
            entries);
    }
}

internal readonly record struct NeonLetterTargetedColorPage<TPeer, TKey>(
    TPeer Peer,
    NeonLetterColorPageResponse<TKey> Response)
    where TPeer : notnull
    where TKey : notnull;

internal enum NeonLetterColorPageScheduleResult
{
    Scheduled,
    Coalesced,
    Rejected,
    CapacityExceeded
}

internal readonly record struct NeonLetterTargetedColorPageRequest<TPeer>(
    TPeer Peer,
    NeonLetterColorPageRequest Request)
    where TPeer : notnull;

internal sealed class NeonLetterColorPageHostCoordinator<TPeer, TKey>
    where TPeer : notnull
    where TKey : notnull
{
    private readonly NeonLetterAuthoritativeColors<TKey> _authoritative;
    private readonly Func<
        ulong,
        ulong,
        NeonLetterAuthoritativeColorPage<TKey>> _createPage;
    private readonly Dictionary<TPeer, PeerSync> _peers = new();
    private readonly Dictionary<
        TPeer,
        LinkedListNode<NeonLetterTargetedColorPageRequest<TPeer>>>
        _pendingByPeer = new();
    private readonly LinkedList<
        NeonLetterTargetedColorPageRequest<TPeer>> _pendingRequests = new();

    internal NeonLetterColorPageHostCoordinator(
        NeonLetterAuthoritativeColors<TKey> authoritative)
        : this(
            authoritative,
            (cursor, watermark) =>
                authoritative.CreatePage(cursor, watermark))
    {
    }

    internal NeonLetterColorPageHostCoordinator(
        NeonLetterAuthoritativeColors<TKey> authoritative,
        Func<
            ulong,
            ulong,
            NeonLetterAuthoritativeColorPage<TKey>> createPage)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        ArgumentNullException.ThrowIfNull(createPage);
        _authoritative = authoritative;
        _createPage = createPage;
    }

    internal int PeerCount => _peers.Count;
    internal int OutstandingPageCount => _peers.Count;
    internal int PendingRequestCount => _pendingByPeer.Count;

    internal NeonLetterColorPageScheduleResult TryScheduleRequest(
        TPeer peer,
        bool canSchedule,
        NeonLetterColorPageRequest request)
    {
        if (!canSchedule || !IsValidRequest(request))
        {
            return NeonLetterColorPageScheduleResult.Rejected;
        }

        if (_pendingByPeer.TryGetValue(
                peer,
                out LinkedListNode<
                    NeonLetterTargetedColorPageRequest<TPeer>>? pending))
        {
            return pending.Value.Request == request
                ? NeonLetterColorPageScheduleResult.Coalesced
                : NeonLetterColorPageScheduleResult.Rejected;
        }

        if (_pendingByPeer.Count >=
            NeonLetterColorPageDeliveryProtocol.MaxPendingPeers)
        {
            return NeonLetterColorPageScheduleResult.CapacityExceeded;
        }

        var targeted = new NeonLetterTargetedColorPageRequest<TPeer>(
            peer,
            request);
        LinkedListNode<NeonLetterTargetedColorPageRequest<TPeer>> node =
            _pendingRequests.AddLast(targeted);
        _pendingByPeer.Add(peer, node);
        return NeonLetterColorPageScheduleResult.Scheduled;
    }

    internal int DrainScheduledRequests(
        Func<TPeer, bool> canSend,
        Action<NeonLetterTargetedColorPage<TPeer, TKey>> send,
        Action<TPeer, Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(canSend);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(onFailure);

        var updateBatch =
            new NeonLetterTargetedColorPageRequest<TPeer>[
                NeonLetterColorPageDeliveryProtocol.MaxPagesPerUpdate];
        int batchCount = 0;
        while (batchCount < updateBatch.Length &&
               _pendingRequests.First != null)
        {
            LinkedListNode<
                NeonLetterTargetedColorPageRequest<TPeer>> node =
                    _pendingRequests.First;
            _pendingRequests.RemoveFirst();
            _pendingByPeer.Remove(node.Value.Peer);
            updateBatch[batchCount++] = node.Value;
        }

        int sentCount = 0;
        for (int index = 0; index < batchCount; index++)
        {
            NeonLetterTargetedColorPageRequest<TPeer> scheduled =
                updateBatch[index];
            if (!canSend(scheduled.Peer))
            {
                continue;
            }

            try
            {
                if (!TryCreateResponse(
                        scheduled.Peer,
                        canSend: true,
                        scheduled.Request,
                        out NeonLetterTargetedColorPage<TPeer, TKey>
                            delivery))
                {
                    continue;
                }

                send(delivery);
                sentCount++;
            }
            catch (Exception exception)
            {
                onFailure(scheduled.Peer, exception);
            }
        }

        return sentCount;
    }

    internal bool TryCreateResponse(
        TPeer peer,
        bool canSend,
        NeonLetterColorPageRequest request,
        out NeonLetterTargetedColorPage<TPeer, TKey> delivery)
    {
        delivery = default;
        if (!canSend || !IsValidRequest(request))
        {
            return false;
        }

        if (!_peers.TryGetValue(peer, out PeerSync? sync))
        {
            if (!IsInitialRequest(request))
            {
                return false;
            }

            sync = new PeerSync(request.SyncId);
            _peers.Add(peer, sync);
        }
        else if (request.SyncId != sync.SyncId)
        {
            if (request.SyncId < sync.SyncId || !IsInitialRequest(request))
            {
                return false;
            }

            sync = new PeerSync(request.SyncId);
            _peers[peer] = sync;
        }
        else if (sync.TryGetDuplicate(request, out NeonLetterColorPageResponse<TKey> duplicate))
        {
            delivery = new NeonLetterTargetedColorPage<TPeer, TKey>(
                peer,
                duplicate);
            return true;
        }
        else if (!sync.IsExpected(request))
        {
            return false;
        }

        if (sync.NextSequence == 0)
        {
            return false;
        }

        NeonLetterAuthoritativeColorPage<TKey> page =
            _createPage(
                request.CursorChangeSerial,
                request.WatermarkChangeSerial);
        var response = new NeonLetterColorPageResponse<TKey>(
            NeonLetterColorPageProtocol.ProtocolVersion,
            request.SyncId,
            sync.NextSequence,
            page.WatermarkChangeSerial,
            page.NextCursorChangeSerial,
            page.Complete,
            page.Entries);
        sync.Record(request, response);
        delivery = new NeonLetterTargetedColorPage<TPeer, TKey>(
            peer,
            response);
        return true;
    }

    internal void Remove(TPeer peer)
    {
        RemovePendingRequest(peer);
        _peers.Remove(peer);
    }

    internal void Quarantine(
        TPeer peer,
        Action<TPeer> scheduleDisconnect)
    {
        ArgumentNullException.ThrowIfNull(scheduleDisconnect);
        Remove(peer);
        scheduleDisconnect(peer);
    }

    internal void Clear()
    {
        _pendingByPeer.Clear();
        _pendingRequests.Clear();
        _peers.Clear();
    }

    private void RemovePendingRequest(TPeer peer)
    {
        if (!_pendingByPeer.Remove(
                peer,
                out LinkedListNode<
                    NeonLetterTargetedColorPageRequest<TPeer>>? node))
        {
            return;
        }

        _pendingRequests.Remove(node);
    }

    private bool IsValidRequest(NeonLetterColorPageRequest request)
    {
        if (request.ProtocolVersion !=
                NeonLetterColorPageProtocol.ProtocolVersion ||
            request.SyncId == 0)
        {
            return false;
        }

        ulong currentSerial = _authoritative.CurrentChangeSerial;
        if (request.WatermarkChangeSerial == 0)
        {
            return request.CursorChangeSerial <= currentSerial;
        }

        return request.CursorChangeSerial <
                request.WatermarkChangeSerial &&
            request.WatermarkChangeSerial <= currentSerial;
    }

    private static bool IsInitialRequest(
        NeonLetterColorPageRequest request)
    {
        return request.CursorChangeSerial == 0 &&
            request.WatermarkChangeSerial == 0;
    }

    private sealed class PeerSync
    {
        private NeonLetterColorPageRequest _lastRequest;
        private NeonLetterColorPageResponse<TKey> _lastResponse;
        private bool _hasResponse;

        internal PeerSync(ulong syncId)
        {
            SyncId = syncId;
            NextSequence = 1;
        }

        internal ulong SyncId { get; }
        internal ulong NextSequence { get; private set; }

        internal bool IsExpected(NeonLetterColorPageRequest request)
        {
            if (!_hasResponse)
            {
                return IsInitialRequest(request);
            }

            return _lastResponse.Complete
                ? request.CursorChangeSerial ==
                    _lastResponse.WatermarkChangeSerial &&
                    request.WatermarkChangeSerial == 0
                : request.CursorChangeSerial ==
                    _lastResponse.NextCursorChangeSerial &&
                    request.WatermarkChangeSerial ==
                    _lastResponse.WatermarkChangeSerial;
        }

        internal bool TryGetDuplicate(
            NeonLetterColorPageRequest request,
            out NeonLetterColorPageResponse<TKey> response)
        {
            if (_hasResponse && request == _lastRequest)
            {
                response = _lastResponse;
                return true;
            }

            response = default;
            return false;
        }

        internal void Record(
            NeonLetterColorPageRequest request,
            NeonLetterColorPageResponse<TKey> response)
        {
            _lastRequest = request;
            _lastResponse = response;
            _hasResponse = true;
            NextSequence = unchecked(response.Sequence + 1);
        }
    }
}

internal sealed class NeonLetterColorPageClientCoordinator<TKey>
    where TKey : notnull
{
    private ulong _nextSyncId;
    private ulong _syncId;
    private ulong _expectedSequence;
    private NeonLetterColorPageRequest _outstandingRequest;
    private double _nextRequestAtSeconds;
    private bool _isActive;

    internal NeonLetterColorPageClientCoordinator()
        : this(firstSyncId: 1)
    {
    }

    internal NeonLetterColorPageClientCoordinator(ulong firstSyncId)
    {
        if (firstSyncId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstSyncId));
        }

        _nextSyncId = firstSyncId;
    }

    internal bool IsComplete { get; private set; }

    internal bool StartSession(bool canStart)
    {
        if (!canStart)
        {
            return false;
        }

        if (_isActive)
        {
            return true;
        }

        if (_nextSyncId == 0)
        {
            throw new InvalidOperationException(
                "The color page sync identifier space is exhausted.");
        }

        _syncId = _nextSyncId;
        _nextSyncId = unchecked(_nextSyncId + 1);
        _expectedSequence = 1;
        _outstandingRequest = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            _syncId,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);
        _nextRequestAtSeconds = 0d;
        IsComplete = false;
        _isActive = true;
        return true;
    }

    internal bool TryGetDueRequest(
        bool canRequest,
        double nowSeconds,
        out NeonLetterColorPageRequest request)
    {
        ValidateNow(nowSeconds);
        if (!canRequest ||
            !_isActive ||
            IsComplete ||
            nowSeconds < _nextRequestAtSeconds)
        {
            request = default;
            return false;
        }

        request = _outstandingRequest;
        return true;
    }

    internal void RecordRequestAttempt(double nowSeconds)
    {
        ValidateNow(nowSeconds);
        if (_isActive && !IsComplete)
        {
            _nextRequestAtSeconds =
                nowSeconds + NeonLetterColorPageProtocol.RetryIntervalSeconds;
        }
    }

    internal bool TryAcceptResponse(
        bool canApply,
        NeonLetterColorPageResponse<TKey> response,
        double nowSeconds,
        Func<NeonLetterColorPageEntry<TKey>, bool> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ValidateNow(nowSeconds);
        if (!canApply ||
            !_isActive ||
            IsComplete ||
            response.SyncId != _syncId)
        {
            return false;
        }

        if (response.Sequence < _expectedSequence)
        {
            return false;
        }

        if (response.Sequence > _expectedSequence ||
            !IsValidResponse(response))
        {
            _nextRequestAtSeconds = nowSeconds;
            return false;
        }

        try
        {
            foreach (NeonLetterColorPageEntry<TKey> entry in response.Entries)
            {
                if (!publish(entry))
                {
                    _nextRequestAtSeconds = nowSeconds;
                    return false;
                }
            }
        }
        catch
        {
            _nextRequestAtSeconds = nowSeconds;
            throw;
        }

        if (_expectedSequence == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The color page sequence space is exhausted.");
        }

        _expectedSequence++;
        if (response.Complete &&
            response.Entries.Count == 0 &&
            _outstandingRequest.CursorChangeSerial ==
                response.WatermarkChangeSerial)
        {
            IsComplete = true;
            return true;
        }

        _outstandingRequest = response.Complete
            ? new NeonLetterColorPageRequest(
                NeonLetterColorPageProtocol.ProtocolVersion,
                _syncId,
                response.WatermarkChangeSerial,
                WatermarkChangeSerial: 0)
            : new NeonLetterColorPageRequest(
                NeonLetterColorPageProtocol.ProtocolVersion,
                _syncId,
                response.NextCursorChangeSerial,
                response.WatermarkChangeSerial);
        _nextRequestAtSeconds = nowSeconds;
        return true;
    }

    internal void Clear()
    {
        _syncId = 0;
        _expectedSequence = 0;
        _outstandingRequest = default;
        _nextRequestAtSeconds = 0d;
        IsComplete = false;
        _isActive = false;
    }

    private bool IsValidResponse(
        NeonLetterColorPageResponse<TKey> response)
    {
        if (response.ProtocolVersion !=
                NeonLetterColorPageProtocol.ProtocolVersion ||
            response.Entries == null ||
            response.Entries.Count >
                NeonLetterColorPageProtocol.MaxPageEntries ||
            response.WatermarkChangeSerial <
                _outstandingRequest.CursorChangeSerial ||
            response.NextCursorChangeSerial <
                _outstandingRequest.CursorChangeSerial ||
            response.NextCursorChangeSerial >
                response.WatermarkChangeSerial ||
            (response.WatermarkChangeSerial ==
                _outstandingRequest.CursorChangeSerial &&
                response.Entries.Count != 0) ||
            (_outstandingRequest.WatermarkChangeSerial != 0 &&
                response.WatermarkChangeSerial !=
                    _outstandingRequest.WatermarkChangeSerial))
        {
            return false;
        }

        if (response.Complete)
        {
            if (response.NextCursorChangeSerial !=
                response.WatermarkChangeSerial)
            {
                return false;
            }
        }
        else if (response.Entries.Count == 0 ||
                 response.NextCursorChangeSerial <=
                    _outstandingRequest.CursorChangeSerial ||
                 response.NextCursorChangeSerial >=
                    response.WatermarkChangeSerial)
        {
            return false;
        }

        var identities = new HashSet<TKey>();
        foreach (NeonLetterColorPageEntry<TKey> entry in response.Entries)
        {
            if (EqualityComparer<TKey>.Default.Equals(
                    entry.Identity,
                    default!) ||
                entry.EntityRevision == 0 ||
                !identities.Add(entry.Identity))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateNow(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }
    }
}
