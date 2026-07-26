#nullable enable

namespace SOTFNeonLetters;

internal static class NeonLetterHostApplyProtocol
{
    public const int MaxCachedRequestsPerPeer = 256;
}

internal enum NeonLetterApplyStatus : byte
{
    Accepted,
    Rejected
}

internal readonly record struct NeonLetterApplyRequest<TKey>(
    byte ProtocolVersion,
    ulong RequestId,
    TKey Identity,
    NeonRgba Color)
    where TKey : notnull;

internal readonly record struct NeonLetterApplyResult<TKey>(
    ulong RequestId,
    TKey Identity,
    NeonLetterApplyStatus Status,
    NeonRgba AuthoritativeColor,
    ulong Revision)
    where TKey : notnull;

internal readonly record struct NeonLetterHostApplyOutcome<TKey>(
    NeonLetterApplyResult<TKey> Result,
    bool ShouldBroadcast)
    where TKey : notnull;

internal sealed class NeonLetterHostApplyCoordinator<TPeer, TKey>
    where TPeer : notnull
    where TKey : notnull
{
    private readonly NeonLetterAuthoritativeColors<TKey> _authoritative;
    private readonly Dictionary<TPeer, PeerRequestCache> _peerCaches = new();

    public NeonLetterHostApplyCoordinator(
        NeonLetterAuthoritativeColors<TKey> authoritative)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        _authoritative = authoritative;
    }

    public NeonLetterHostApplyOutcome<TKey> Process(
        TPeer peer,
        ulong requestId,
        TKey identity,
        bool isHost,
        bool isLive,
        int recipeId,
        NeonRgba color)
    {
        if (TryGetCached(peer, requestId, out NeonLetterApplyResult<TKey> cached))
        {
            return new NeonLetterHostApplyOutcome<TKey>(
                cached,
                ShouldBroadcast: false);
        }

        NeonLetterColorAcceptance acceptance = requestId == 0
            ? new NeonLetterColorAcceptance(
                false,
                _authoritative.Resolve(identity),
                _authoritative.ResolveState(identity).Revision)
            : _authoritative.TryAccept(
                isHost,
                identity,
                isLive,
                recipeId,
                color);
        var result = new NeonLetterApplyResult<TKey>(
            requestId,
            identity,
            acceptance.Accepted
                ? NeonLetterApplyStatus.Accepted
                : NeonLetterApplyStatus.Rejected,
            acceptance.AuthoritativeColor,
            acceptance.Revision);
        if (requestId != 0)
        {
            GetOrCreateCache(peer).Add(requestId, result);
        }

        return new NeonLetterHostApplyOutcome<TKey>(
            result,
            ShouldBroadcast: acceptance.Accepted);
    }

    public bool TryGetCached(
        TPeer peer,
        ulong requestId,
        out NeonLetterApplyResult<TKey> result)
    {
        if (requestId == 0)
        {
            result = default;
            return false;
        }

        if (_peerCaches.TryGetValue(peer, out PeerRequestCache? cache))
        {
            return cache.TryGet(requestId, out result);
        }

        result = default;
        return false;
    }

    public void Remove(TPeer peer)
    {
        _peerCaches.Remove(peer);
    }

    public void Clear()
    {
        _peerCaches.Clear();
    }

    private PeerRequestCache GetOrCreateCache(TPeer peer)
    {
        if (_peerCaches.TryGetValue(peer, out PeerRequestCache? cache))
        {
            return cache;
        }

        cache = new PeerRequestCache();
        _peerCaches.Add(peer, cache);
        return cache;
    }

    private sealed class PeerRequestCache
    {
        private readonly Dictionary<
            ulong,
            NeonLetterApplyResult<TKey>> _results = new(
                NeonLetterHostApplyProtocol.MaxCachedRequestsPerPeer);
        private readonly Queue<ulong> _requestOrder = new(
            NeonLetterHostApplyProtocol.MaxCachedRequestsPerPeer);

        public bool TryGet(
            ulong requestId,
            out NeonLetterApplyResult<TKey> result)
        {
            return _results.TryGetValue(requestId, out result);
        }

        public void Add(
            ulong requestId,
            NeonLetterApplyResult<TKey> result)
        {
            _results.Add(requestId, result);
            _requestOrder.Enqueue(requestId);
            if (_results.Count <=
                NeonLetterHostApplyProtocol.MaxCachedRequestsPerPeer)
            {
                return;
            }

            ulong evictedRequestId = _requestOrder.Dequeue();
            _results.Remove(evictedRequestId);
        }
    }
}

internal readonly record struct NeonLetterAuthoritativeColor(
    NeonRgba Color,
    ulong Revision);

internal enum NeonLetterClientApplyAction
{
    Ignored,
    Confirm,
    Rollback,
    ApplyAuthoritative
}

internal readonly record struct NeonLetterClientApplyDecision<TKey>(
    NeonLetterClientApplyAction Action,
    ulong RequestId,
    TKey Identity,
    NeonRgba Color,
    ulong Revision)
    where TKey : notnull;

