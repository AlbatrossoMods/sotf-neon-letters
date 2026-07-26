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

    /// <summary>
    /// Deconstructs the acceptance using the original two-value public contract.
    /// </summary>
    public void Deconstruct(
        out bool accepted,
        out NeonRgba authoritativeColor)
    {
        accepted = Accepted;
        authoritativeColor = AuthoritativeColor;
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
    private readonly Dictionary<TKey, AuthoritativeEntry> _colors =
        new();
    private readonly SortedSet<IndexedEntry> _entriesByChangeSerial =
        new(IndexedEntryComparer.Instance);
    private ulong _lastChangeSerial;

    public NeonLetterAuthoritativeColors()
    {
    }

    internal NeonLetterAuthoritativeColors(ulong initialChangeSerial)
    {
        _lastChangeSerial = initialChangeSerial;
    }

    internal int CurrentEntryCount => _colors.Count;
    internal int IndexedEntryCount => _entriesByChangeSerial.Count;
    internal ulong CurrentChangeSerial => _lastChangeSerial;

    public NeonLetterColorAcceptance TryAccept(
        bool isHost,
        TKey identity,
        bool isLive,
        int recipeId,
        NeonRgba color)
    {
        if (!isHost || !isLive || !IsKnownRecipe(recipeId))
        {
            AuthoritativeEntry current = ResolveEntry(identity);
            return new NeonLetterColorAcceptance(
                false,
                current.Color,
                current.Revision);
        }

        uint packedColor = NeonLetterNetworkProtocol.Pack(color);
        NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            packedColor);
        AuthoritativeEntry currentState = ResolveEntry(identity);
        if (currentState.Revision == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The authoritative color revision space is exhausted.");
        }

        if (_lastChangeSerial == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The authoritative color change serial space is exhausted.");
        }

        ulong revision = currentState.Revision + 1;
        ulong changeSerial = _lastChangeSerial + 1;
        if (currentState.ChangeSerial != 0 &&
            !_entriesByChangeSerial.Remove(
                new IndexedEntry(currentState.ChangeSerial, identity)))
        {
            throw new InvalidOperationException(
                "The authoritative color serial index is inconsistent.");
        }

        var indexed = new IndexedEntry(changeSerial, identity);
        if (!_entriesByChangeSerial.Add(indexed))
        {
            if (currentState.ChangeSerial != 0)
            {
                _entriesByChangeSerial.Add(
                    new IndexedEntry(currentState.ChangeSerial, identity));
            }

            throw new InvalidOperationException(
                "The authoritative color change serial was reused.");
        }

        _colors[identity] = new AuthoritativeEntry(
            canonicalColor,
            revision,
            changeSerial);
        _lastChangeSerial = changeSerial;

        return new NeonLetterColorAcceptance(
            true,
            canonicalColor,
            revision);
    }

    internal NeonLetterAuthoritativeColorPage<TKey> CreatePage(
        ulong cursorChangeSerial,
        ulong watermarkChangeSerial)
    {
        ulong watermark = watermarkChangeSerial == 0
            ? _lastChangeSerial
            : watermarkChangeSerial;
        if (cursorChangeSerial > watermark ||
            watermark > _lastChangeSerial)
        {
            throw new ArgumentOutOfRangeException(
                nameof(watermarkChangeSerial),
                watermarkChangeSerial,
                "The page watermark must be within the current session.");
        }

        var entries = new List<NeonLetterColorPageEntry<TKey>>(
            Math.Min(_colors.Count, NeonLetterColorPageProtocol.MaxPageEntries));
        if (cursorChangeSerial == watermark)
        {
            return new NeonLetterAuthoritativeColorPage<TKey>(
                watermark,
                watermark,
                Complete: true,
                entries);
        }

        var lowerBound = new IndexedEntry(
            cursorChangeSerial + 1,
            default!);
        var upperBound = new IndexedEntry(watermark, default!);
        IEnumerator<IndexedEntry> enumerator = _entriesByChangeSerial
            .GetViewBetween(lowerBound, upperBound)
            .GetEnumerator();
        ulong nextCursor = watermark;
        bool hasMore;
        try
        {
            while (entries.Count < NeonLetterColorPageProtocol.MaxPageEntries &&
                   enumerator.MoveNext())
            {
                IndexedEntry indexed = enumerator.Current;
                AuthoritativeEntry current = _colors[indexed.Identity];
                entries.Add(new NeonLetterColorPageEntry<TKey>(
                    indexed.Identity,
                    current.Revision,
                    current.Color));
                nextCursor = indexed.ChangeSerial;
            }

            hasMore = enumerator.MoveNext();
        }
        finally
        {
            enumerator.Dispose();
        }

        return new NeonLetterAuthoritativeColorPage<TKey>(
            watermark,
            hasMore ? nextCursor : watermark,
            Complete: !hasMore,
            entries);
    }

    public NeonRgba Resolve(TKey identity)
    {
        return ResolveState(identity).Color;
    }

    internal NeonLetterAuthoritativeColor ResolveState(TKey identity)
    {
        AuthoritativeEntry state = ResolveEntry(identity);
        return new NeonLetterAuthoritativeColor(state.Color, state.Revision);
    }

    /// <summary>
    /// Removes one authoritative identity; repeated removals have no effect.
    /// </summary>
    public void Remove(TKey identity)
    {
        if (!_colors.TryGetValue(identity, out AuthoritativeEntry removed))
        {
            return;
        }

        if (!_entriesByChangeSerial.Remove(
                new IndexedEntry(removed.ChangeSerial, identity)))
        {
            throw new InvalidOperationException(
                "The authoritative color serial index is inconsistent.");
        }

        _colors.Remove(identity);
    }

    public void Clear()
    {
        _colors.Clear();
        _entriesByChangeSerial.Clear();
        _lastChangeSerial = 0;
    }

    private static bool IsKnownRecipe(int recipeId)
    {
        return NeonLetterSmallCatalog.All.Any(
            definition => definition.RecipeId == recipeId);
    }

    private AuthoritativeEntry ResolveEntry(TKey identity)
    {
        return _colors.TryGetValue(identity, out AuthoritativeEntry state)
            ? state
            : new AuthoritativeEntry(
                NeonRgba.ProjectCyan,
                Revision: 0,
                ChangeSerial: 0);
    }

    private readonly record struct AuthoritativeEntry(
        NeonRgba Color,
        ulong Revision,
        ulong ChangeSerial);

    private readonly record struct IndexedEntry(
        ulong ChangeSerial,
        TKey Identity);

    private sealed class IndexedEntryComparer : IComparer<IndexedEntry>
    {
        internal static readonly IndexedEntryComparer Instance = new();

        public int Compare(IndexedEntry left, IndexedEntry right)
        {
            // Session change serials are unique, so this is a total,
            // deterministic order for every valid index entry.
            return left.ChangeSerial.CompareTo(right.ChangeSerial);
        }
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
        TryEnqueue(identity, color, nowSeconds, persistent: false);
    }

    internal bool TryEnqueueTransient(
        TKey identity,
        NeonRgba color,
        double nowSeconds)
    {
        return TryEnqueue(identity, color, nowSeconds, persistent: false);
    }

    internal bool TryEnqueuePersistent(
        TKey identity,
        NeonRgba color,
        double nowSeconds)
    {
        return TryEnqueue(identity, color, nowSeconds, persistent: true);
    }

    private bool TryEnqueue(
        TKey identity,
        NeonRgba color,
        double nowSeconds,
        bool persistent)
    {
        ValidateNowSeconds(nowSeconds);
        double expiresAtSeconds = persistent
            ? double.PositiveInfinity
            : nowSeconds + _lifetimeSeconds;
        if (!persistent && !double.IsFinite(expiresAtSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "Pending color expiry must be finite.");
        }

        if (!_colors.ContainsKey(identity) && _colors.Count >= _capacity)
        {
            if (persistent || !RemoveOldestTransient())
            {
                return false;
            }
        }

        if (_colors.TryGetValue(
                identity,
                out LinkedListNode<PendingColor>? existing))
        {
            persistent |= existing.Value.IsPersistent;
            RemoveNode(existing);
        }

        var node = new LinkedListNode<PendingColor>(new PendingColor(
            identity,
            color,
            expiresAtSeconds,
            persistent));
        _pending.AddLast(node);
        _colors.Add(identity, node);
        _nextPending ??= node;
        return true;
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

    internal bool TryApply(
        TKey identity,
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);
        ValidateNowSeconds(nowSeconds);
        if (!_colors.TryGetValue(
                identity,
                out LinkedListNode<PendingColor>? node))
        {
            return false;
        }

        PendingColor pending = node.Value;
        if (!pending.IsPersistent &&
            nowSeconds >= pending.ExpiresAtSeconds)
        {
            RemoveNode(node);
            return false;
        }

        if (!isReady(identity))
        {
            return false;
        }

        apply(identity, pending.Color);
        if (IsCurrent(node, identity))
        {
            RemoveNode(node);
        }

        return true;
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
        LinkedListNode<PendingColor>? node =
            _nextPending?.List == _pending
                ? _nextPending
                : _pending.First;
        int nodesToInspect = _pending.Count;
        int inspectionBudget = maxItems == int.MaxValue
            ? nodesToInspect
            : Math.Min(nodesToInspect, maxItems);
        List<TKey> snapshot = _snapshotPool.Rent();
        int appliedCount = 0;
        try
        {
            for (int inspected = 0;
                 inspected < inspectionBudget &&
                 node != null;
                 inspected++)
            {
                LinkedListNode<PendingColor> current = node;
                TKey identity = current.Value.Identity;
                node = current.Next ?? _pending.First;
                if (!current.Value.IsPersistent &&
                    nowSeconds >= current.Value.ExpiresAtSeconds)
                {
                    RemoveNode(current);
                    node = node?.List == _pending
                        ? node
                        : _pending.First;
                    continue;
                }

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
            if (!node.Value.IsPersistent &&
                nowSeconds >= node.Value.ExpiresAtSeconds)
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

    private bool RemoveOldestTransient()
    {
        LinkedListNode<PendingColor>? node = _pending.First;
        if (node == null || node.Value.IsPersistent)
        {
            return false;
        }

        RemoveNode(node);
        return true;
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
        double ExpiresAtSeconds,
        bool IsPersistent);

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

        _pendingColors.Enqueue(identity, color, nowSeconds);
        return _pendingColors.TryApply(
            identity,
            nowSeconds,
            isReady,
            (candidateIdentity, candidateColor) =>
            {
                apply(candidateIdentity, candidateColor);
                _resolvedColors.Commit(candidateIdentity, candidateColor);
            });
    }

    internal bool TryReceivePersistent(
        TKey identity,
        NeonRgba color,
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);

        if (!_pendingColors.TryEnqueuePersistent(
                identity,
                color,
                nowSeconds))
        {
            return false;
        }

        _pendingColors.TryApply(
            identity,
            nowSeconds,
            isReady,
            (candidateIdentity, candidateColor) =>
            {
                apply(candidateIdentity, candidateColor);
                _resolvedColors.Commit(candidateIdentity, candidateColor);
            });
        return true;
    }

    internal bool TryReceiveAuthoritative(
        TKey identity,
        NeonRgba color,
        double nowSeconds,
        Func<TKey, bool> isReady,
        Action<TKey, NeonRgba> apply)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(apply);

        if (!_pendingColors.TryEnqueueTransient(
                identity,
                color,
                nowSeconds))
        {
            return false;
        }

        _pendingColors.TryApply(
            identity,
            nowSeconds,
            isReady,
            (candidateIdentity, candidateColor) =>
            {
                apply(candidateIdentity, candidateColor);
                _resolvedColors.Commit(candidateIdentity, candidateColor);
            });
        return true;
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
