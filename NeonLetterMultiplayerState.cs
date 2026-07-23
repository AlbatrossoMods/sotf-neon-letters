#nullable enable

namespace SOTFNeonLetters;

public readonly record struct NeonLetterColorAcceptance(
    bool Accepted,
    NeonRgba AuthoritativeColor);

public sealed class NeonLetterAuthoritativeColors<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, NeonRgba> _colors = new();

    public NeonLetterColorAcceptance TryAccept(
        bool isHost,
        TKey identity,
        bool isLive,
        int recipeId,
        NeonRgba color)
    {
        if (!isHost || !isLive || !IsKnownRecipe(recipeId))
        {
            return new NeonLetterColorAcceptance(false, Resolve(identity));
        }

        uint packedColor = NeonLetterNetworkProtocol.Pack(color);
        NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            packedColor);
        _colors[identity] = canonicalColor;

        return new NeonLetterColorAcceptance(true, canonicalColor);
    }

    public IReadOnlyList<KeyValuePair<TKey, NeonRgba>> Snapshot(
        Func<TKey, bool> isLive)
    {
        ArgumentNullException.ThrowIfNull(isLive);

        var snapshot = new List<KeyValuePair<TKey, NeonRgba>>(_colors.Count);
        var deadIdentities = new List<TKey>();
        foreach (KeyValuePair<TKey, NeonRgba> entry in _colors)
        {
            if (isLive(entry.Key))
            {
                snapshot.Add(entry);
            }
            else
            {
                deadIdentities.Add(entry.Key);
            }
        }

        foreach (TKey identity in deadIdentities)
        {
            _colors.Remove(identity);
        }

        return snapshot;
    }

    public NeonRgba Resolve(TKey identity)
    {
        return _colors.TryGetValue(identity, out NeonRgba color)
            ? color
            : NeonRgba.ProjectCyan;
    }

    public void Clear()
    {
        _colors.Clear();
    }

    private static bool IsKnownRecipe(int recipeId)
    {
        return NeonLetterSmallCatalog.All.Any(
            definition => definition.RecipeId == recipeId);
    }
}