internal sealed class NeonLetterClientApplyCoordinator<TKey>
    where TKey : notnull
{
    private readonly double _timeoutSeconds;
    private readonly Dictionary<TKey, PendingApply> _pending = new();
    private readonly Dictionary<TKey, NeonLetterAuthoritativeColor>
        _authoritative = new();
    private ulong _nextRequestId = 1;

    public NeonLetterClientApplyCoordinator(double timeoutSeconds)
    {
        if (!double.IsFinite(timeoutSeconds) || timeoutSeconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        _timeoutSeconds = timeoutSeconds;
    }

    public int PendingCount => _pending.Count;

    public NeonLetterApplyRequest<TKey> Start(
        TKey identity,
        NeonRgba color,
        double nowSeconds)
    {
        ValidateNow(nowSeconds);
        if (_nextRequestId == 0)
        {
            throw new InvalidOperationException(
                "The color request identifier space is exhausted.");
        }

        ulong requestId = _nextRequestId++;
        NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            NeonLetterNetworkProtocol.Pack(color));
        double expiresAtSeconds = nowSeconds + _timeoutSeconds;
        if (!double.IsFinite(expiresAtSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }

        _pending[identity] = new PendingApply(
            requestId,
            expiresAtSeconds);
        return new NeonLetterApplyRequest<TKey>(
            NeonLetterNetworkProtocol.CurrentVersion,
            requestId,
            identity,
            canonicalColor);
    }

    public NeonLetterClientApplyDecision<TKey> AcceptResult(
        NeonLetterApplyResult<TKey> result)
    {
        if (!_pending.TryGetValue(result.Identity, out PendingApply pending) ||
            pending.RequestId != result.RequestId)
        {
            return Ignored(result.Identity, result.RequestId);
        }

        _pending.Remove(result.Identity);
        UpdateAuthoritative(
            result.Identity,
            result.AuthoritativeColor,
            result.Revision);
        NeonLetterAuthoritativeColor authoritative =
            ResolveAuthoritative(result.Identity);
        return new NeonLetterClientApplyDecision<TKey>(
            result.Status == NeonLetterApplyStatus.Accepted
                ? NeonLetterClientApplyAction.Confirm
                : NeonLetterClientApplyAction.Rollback,
            result.RequestId,
            result.Identity,
            authoritative.Color,
            authoritative.Revision);
    }

    public NeonLetterClientApplyDecision<TKey> AcceptLive(
        TKey identity,
        NeonRgba color,
        ulong revision)
    {
        if (revision == 0 ||
            (_authoritative.TryGetValue(
                    identity,
                    out NeonLetterAuthoritativeColor current) &&
                revision <= current.Revision))
        {
            return Ignored(identity, requestId: 0);
        }

        NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            NeonLetterNetworkProtocol.Pack(color));
        _authoritative[identity] = new NeonLetterAuthoritativeColor(
            canonicalColor,
            revision);
        return new NeonLetterClientApplyDecision<TKey>(
            NeonLetterClientApplyAction.ApplyAuthoritative,
            RequestId: 0,
            identity,
            canonicalColor,
            revision);
    }

    public void SeedAuthoritative(TKey identity, NeonRgba color)
    {
        if (_authoritative.ContainsKey(identity))
        {
            return;
        }

        NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            NeonLetterNetworkProtocol.Pack(color));
        _authoritative.Add(
            identity,
            new NeonLetterAuthoritativeColor(
                canonicalColor,
                Revision: 0));
    }

    public IReadOnlyList<NeonLetterClientApplyDecision<TKey>> RejectTimedOut(
        double nowSeconds)
    {
        ValidateNow(nowSeconds);

        var decisions = new List<NeonLetterClientApplyDecision<TKey>>();
        foreach ((TKey identity, PendingApply pending) in _pending.ToArray())
        {
            if (nowSeconds < pending.ExpiresAtSeconds)
            {
                continue;
            }

            _pending.Remove(identity);
            NeonLetterAuthoritativeColor authoritative =
                ResolveAuthoritative(identity);
            decisions.Add(new NeonLetterClientApplyDecision<TKey>(
                NeonLetterClientApplyAction.Rollback,
                pending.RequestId,
                identity,
                authoritative.Color,
                authoritative.Revision));
        }

        return decisions;
    }

    public NeonLetterAuthoritativeColor ResolveAuthoritative(TKey identity)
    {
        return _authoritative.TryGetValue(
            identity,
            out NeonLetterAuthoritativeColor state)
                ? state
                : new NeonLetterAuthoritativeColor(
                    NeonRgba.ProjectCyan,
                    Revision: 0);
    }

    public ulong ResolvePendingRequestId(TKey identity)
    {
        return _pending.TryGetValue(identity, out PendingApply pending)
            ? pending.RequestId
            : 0;
    }

    public void Remove(TKey identity)
    {
        _pending.Remove(identity);
        _authoritative.Remove(identity);
    }

    public void Clear()
    {
        _pending.Clear();
        _authoritative.Clear();
        _nextRequestId = 1;
    }

    private void UpdateAuthoritative(
        TKey identity,
        NeonRgba color,
        ulong revision)
    {
        if (_authoritative.TryGetValue(
                identity,
                out NeonLetterAuthoritativeColor current) &&
            revision <= current.Revision)
        {
            return;
        }

        NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            NeonLetterNetworkProtocol.Pack(color));
        _authoritative[identity] = new NeonLetterAuthoritativeColor(
            canonicalColor,
            revision);
    }

    private static NeonLetterClientApplyDecision<TKey> Ignored(
        TKey identity,
        ulong requestId)
    {
        return new NeonLetterClientApplyDecision<TKey>(
            NeonLetterClientApplyAction.Ignored,
            requestId,
            identity,
            default,
            Revision: 0);
    }

    private static void ValidateNow(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }
    }

    private readonly record struct PendingApply(
        ulong RequestId,
        double ExpiresAtSeconds);
}
