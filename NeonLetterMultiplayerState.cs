#nullable enable

namespace SOTFNeonLetters;

public readonly record struct NeonLetterColorAcceptance(
    bool Accepted,
    NeonRgba AuthoritativeColor,
    ulong Revision)
{
    public NeonLetterColorAcceptance(
        bool accepted,
        NeonRgba authoritativeColor)
        : this(accepted, authoritativeColor, Revision: 0)
    {
    }
}

internal delegate bool NeonLetterPendingReadyCallback<TState, TKey>(
    ref TState state,
    TKey identity);

internal delegate void NeonLetterPendingApplyCallback<TState, TKey>(
    ref TState state,
    TKey identity,
    NeonRgba color);

internal delegate void NeonLetterPendingApplyErrorCallback<TState, TKey>(
    ref TState state,
    TKey identity,
    Exception exception);

public sealed class NeonLetterAuthoritativeColors<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, NeonLetterAuthoritativeColor> _colors =
        new();
    private ulong _revision;

    public NeonLetterColorAcceptance TryAccept(
        bool isHost,
        TKey identity,
        bool isLive,
        int recipeId,
        NeonRgba color)
    {
        if (!isHost || !isLive || !IsKnownRecipe(recipeId))
        {
            NeonLetterAuthoritativeColor current = ResolveState(identity);
            return new NeonLetterColorAcceptance(
                false,
                current.Color,
                current.Revision);
        }

        uint packedColor = NeonLetterNetworkProtocol.Pack(color);
        NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            packedColor);
        if (_revision == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The authoritative color revision space is exhausted.");
        }

        ulong revision = ++_revision;
        _colors[identity] = new NeonLetterAuthoritativeColor(
            canonicalColor,
            revision);

        return new NeonLetterColorAcceptance(
            true,
            canonicalColor,
            revision);
    }

    public IReadOnlyList<KeyValuePair<TKey, NeonRgba>> Snapshot(
        Func<TKey, bool> isLive)
    {
        ArgumentNullException.ThrowIfNull(isLive);

        var snapshot = new List<KeyValuePair<TKey, NeonRgba>>(_colors.Count);
        var deadIdentities = new List<TKey>();
        foreach (KeyValuePair<TKey, NeonLetterAuthoritativeColor> entry in _colors)
        {
            if (isLive(entry.Key))
            {
                snapshot.Add(
                    new KeyValuePair<TKey, NeonRgba>(
                        entry.Key,
                        entry.Value.Color));
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
        return ResolveState(identity).Color;
    }

    internal NeonLetterAuthoritativeColor ResolveState(TKey identity)
    {
        return _colors.TryGetValue(
            identity,
            out NeonLetterAuthoritativeColor state)
                ? state
                : new NeonLetterAuthoritativeColor(
                    NeonRgba.ProjectCyan,
                    Revision: 0);
    }

    /// <summary>
    /// Removes one authoritative identity; repeated removals have no effect.
    /// </summary>
    public void Remove(TKey identity)
    {
        _colors.Remove(identity);
    }

    public void Clear()
    {
        _colors.Clear();
        _revision = 0;
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
    private static readonly NeonLetterPendingReadyCallback<
        DirectCallbackState,
        TKey> DirectIsReady =
            static (ref DirectCallbackState state, TKey identity) =>
                state.IsReady(identity);
    private static readonly NeonLetterPendingApplyCallback<
        DirectCallbackState,
        TKey> DirectApply =
            static (
                ref DirectCallbackState state,
                TKey identity,
                NeonRgba color) =>
                    state.Apply(identity, color);
    private static readonly NeonLetterPendingApplyErrorCallback<
        DirectCallbackState,
        TKey> DirectOnApplyError =
            static (
                ref DirectCallbackState state,
                TKey identity,
                Exception exception) =>
                    state.OnApplyError!(identity, exception);

    private readonly int _capacity;
    private readonly double _lifetimeSeconds;
    private readonly Dictionary<TKey, LinkedListNode<PendingColor>> _colors =
        new();
    private readonly LinkedList<PendingColor> _pending = new();
    private readonly NeonLetterReentrantSnapshotPool<TKey> _snapshotPool =
        new();
    private LinkedListNode<PendingColor>? _nextPending;

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

        if (_colors.TryGetValue(
                identity,
                out LinkedListNode<PendingColor>? existing))
        {
            RemoveNode(existing);
        }

        var node = new LinkedListNode<PendingColor>(new PendingColor(
            identity,
            color,
            expiresAtSeconds));
        _pending.AddLast(node);
        _colors.Add(identity, node);
        _nextPending ??= node;
    }

    internal void EnsureCanRetainBatch<TEntry>(
        IReadOnlyList<TEntry> entries,
        double nowSeconds,
        Func<TEntry, TKey> getIdentity)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(getIdentity);
        ValidateNowSeconds(nowSeconds);
        PruneExpired(nowSeconds);

        int availableCount = _capacity - _colors.Count;
        var additionalIdentities = new HashSet<TKey>();
        foreach (TEntry entry in entries)
        {
            TKey identity = getIdentity(entry);
            if (!_colors.ContainsKey(identity) &&
                additionalIdentities.Add(identity) &&
                additionalIdentities.Count > availableCount)
            {
                throw new InvalidOperationException(
                    "The complete snapshot batch cannot be retained without " +
                    "discarding pending replicated color state.");
            }
        }
    }

    public int ApplyReady(
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);
        ValidateNowSeconds(nowSeconds);

        if (_colors.Count == 0)
        {
            return 0;
        }

        var callbackState = new DirectCallbackState(
            isReady,
            apply,
            OnApplyError: null);
        return ApplyReady(
            nowSeconds,
            int.MaxValue,
            ref callbackState,
            DirectIsReady,
            DirectApply,
            onApplyError: null);
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

        return ApplyReadyContinuing(
            nowSeconds,
            int.MaxValue,
            isReady,
            apply,
            onApplyError);
    }

    /// <summary>
    /// Applies at most the requested pending entries while isolating failures.
    /// </summary>
    public int ApplyReadyContinuing(
        double nowSeconds,
        int maxItems,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply,
        Action<TKey, Exception> onApplyError)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(onApplyError);
        var callbackState = new DirectCallbackState(
            isReady,
            apply,
            onApplyError);
        return ApplyReadyContinuing(
            nowSeconds,
            maxItems,
            ref callbackState,
            DirectIsReady,
            DirectApply,
            DirectOnApplyError);
    }

    internal int ApplyReadyContinuing<TState>(
        double nowSeconds,
        int maxItems,
        ref TState callbackState,
        NeonLetterPendingReadyCallback<TState, TKey> isReady,
        NeonLetterPendingApplyCallback<TState, TKey> apply,
        NeonLetterPendingApplyErrorCallback<TState, TKey>? onApplyError)
    {
        if (maxItems < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxItems),
                maxItems,
                "Pending color item budget cannot be negative.");
        }

        ValidateNowSeconds(nowSeconds);
        if (_colors.Count == 0 || maxItems == 0)
        {
            return 0;
        }

        return ApplyReady(
            nowSeconds,
            maxItems,
            ref callbackState,
            isReady,
            apply,
            onApplyError);
    }

    private int ApplyReady<TState>(
        double nowSeconds,
        int maxItems,
        ref TState callbackState,
        NeonLetterPendingReadyCallback<TState, TKey> isReady,
        NeonLetterPendingApplyCallback<TState, TKey> apply,
        NeonLetterPendingApplyErrorCallback<TState, TKey>? onApplyError)
    {
        PruneExpired(nowSeconds);
        if (_colors.Count == 0)
        {
            return 0;
        }

        LinkedListNode<PendingColor>? node =
            _nextPending?.List == _pending
                ? _nextPending
                : _pending.First;
        int nodesToInspect = _pending.Count;
        List<TKey> snapshot = _snapshotPool.Rent();
        int appliedCount = 0;
        try
        {
            for (int inspected = 0;
                 inspected < nodesToInspect &&
                 snapshot.Count < maxItems &&
                 node != null;
                 inspected++)
            {
                TKey identity = node.Value.Identity;
                node = node.Next ?? _pending.First;
                if (!_snapshotPool.IsReservedByOuterBuffer(identity))
                {
                    snapshot.Add(identity);
                }
            }

            _nextPending = node;
            foreach (TKey identity in snapshot)
            {
                if (!_colors.TryGetValue(
                        identity,
                        out LinkedListNode<PendingColor>? currentNode) ||
                    !isReady(ref callbackState, identity))
                {
                    continue;
                }

                PendingColor pending = currentNode.Value;
                try
                {
                    apply(ref callbackState, identity, pending.Color);
                }
                catch (Exception exception)
                {
                    if (onApplyError == null)
                    {
                        throw;
                    }

                    onApplyError(
                        ref callbackState,
                        identity,
                        exception);
                    continue;
                }

                if (IsCurrent(currentNode, identity))
                {
                    RemoveNode(currentNode);
                }

                appliedCount++;
            }
        }
        finally
        {
            _snapshotPool.Return(snapshot);
        }

        return appliedCount;
    }

    public void Prune(double nowSeconds)
    {
        ValidateNowSeconds(nowSeconds);
        if (_colors.Count == 0)
        {
            return;
        }

        PruneExpired(nowSeconds);
    }

    /// <summary>
    /// Removes one pending identity; repeated removals have no effect.
    /// </summary>
    public void Remove(TKey identity)
    {
        if (_colors.TryGetValue(
                identity,
                out LinkedListNode<PendingColor>? node))
        {
            RemoveNode(node);
        }
    }

    private void PruneExpired(double nowSeconds)
    {
        LinkedListNode<PendingColor>? node = _pending.First;
        while (node != null)
        {
            LinkedListNode<PendingColor>? next = node.Next;
            if (nowSeconds >= node.Value.ExpiresAtSeconds)
            {
                RemoveNode(node);
            }

            node = next;
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
        _pending.Clear();
        _nextPending = null;
    }

    private void RemoveOldest()
    {
        if (_pending.First != null)
        {
            RemoveNode(_pending.First);
        }
    }

    private bool IsCurrent(
        LinkedListNode<PendingColor> node,
        TKey identity)
    {
        return _colors.TryGetValue(
                   identity,
                   out LinkedListNode<PendingColor>? current) &&
               ReferenceEquals(current, node);
    }

    private void RemoveNode(LinkedListNode<PendingColor> node)
    {
        if (node.List != _pending)
        {
            return;
        }

        LinkedListNode<PendingColor>? next =
            node.Next ?? _pending.First;
        if (_colors.TryGetValue(
                node.Value.Identity,
                out LinkedListNode<PendingColor>? current) &&
            ReferenceEquals(current, node))
        {
            _colors.Remove(node.Value.Identity);
        }

        _pending.Remove(node);
        if (ReferenceEquals(_nextPending, node))
        {
            _nextPending = next?.List == _pending
                ? next
                : _pending.First;
        }
    }

    private readonly record struct PendingColor(
        TKey Identity,
        NeonRgba Color,
        double ExpiresAtSeconds);

    private readonly record struct DirectCallbackState(
        Func<TKey, bool> IsReady,
        Action<TKey, NeonRgba> Apply,
        Action<TKey, Exception>? OnApplyError);
}