public sealed class NeonLetterPendingColors<TKey>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly double _lifetimeSeconds;
    private readonly Dictionary<TKey, PendingColor> _colors = new();
    private long _nextSequence;

    public NeonLetterPendingColors(int capacity, double lifetimeSeconds)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Pending color capacity must be positive.");
        }

        if (!double.IsFinite(lifetimeSeconds) || lifetimeSeconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetimeSeconds),
                lifetimeSeconds,
                "Pending color lifetime must be finite and positive.");
        }

        _capacity = capacity;
        _lifetimeSeconds = lifetimeSeconds;
    }

    public int Count => _colors.Count;

    public void Enqueue(TKey identity, NeonRgba color, double nowSeconds)
    {
        ValidateNowSeconds(nowSeconds);
        double expiresAtSeconds = nowSeconds + _lifetimeSeconds;
        if (!double.IsFinite(expiresAtSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "Pending color expiry must be finite.");
        }

        if (!_colors.ContainsKey(identity) && _colors.Count >= _capacity)
        {
            RemoveOldest();
        }

        _colors[identity] = new PendingColor(
            color,
            expiresAtSeconds,
            _nextSequence++);
    }

    public int ApplyReady(
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);

        ValidateNowSeconds(nowSeconds);
        PruneExpired(nowSeconds);
        TKey[] pendingIdentities = _colors
            .OrderBy(entry => entry.Value.Sequence)
            .Select(entry => entry.Key)
            .ToArray();
        int appliedCount = 0;
        foreach (TKey identity in pendingIdentities)
        {
            if (!_colors.TryGetValue(identity, out PendingColor pending) ||
                !isReady(identity))
            {
                continue;
            }

            apply(identity, pending.Color);
            if (_colors.TryGetValue(identity, out PendingColor current) &&
                current.Sequence == pending.Sequence)
            {
                _colors.Remove(identity);
            }

            appliedCount++;
        }

        return appliedCount;
    }

    public int ApplyReadyContinuing(
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply,
        Action<TKey, Exception> onApplyError)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(onApplyError);

        ValidateNowSeconds(nowSeconds);
        PruneExpired(nowSeconds);
        TKey[] pendingIdentities = _colors
            .OrderBy(entry => entry.Value.Sequence)
            .Select(entry => entry.Key)
            .ToArray();
        int appliedCount = 0;
        foreach (TKey identity in pendingIdentities)
        {
            if (!_colors.TryGetValue(identity, out PendingColor pending) ||
                !isReady(identity))
            {
                continue;
            }

            try
            {
                apply(identity, pending.Color);
            }
            catch (Exception exception)
            {
                onApplyError(identity, exception);
                continue;
            }

            if (_colors.TryGetValue(identity, out PendingColor current) &&
                current.Sequence == pending.Sequence)
            {
                _colors.Remove(identity);
            }

            appliedCount++;
        }

        return appliedCount;
    }

    public void Prune(double nowSeconds)
    {
        ValidateNowSeconds(nowSeconds);
        PruneExpired(nowSeconds);
    }

    private void PruneExpired(double nowSeconds)
    {
        TKey[] expiredIdentities = _colors
            .Where(entry => nowSeconds >= entry.Value.ExpiresAtSeconds)
            .Select(entry => entry.Key)
            .ToArray();
        foreach (TKey identity in expiredIdentities)
        {
            _colors.Remove(identity);
        }
    }

    private static void ValidateNowSeconds(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "Pending color time must be finite.");
        }
    }

    public void Clear()
    {
        _colors.Clear();
    }

    private void RemoveOldest()
    {
        TKey oldestIdentity = default!;
        long oldestSequence = long.MaxValue;
        foreach (KeyValuePair<TKey, PendingColor> entry in _colors)
        {
            if (entry.Value.Sequence < oldestSequence)
            {
                oldestIdentity = entry.Key;
                oldestSequence = entry.Value.Sequence;
            }
        }

        _colors.Remove(oldestIdentity);
    }

    private readonly record struct PendingColor(
        NeonRgba Color,
        double ExpiresAtSeconds,
        long Sequence);
}

public sealed class NeonLetterReplicatedColorState<TKey>
    where TKey : notnull
{
    private readonly NeonLetterSessionColors<TKey> _resolvedColors = new();
    private readonly NeonLetterPendingColors<TKey> _pendingColors;

    public NeonLetterReplicatedColorState(
        int pendingCapacity,
        double pendingLifetimeSeconds)
    {
        _pendingColors = new NeonLetterPendingColors<TKey>(
            pendingCapacity,
            pendingLifetimeSeconds);
    }

    public int PendingCount => _pendingColors.Count;

    public bool Receive(
        TKey identity,
        NeonRgba color,
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);

        _pendingColors.Prune(nowSeconds);
        _pendingColors.Enqueue(identity, color, nowSeconds);
        int appliedCount = _pendingColors.ApplyReady(
            nowSeconds,
            candidateIdentity =>
                EqualityComparer<TKey>.Default.Equals(
                    candidateIdentity,
                    identity) &&
                isReady(candidateIdentity),
            (candidateIdentity, candidateColor) =>
            {
                apply(candidateIdentity, candidateColor);
                _resolvedColors.Commit(candidateIdentity, candidateColor);
            });
        return appliedCount == 1;
    }

    public int DrainReady(
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        Exception? firstApplyError = null;
        int appliedCount = DrainReady(
            nowSeconds,
            isReady,
            apply,
            (_, exception) => firstApplyError ??= exception);
        if (firstApplyError != null)
        {
            throw firstApplyError;
        }

        return appliedCount;
    }

    public int DrainReady(
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply,
        Action<TKey, Exception> onApplyError)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(onApplyError);

        return _pendingColors.ApplyReadyContinuing(
            nowSeconds,
            isReady,
            (identity, color) =>
            {
                apply(identity, color);
                _resolvedColors.Commit(identity, color);
            },
            onApplyError);
    }

    public NeonRgba Resolve(TKey identity)
    {
        return _resolvedColors.Resolve(identity);
    }

    public void Clear()
    {
        _resolvedColors.Clear();
        _pendingColors.Clear();
    }
}
