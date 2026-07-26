#nullable enable

namespace SOTFNeonLetters;

internal static class NeonLetterSnapshotProtocol
{
    internal const byte ProtocolVersion =
        NeonLetterNetworkProtocol.CurrentVersion;

    // This is a hostile-wire sanity maximum, not a save or product limit.
    internal const int MaxSnapshotEntries = 65_536;
    internal const int MaxSendFramesPerUpdate = 256;
}

internal readonly record struct NeonLetterSnapshotEntry(
    ulong Identity,
    NeonRgba Color);

internal sealed class NeonLetterSnapshotBatchCoordinator
{
    private readonly Dictionary<int, NeonLetterSnapshotEntry> _entries = new();
    private readonly HashSet<ulong> _snapshotIdentities = new();
    private readonly HashSet<ulong> _liveColorsAfterBegin = new();
    private ulong _lastRequestId;
    private ulong _currentRequestId;
    private int _declaredCount = -1;
    private bool _isInvalid;

    internal ulong StartRequest()
    {
        if (_lastRequestId == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "Snapshot request identifiers are exhausted.");
        }

        _lastRequestId++;
        _currentRequestId = _lastRequestId;
        ClearBatch();
        return _currentRequestId;
    }

    internal bool TryBegin(byte version, ulong requestId, int count)
    {
        if (!IsCurrentRequest(requestId))
        {
            return false;
        }

        if (version != NeonLetterSnapshotProtocol.ProtocolVersion ||
            count < 0 ||
            count > NeonLetterSnapshotProtocol.MaxSnapshotEntries ||
            _declaredCount >= 0)
        {
            InvalidateBatch();
            return false;
        }

        _declaredCount = count;
        _liveColorsAfterBegin.Clear();
        return true;
    }

    internal bool TryAcceptEntry(
        byte version,
        ulong requestId,
        int index,
        ulong identity,
        uint packedColor)
    {
        if (!IsCurrentRequest(requestId))
        {
            return false;
        }

        if (_isInvalid ||
            _declaredCount < 0 ||
            version != NeonLetterSnapshotProtocol.ProtocolVersion ||
            index < 0 ||
            index >= _declaredCount ||
            identity == 0 ||
            _entries.ContainsKey(index) ||
            _snapshotIdentities.Contains(identity))
        {
            InvalidateBatch();
            return false;
        }

        NeonRgba color;
        try
        {
            color = NeonLetterNetworkProtocol.Unpack(version, packedColor);
        }
        catch (InvalidOperationException)
        {
            InvalidateBatch();
            return false;
        }

        _entries.Add(index, new NeonLetterSnapshotEntry(identity, color));
        _snapshotIdentities.Add(identity);
        return true;
    }

    internal bool TryComplete(
        byte version,
        ulong requestId,
        int count,
        Action<IReadOnlyList<NeonLetterSnapshotEntry>> publish,
        Action onCompleted)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(onCompleted);

        if (!IsCurrentRequest(requestId))
        {
            return false;
        }

        if (_isInvalid ||
            _declaredCount < 0 ||
            version != NeonLetterSnapshotProtocol.ProtocolVersion ||
            count != _declaredCount ||
            _entries.Count != _declaredCount)
        {
            InvalidateBatch();
            return false;
        }

        int publishedCount = 0;
        for (int index = 0; index < _declaredCount; index++)
        {
            if (!_entries.TryGetValue(
                    index,
                    out NeonLetterSnapshotEntry entry))
            {
                InvalidateBatch();
                return false;
            }

            if (!_liveColorsAfterBegin.Contains(entry.Identity))
            {
                publishedCount++;
            }
        }

        var completedEntries =
            new NeonLetterSnapshotEntry[publishedCount];
        int publishedIndex = 0;
        for (int index = 0; index < _declaredCount; index++)
        {
            NeonLetterSnapshotEntry entry = _entries[index];
            if (_liveColorsAfterBegin.Contains(entry.Identity))
            {
                continue;
            }

            completedEntries[publishedIndex++] = entry;
        }

        _currentRequestId = 0;
        ClearBatch();
        publish(completedEntries);
        onCompleted();
        return true;
    }

    internal void RecordLiveColor(ulong identity)
    {
        if (identity == 0 ||
            _currentRequestId == 0 ||
            _declaredCount < 0 ||
            _isInvalid ||
            _liveColorsAfterBegin.Contains(identity))
        {
            return;
        }

        if (_liveColorsAfterBegin.Count >=
            NeonLetterSnapshotProtocol.MaxSnapshotEntries)
        {
            InvalidateBatch();
            return;
        }

        _liveColorsAfterBegin.Add(identity);
    }

    internal void RejectMalformedFrame(ulong requestId)
    {
        if (IsCurrentRequest(requestId))
        {
            InvalidateBatch();
        }
    }

    internal void Reset()
    {
        _currentRequestId = 0;
        ClearBatch();
    }

    private bool IsCurrentRequest(ulong requestId)
    {
        return requestId != 0 && requestId == _currentRequestId;
    }

    private void InvalidateBatch()
    {
        _isInvalid = true;
        _entries.Clear();
        _snapshotIdentities.Clear();
        _liveColorsAfterBegin.Clear();
    }

    private void ClearBatch()
    {
        _declaredCount = -1;
        _isInvalid = false;
        _entries.Clear();
        _snapshotIdentities.Clear();
        _liveColorsAfterBegin.Clear();
    }
}