public sealed class NeonLetterReplicatedColorState<TKey>
    where TKey : notnull
{
    private static readonly NeonLetterPendingReadyCallback<
        DrainCallbackState,
        TKey> DrainIsReady =
            static (ref DrainCallbackState state, TKey identity) =>
                state.IsReady(identity);
    private static readonly NeonLetterPendingApplyCallback<
        DrainCallbackState,
        TKey> DrainApply =
            static (
                ref DrainCallbackState state,
                TKey identity,
                NeonRgba color) =>
            {
                state.Apply(identity, color);
                state.Owner._resolvedColors.Commit(identity, color);
            };
    private static readonly NeonLetterPendingApplyErrorCallback<
        DrainCallbackState,
        TKey> DrainOnApplyError =
            static (
                ref DrainCallbackState state,
                TKey identity,
                Exception exception) =>
            {
                if (state.OnApplyError == null)
                {
                    state.FirstApplyError ??= exception;
                    return;
                }

                state.OnApplyError(identity, exception);
            };

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

    internal int ReceiveBatch<TEntry>(
        IReadOnlyList<TEntry> entries,
        double nowSeconds,
        Func<TEntry, TKey> getIdentity,
        Func<TEntry, NeonRgba> getColor,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(getIdentity);
        ArgumentNullException.ThrowIfNull(getColor);
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);

        _pendingColors.EnsureCanRetainBatch(
            entries,
            nowSeconds,
            getIdentity);
        if (entries.Count == 0)
        {
            return 0;
        }

        foreach (TEntry entry in entries)
        {
            _pendingColors.Enqueue(
                getIdentity(entry),
                getColor(entry),
                nowSeconds);
        }

        return _pendingColors.ApplyReady(
            nowSeconds,
            isReady,
            (identity, color) =>
            {
                apply(identity, color);
                _resolvedColors.Commit(identity, color);
            });
    }

    public int DrainReady(
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);

        var callbackState = new DrainCallbackState(
            this,
            isReady,
            apply,
            onApplyError: null);
        int appliedCount = _pendingColors.ApplyReadyContinuing(
            nowSeconds,
            int.MaxValue,
            ref callbackState,
            DrainIsReady,
            DrainApply,
            DrainOnApplyError);
        if (callbackState.FirstApplyError != null)
        {
            throw callbackState.FirstApplyError;
        }

        return appliedCount;
    }

    public int DrainReady(
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply,
        Action<TKey, Exception> onApplyError)
    {
        return DrainReady(
            nowSeconds,
            int.MaxValue,
            isReady,
            apply,
            onApplyError);
    }

    /// <summary>
    /// Drains at most the requested pending identities in one update.
    /// </summary>
    public int DrainReady(
        double nowSeconds,
        int maxItems,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply,
        Action<TKey, Exception> onApplyError)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(onApplyError);

        var callbackState = new DrainCallbackState(
            this,
            isReady,
            apply,
            onApplyError);
        return _pendingColors.ApplyReadyContinuing(
            nowSeconds,
            maxItems,
            ref callbackState,
            DrainIsReady,
            DrainApply,
            DrainOnApplyError);
    }

    public NeonRgba Resolve(TKey identity)
    {
        return _resolvedColors.Resolve(identity);
    }

    /// <summary>
    /// Removes one identity from both resolved and pending replicated state.
    /// </summary>
    public void Remove(TKey identity)
    {
        _resolvedColors.Remove(identity);
        _pendingColors.Remove(identity);
    }

    public void Clear()
    {
        _resolvedColors.Clear();
        _pendingColors.Clear();
    }

    private struct DrainCallbackState
    {
        public DrainCallbackState(
            NeonLetterReplicatedColorState<TKey> owner,
            Func<TKey, bool> isReady,
            Action<TKey, NeonRgba> apply,
            Action<TKey, Exception>? onApplyError)
        {
            Owner = owner;
            IsReady = isReady;
            Apply = apply;
            OnApplyError = onApplyError;
            FirstApplyError = null;
        }

        public NeonLetterReplicatedColorState<TKey> Owner { get; }
        public Func<TKey, bool> IsReady { get; }
        public Action<TKey, NeonRgba> Apply { get; }
        public Action<TKey, Exception>? OnApplyError { get; }
        public Exception? FirstApplyError { get; set; }
    }
}