internal enum NeonLetterSnapshotSendFrameKind : byte
{
    Begin = 1,
    Entry = 2,
    Complete = 3
}

internal readonly record struct NeonLetterSnapshotSendFrame(
    NeonLetterSnapshotSendFrameKind Kind,
    ulong RequestId,
    int Count,
    int Index,
    NeonLetterSnapshotEntry Entry);

internal sealed class NeonLetterSnapshotSendCoordinator<TConnection>
    where TConnection : notnull
{
    private readonly Dictionary<
        TConnection,
        LinkedListNode<SendJob>> _jobsByConnection = new();
    private readonly LinkedList<SendJob> _jobs = new();
    private LinkedListNode<SendJob>? _nextJob;

    internal int PendingJobCount => _jobsByConnection.Count;

    internal void Stage(
        TConnection connection,
        ulong requestId,
        Func<NeonLetterSnapshotEntry[]> snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        if (requestId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestId),
                requestId,
                "Snapshot request identity must be nonzero.");
        }

        if (_jobsByConnection.TryGetValue(
                connection,
                out LinkedListNode<SendJob>? existing))
        {
            existing.Value.Replace(requestId, snapshotProvider);
            _nextJob ??= existing;
            return;
        }

        var job = new SendJob(connection, requestId, snapshotProvider);
        LinkedListNode<SendJob> node = _jobs.AddLast(job);
        _jobsByConnection.Add(connection, node);
        _nextJob ??= node;
    }

    internal int Advance(
        int maxFrames,
        Func<TConnection, NeonLetterSnapshotSendFrame, bool> trySend,
        Action<TConnection, ulong, Exception>? onFreezeError = null)
    {
        ArgumentNullException.ThrowIfNull(trySend);
        if (maxFrames < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFrames),
                maxFrames,
                "Snapshot frame budget cannot be negative.");
        }

        if (maxFrames == 0 || _jobs.Count == 0)
        {
            return 0;
        }

        LinkedListNode<SendJob>? node =
            _nextJob?.List == _jobs
                ? _nextJob
                : _jobs.First;
        int attemptedCount = 0;
        while (attemptedCount < maxFrames && node != null)
        {
            LinkedListNode<SendJob> current = node;
            LinkedListNode<SendJob>? next = current.Next ?? _jobs.First;
            SendJob job = current.Value;
            int generation = job.Generation;
            NeonLetterSnapshotSendFrame frame;
            try
            {
                frame = job.CreateFrame();
            }
            catch (Exception exception)
            {
                Remove(current);
                node = next?.List == _jobs
                    ? next
                    : _jobs.First;
                if (onFreezeError == null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo
                        .Capture(exception)
                        .Throw();
                }

                onFreezeError(job.Connection, job.RequestId, exception);
                continue;
            }

            bool sent = trySend(job.Connection, frame);
            attemptedCount++;

            if (current.List == _jobs && job.Generation == generation)
            {
                if (!sent || job.MoveNext())
                {
                    Remove(current);
                }
            }

            node = next?.List == _jobs
                ? next
                : _jobs.First;
        }

        _nextJob = node?.List == _jobs
            ? node
            : _jobs.First;
        return attemptedCount;
    }

    internal void Clear()
    {
        _jobsByConnection.Clear();
        _jobs.Clear();
        _nextJob = null;
    }

    private void Remove(LinkedListNode<SendJob> node)
    {
        if (node.List != _jobs)
        {
            return;
        }

        _jobsByConnection.Remove(node.Value.Connection);
        _jobs.Remove(node);
    }

    private sealed class SendJob
    {
        private ulong _requestId;
        private Func<NeonLetterSnapshotEntry[]>? _snapshotProvider;
        private NeonLetterSnapshotEntry[]? _snapshot;
        private int _frameIndex;

        internal SendJob(
            TConnection connection,
            ulong requestId,
            Func<NeonLetterSnapshotEntry[]> snapshotProvider)
        {
            Connection = connection;
            _requestId = requestId;
            _snapshotProvider = snapshotProvider;
        }

        internal TConnection Connection { get; }
        internal int Generation { get; private set; }
        internal ulong RequestId => _requestId;

        internal void Replace(
            ulong requestId,
            Func<NeonLetterSnapshotEntry[]> snapshotProvider)
        {
            _requestId = requestId;
            _snapshotProvider = snapshotProvider;
            _snapshot = null;
            _frameIndex = 0;
            Generation++;
        }

        internal NeonLetterSnapshotSendFrame CreateFrame()
        {
            NeonLetterSnapshotEntry[] snapshot = GetOrFreezeSnapshot();
            if (_frameIndex == 0)
            {
                return new NeonLetterSnapshotSendFrame(
                    NeonLetterSnapshotSendFrameKind.Begin,
                    _requestId,
                    snapshot.Length,
                    Index: -1,
                    Entry: default);
            }

            if (_frameIndex <= snapshot.Length)
            {
                int entryIndex = _frameIndex - 1;
                return new NeonLetterSnapshotSendFrame(
                    NeonLetterSnapshotSendFrameKind.Entry,
                    _requestId,
                    snapshot.Length,
                    entryIndex,
                    snapshot[entryIndex]);
            }

            return new NeonLetterSnapshotSendFrame(
                NeonLetterSnapshotSendFrameKind.Complete,
                _requestId,
                snapshot.Length,
                Index: -1,
                Entry: default);
        }

        internal bool MoveNext()
        {
            NeonLetterSnapshotEntry[] snapshot =
                _snapshot ??
                throw new InvalidOperationException(
                    "The snapshot has not been frozen.");
            _frameIndex++;
            return _frameIndex > snapshot.Length + 1;
        }

        private NeonLetterSnapshotEntry[] GetOrFreezeSnapshot()
        {
            if (_snapshot != null)
            {
                return _snapshot;
            }

            Func<NeonLetterSnapshotEntry[]> snapshotProvider =
                _snapshotProvider ??
                throw new InvalidOperationException(
                    "The snapshot provider is unavailable.");
            NeonLetterSnapshotEntry[] snapshot =
                snapshotProvider() ??
                throw new InvalidOperationException(
                    "The snapshot provider returned no snapshot.");
            if (snapshot.Length >
                NeonLetterSnapshotProtocol.MaxSnapshotEntries)
            {
                throw new InvalidOperationException(
                    $"Snapshot cannot exceed " +
                    $"{NeonLetterSnapshotProtocol.MaxSnapshotEntries} " +
                    "entries.");
            }

            _snapshot = snapshot;
            _snapshotProvider = null;
            return snapshot;
        }
    }
}
