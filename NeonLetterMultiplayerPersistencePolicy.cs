#nullable enable

namespace SOTFNeonLetters;

public sealed class NeonVector3
{
    public NeonVector3()
    {
    }

    public NeonVector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public sealed class NeonQuaternion
{
    public NeonQuaternion()
    {
    }

    public NeonQuaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
}

public sealed class NeonLetterMultiplayerSaveEntry
{
    public int RecipeId { get; set; }
    public int NativeSaveId { get; set; }
    public NeonVector3 Position { get; set; } = new();
    public NeonQuaternion Rotation { get; set; } = new();
    public uint PackedColor { get; set; }
}

public sealed class NeonLetterMultiplayerSaveEnvelope
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<NeonLetterMultiplayerSaveEntry> Entries { get; set; } = new();
}

public enum NeonLetterMultiplayerRestoreDecision
{
    UseNative,
    SpawnFallback,
    Skip
}

public sealed class NeonLetterMultiplayerRestoreEntryState
{
    private readonly int _recipeId;
    private bool _nativeRecipeMismatch;

    public NeonLetterMultiplayerRestoreEntryState(
        NeonLetterMultiplayerSaveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _recipeId = entry.RecipeId;
    }

    public bool FallbackSpawnStarted { get; private set; }

    public NeonLetterMultiplayerRestoreDecision Decide(
        bool nativeIdentityResolved,
        int resolvedRecipeId)
    {
        if (FallbackSpawnStarted || _nativeRecipeMismatch)
        {
            return NeonLetterMultiplayerRestoreDecision.Skip;
        }

        if (!nativeIdentityResolved)
        {
            return NeonLetterMultiplayerRestoreDecision.SpawnFallback;
        }

        if (resolvedRecipeId == _recipeId)
        {
            return NeonLetterMultiplayerRestoreDecision.UseNative;
        }

        _nativeRecipeMismatch = true;
        return NeonLetterMultiplayerRestoreDecision.Skip;
    }

    public void MarkFallbackSpawnStarted()
    {
        FallbackSpawnStarted = true;
    }
}

public enum NeonLetterMultiplayerRestoreRole
{
    Unknown,
    Host,
    Client,
    SinglePlayer
}

public enum NeonLetterMultiplayerRestoreObservationKind
{
    ProcessedRecipeUnavailable,
    FallbackPrefabUnavailable,
    NativeRecipeUnavailable,
    NativeTargetUnavailable,
    NativeRecipeMismatch,
    NativeTargetReady,
    ReadyToSpawnFallback,
    FallbackTargetUnavailable,
    FallbackTargetReady
}

public readonly record struct NeonLetterMultiplayerRestoreObservation<TTarget>(
    NeonLetterMultiplayerRestoreObservationKind Kind,
    TTarget? Target = null,
    int? ResolvedRecipeId = null)
    where TTarget : class;

internal sealed class NeonLetterMultiplayerRestoreEntrySnapshot
{
    internal NeonLetterMultiplayerRestoreEntrySnapshot(
        NeonLetterMultiplayerSaveEntry entry)
    {
        RecipeId = entry.RecipeId;
        NativeSaveId = entry.NativeSaveId;
        RestoreEntry = new NeonLetterMultiplayerSaveEntry
        {
            RecipeId = entry.RecipeId,
            NativeSaveId = entry.NativeSaveId,
            Position = new NeonVector3(
                entry.Position.X,
                entry.Position.Y,
                entry.Position.Z),
            Rotation = new NeonQuaternion(
                entry.Rotation.X,
                entry.Rotation.Y,
                entry.Rotation.Z,
                entry.Rotation.W),
            PackedColor = entry.PackedColor
        };
    }

    internal int RecipeId { get; }
    internal int NativeSaveId { get; }

    // This owned callback view is not authoritative restore state.
    internal NeonLetterMultiplayerSaveEntry RestoreEntry { get; }
}

internal sealed class NeonLetterMultiplayerRestoreSnapshot
{
    private static readonly NeonLetterMultiplayerRestoreSnapshot EmptySnapshot =
        new(
            new List<NeonLetterMultiplayerRestoreEntrySnapshot>(),
            onEntryTransferred: null);
    private readonly IReadOnlyList<NeonLetterMultiplayerRestoreEntrySnapshot>
        _entries;
    private readonly Action<int>? _onEntryTransferred;

    private NeonLetterMultiplayerRestoreSnapshot(
        List<NeonLetterMultiplayerRestoreEntrySnapshot> entries,
        Action<int>? onEntryTransferred)
    {
        _entries = entries.AsReadOnly();
        _onEntryTransferred = onEntryTransferred;
    }

    internal static NeonLetterMultiplayerRestoreSnapshot Empty =>
        EmptySnapshot;

    internal IReadOnlyList<NeonLetterMultiplayerRestoreEntrySnapshot> Entries =>
        _entries;

    internal static NeonLetterMultiplayerRestoreSnapshot Sanitize(
        NeonLetterMultiplayerSaveEnvelope? envelope,
        Action? onEntryVisited = null,
        Action<int>? onEntryTransferred = null)
    {
        if (envelope == null ||
            envelope.Version != NeonLetterMultiplayerSaveEnvelope.CurrentVersion ||
            envelope.Entries == null)
        {
            return Empty;
        }

        var entries =
            new List<NeonLetterMultiplayerRestoreEntrySnapshot>(
                envelope.Entries.Count);
        foreach (NeonLetterMultiplayerSaveEntry? entry in envelope.Entries)
        {
            onEntryVisited?.Invoke();
            NeonLetterMultiplayerRestoreEntrySnapshot? snapshotEntry =
                NeonLetterMultiplayerPersistencePolicy
                    .CreateRestoreSnapshotEntry(entry);
            if (snapshotEntry != null)
            {
                entries.Add(snapshotEntry);
            }
        }

        return entries.Count == 0
            ? Empty
            : new NeonLetterMultiplayerRestoreSnapshot(
                entries,
                onEntryTransferred);
    }

    internal void NotifyEntryTransferred(int transferredCount)
    {
        _onEntryTransferred?.Invoke(transferredCount);
    }

    internal NeonLetterMultiplayerSaveEnvelope ToEnvelope()
    {
        return new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = _entries
                .Select(entry => entry.RestoreEntry)
                .ToList()
        };
    }
}

internal sealed class NeonLetterMultiplayerRestoreLoadQueue
{
    private readonly object _sync = new();
    private readonly Func<
        NeonLetterMultiplayerSaveEnvelope?,
        NeonLetterMultiplayerRestoreSnapshot> _sanitize;
    private readonly NeonLetterMonotonicSequence _sequences = new();
    private NeonLetterMultiplayerRestoreSnapshot? _pending;
    private bool _accepting = true;
    private object _generation = new();
    private ulong _latestPublishedSequence;

    internal NeonLetterMultiplayerRestoreLoadQueue()
        : this(
            envelope =>
                NeonLetterMultiplayerRestoreSnapshot.Sanitize(envelope))
    {
    }

    internal NeonLetterMultiplayerRestoreLoadQueue(
        Func<
            NeonLetterMultiplayerSaveEnvelope?,
            NeonLetterMultiplayerRestoreSnapshot> sanitize)
    {
        ArgumentNullException.ThrowIfNull(sanitize);
        _sanitize = sanitize;
    }

    internal bool HasPending
    {
        get
        {
            lock (_sync)
            {
                return _pending != null;
            }
        }
    }

    internal bool Enqueue(NeonLetterMultiplayerSaveEnvelope? envelope)
    {
        return Enqueue(envelope, out _);
    }

    internal bool Enqueue(
        NeonLetterMultiplayerSaveEnvelope? envelope,
        out ulong sequence)
    {
        object generation;
        lock (_sync)
        {
            if (!_accepting)
            {
                sequence = 0;
                return false;
            }

            sequence = _sequences.Advance();
            generation = _generation;
        }

        NeonLetterMultiplayerRestoreSnapshot snapshot = _sanitize(envelope);
        lock (_sync)
        {
            if (!_accepting ||
                !ReferenceEquals(generation, _generation))
            {
                return false;
            }

            if (sequence > _latestPublishedSequence)
            {
                _latestPublishedSequence = sequence;
                _pending = snapshot;
            }

            return true;
        }
    }

    internal bool TryDequeue(
        out NeonLetterMultiplayerRestoreSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_pending == null)
            {
                snapshot = NeonLetterMultiplayerRestoreSnapshot.Empty;
                return false;
            }

            snapshot = _pending;
            _pending = null;
            return true;
        }
    }

    internal void Clear()
    {
        lock (_sync)
        {
            _generation = new object();
            _pending = null;
        }
    }

    internal void SuspendAndClear()
    {
        lock (_sync)
        {
            _accepting = false;
            _generation = new object();
            _pending = null;
        }
    }

    internal void Resume()
    {
        lock (_sync)
        {
            _accepting = true;
        }
    }
}

internal sealed class NeonLetterDetachedRestoreCleanup
{
    private Action? _rollback;

    internal NeonLetterDetachedRestoreCleanup(Action? rollback)
    {
        _rollback = rollback;
    }

    internal void Rollback()
    {
        Interlocked.Exchange(ref _rollback, null)?.Invoke();
    }

    internal void Abandon()
    {
        Interlocked.Exchange(ref _rollback, null);
    }
}

public sealed class NeonLetterMultiplayerRestoreCoordinator<TTarget>
    where TTarget : class
{
    internal const int MaxItemsPerUpdate = 16;
    internal const int MaxFallbackSpawnsPerUpdate = 2;

    private readonly LinkedList<PendingRestore> _pending = new();
    private readonly NeonLetterReentrantSnapshotPool<
        LinkedListNode<PendingRestore>> _snapshotPool =
            new();
    private readonly NeonLetterMonotonicSequence _epochs;
    private NeonLetterMultiplayerRestoreSnapshot _stagedSnapshot =
        NeonLetterMultiplayerRestoreSnapshot.Empty;
    private LinkedListNode<PendingRestore>? _nextPending;
    private NeonLetterMultiplayerRestoreRole _role;
    private bool _hasStagedEnvelope;
    private int _startedFallbackCount;
    private ulong _readinessToken;
    private int _readinessWaveAttemptsRemaining;
    private bool _isAdvancing;
    private ulong? _deferredReadinessToken;
    private bool _canAdvance = true;
    private ulong _lastAbandonmentEpoch;

    public NeonLetterMultiplayerRestoreCoordinator()
        : this(new NeonLetterMonotonicSequence())
    {
    }

    internal NeonLetterMultiplayerRestoreCoordinator(
        NeonLetterMonotonicSequence epochs)
    {
        ArgumentNullException.ThrowIfNull(epochs);
        _epochs = epochs;
    }

    public NeonLetterMultiplayerRestoreRole Role => _role;
    public bool HasStagedEnvelope => _hasStagedEnvelope;
    public int PendingCount => _pending.Count;
    public int StartedFallbackCount => _startedFallbackCount;
    internal ulong RestoreEpoch => _epochs.Current;

    /// <summary>
    /// Returns whether the current host restore has entries left for a token.
    /// </summary>
    public bool HasWorkForToken(ulong readinessToken)
    {
        ValidateReadinessToken(readinessToken);
        return _canAdvance &&
               _role == NeonLetterMultiplayerRestoreRole.Host &&
               _pending.Count > 0 &&
               (readinessToken != _readinessToken ||
                _readinessWaveAttemptsRemaining > 0);
    }

    public void Stage(NeonLetterMultiplayerSaveEnvelope? envelope)
    {
        StageSnapshot(
            NeonLetterMultiplayerRestoreSnapshot.Sanitize(envelope));
    }

    internal void StageSnapshot(
        NeonLetterMultiplayerRestoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        BeginNextRestoreEpoch(abandonWithoutMutation: false);
        List<OwnedFallback>? ownedFallbacks = DetachLoadedState();
        _stagedSnapshot = snapshot;
        _hasStagedEnvelope = true;
        List<OwnedFallback>? displacedFallbacks = ApplyRoleToLoadedState();
        RollbackFallbacks(ownedFallbacks);
        RollbackFallbacks(displacedFallbacks);
    }

    public void SetRole(NeonLetterMultiplayerRestoreRole role)
    {
        if (_role != role)
        {
            BeginNextRestoreEpoch(abandonWithoutMutation: false);
        }

        _role = role;
        RollbackFallbacks(ApplyRoleToLoadedState());
    }

    public void Clear()
    {
        BeginNextRestoreEpoch(abandonWithoutMutation: false);
        List<OwnedFallback>? ownedFallbacks = DetachLoadedState();
        _role = NeonLetterMultiplayerRestoreRole.Unknown;
        RollbackFallbacks(ownedFallbacks);
    }

    internal void AbandonWithoutWorldMutation()
    {
        DetachForReset().Abandon();
    }

    internal NeonLetterDetachedRestoreCleanup DetachForReset()
    {
        BeginNextRestoreEpoch(abandonWithoutMutation: true);
        List<OwnedFallback>? ownedFallbacks = DetachLoadedState();
        _role = NeonLetterMultiplayerRestoreRole.Unknown;
        return new NeonLetterDetachedRestoreCleanup(
            ownedFallbacks == null
                ? null
                : () => RollbackFallbacks(ownedFallbacks));
    }

    public void Advance(
        double nowSeconds,
        Func<NeonLetterMultiplayerSaveEntry, bool, TTarget?,
            NeonLetterMultiplayerRestoreObservation<TTarget>> observe,
        Func<NeonLetterMultiplayerSaveEntry, TTarget> startFallback,
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored,
        Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError)
    {
        Advance(
            nowSeconds,
            int.MaxValue,
            int.MaxValue,
            observe,
            startFallback,
            applyRestored,
            onEntryError);
    }

    /// <summary>
    /// Advances at most the requested entries and fallback spawns in one slice.
    /// </summary>
    public void Advance(
        double nowSeconds,
        int maxItems,
        int maxFallbackSpawns,
        Func<NeonLetterMultiplayerSaveEntry, bool, TTarget?,
            NeonLetterMultiplayerRestoreObservation<TTarget>> observe,
        Func<NeonLetterMultiplayerSaveEntry, TTarget> startFallback,
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored,
        Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError)
    {
        ValidateNowSeconds(nowSeconds);
        ValidateBudgets(maxItems, maxFallbackSpawns);

        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(startFallback);
        ArgumentNullException.ThrowIfNull(applyRestored);
        ArgumentNullException.ThrowIfNull(onEntryError);

        if (!TryBeginAdvance(readinessToken: null))
        {
            return;
        }

        try
        {
            AdvanceCore(
                readinessToken: null,
                maxItems,
                maxFallbackSpawns,
                observe,
                startFallback,
                applyRestored,
                onEntryError);
        }
        finally
        {
            _isAdvancing = false;
        }
    }

    /// <summary>
    /// Advances one bounded slice of the entries woken by a readiness token.
    /// </summary>
    public void AdvanceForReadinessToken(
        ulong readinessToken,
        int maxItems,
        int maxFallbackSpawns,
        Func<NeonLetterMultiplayerSaveEntry, bool, TTarget?,
            NeonLetterMultiplayerRestoreObservation<TTarget>> observe,
        Func<NeonLetterMultiplayerSaveEntry, TTarget> startFallback,
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored,
        Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError)
    {
        ValidateReadinessToken(readinessToken);
        ValidateBudgets(maxItems, maxFallbackSpawns);

        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(startFallback);
        ArgumentNullException.ThrowIfNull(applyRestored);
        ArgumentNullException.ThrowIfNull(onEntryError);

        if (!TryBeginAdvance(readinessToken))
        {
            return;
        }

        try
        {
            AdvanceCore(
                ResolveDeferredReadinessToken(readinessToken),
                maxItems,
                maxFallbackSpawns,
                observe,
                startFallback,
                applyRestored,
                onEntryError);
        }
        finally
        {
            _isAdvancing = false;
        }
    }

    private bool TryBeginAdvance(ulong? readinessToken)
    {
        if (_isAdvancing)
        {
            if (readinessToken.HasValue &&
                (!_deferredReadinessToken.HasValue ||
                 readinessToken.Value > _deferredReadinessToken.Value))
            {
                _deferredReadinessToken = readinessToken;
            }

            return false;
        }

        _isAdvancing = true;
        return true;
    }

    private ulong ResolveDeferredReadinessToken(ulong readinessToken)
    {
        if (_deferredReadinessToken.HasValue &&
            _deferredReadinessToken.Value > readinessToken)
        {
            readinessToken = _deferredReadinessToken.Value;
        }

        _deferredReadinessToken = null;
        return readinessToken;
    }

    private void AdvanceCore(
        ulong? readinessToken,
        int maxItems,
        int maxFallbackSpawns,
        Func<NeonLetterMultiplayerSaveEntry, bool, TTarget?,
            NeonLetterMultiplayerRestoreObservation<TTarget>> observe,
        Func<NeonLetterMultiplayerSaveEntry, TTarget> startFallback,
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored,
        Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError)
    {
        maxItems = Math.Min(maxItems, MaxItemsPerUpdate);
        maxFallbackSpawns = Math.Min(
            maxFallbackSpawns,
            MaxFallbackSpawnsPerUpdate);
        if (!_canAdvance ||
            _role != NeonLetterMultiplayerRestoreRole.Host ||
            _pending.Count == 0 ||
            maxItems == 0)
        {
            return;
        }

        if (readinessToken.HasValue)
        {
            if (readinessToken.Value != _readinessToken)
            {
                _readinessToken = readinessToken.Value;
                _readinessWaveAttemptsRemaining = _pending.Count;
            }

            maxItems = Math.Min(maxItems, _readinessWaveAttemptsRemaining);
            if (maxItems == 0)
            {
                return;
            }
        }

        ulong advanceEpoch = _epochs.Current;
        LinkedListNode<PendingRestore>? node =
            _nextPending?.List == _pending
                ? _nextPending
                : _pending.First;
        int nodesToInspect = _pending.Count;
        List<LinkedListNode<PendingRestore>> snapshot =
            _snapshotPool.Rent();
        int fallbackSpawns = 0;
        try
        {
            for (int inspected = 0;
                 inspected < nodesToInspect &&
                 snapshot.Count < maxItems &&
                 node != null;
                 inspected++)
            {
                LinkedListNode<PendingRestore> candidate = node;
                node = node.Next ?? _pending.First;
                if (!_snapshotPool.IsReservedByOuterBuffer(candidate))
                {
                    snapshot.Add(candidate);
                }
            }

            _nextPending = node;
            foreach (LinkedListNode<PendingRestore> currentNode in snapshot)
            {
                PendingRestore pending = currentNode.Value;
                if (!IsCurrent(currentNode, pending, advanceEpoch))
                {
                    break;
                }

                if (readinessToken.HasValue)
                {
                    if (readinessToken.Value != _readinessToken)
                    {
                        break;
                    }

                    _readinessWaveAttemptsRemaining--;
                }

                try
                {
                    NeonLetterMultiplayerRestoreObservation<TTarget>
                        observation = observe(
                            pending.Entry,
                            pending.State.FallbackSpawnStarted,
                            pending.SpawnedTarget);
                    if (!IsCurrent(currentNode, pending, advanceEpoch))
                    {
                        break;
                    }

                    ProcessObservation(
                        currentNode,
                        pending,
                        observation,
                        advanceEpoch,
                        maxFallbackSpawns,
                        ref fallbackSpawns,
                        observe,
                        startFallback,
                        applyRestored,
                        onEntryError);
                }
                catch (Exception exception)
                {
                    if (!IsCurrent(currentNode, pending, advanceEpoch))
                    {
                        HandleStaleFallback(
                            currentNode,
                            pending,
                            advanceEpoch);
                        break;
                    }

                    OwnedFallback? ownedFallback = RemovePending(currentNode);
                    Exception? rollbackException =
                        TryRollbackFallback(ownedFallback);
                    try
                    {
                        onEntryError(pending.Entry, exception);
                    }
                    finally
                    {
                        ReportRollbackError(
                            ownedFallback,
                            rollbackException);
                    }
                }
            }
        }
        finally
        {
            _snapshotPool.Return(snapshot);
        }
    }

    private void ProcessObservation(
        LinkedListNode<PendingRestore> node,
        PendingRestore pending,
        NeonLetterMultiplayerRestoreObservation<TTarget> observation,
        ulong advanceEpoch,
        int maxFallbackSpawns,
        ref int fallbackSpawns,
        Func<NeonLetterMultiplayerSaveEntry, bool, TTarget?,
            NeonLetterMultiplayerRestoreObservation<TTarget>> observe,
        Func<NeonLetterMultiplayerSaveEntry, TTarget> startFallback,
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored,
        Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError)
    {
        if (!IsCurrent(node, pending, advanceEpoch))
        {
            return;
        }

        switch (observation.Kind)
        {
            case NeonLetterMultiplayerRestoreObservationKind.ProcessedRecipeUnavailable:
            case NeonLetterMultiplayerRestoreObservationKind.FallbackPrefabUnavailable:
            case NeonLetterMultiplayerRestoreObservationKind.NativeRecipeUnavailable:
            case NeonLetterMultiplayerRestoreObservationKind.NativeTargetUnavailable:
            case NeonLetterMultiplayerRestoreObservationKind.FallbackTargetUnavailable:
                break;

            case NeonLetterMultiplayerRestoreObservationKind.NativeRecipeMismatch:
                if (!observation.ResolvedRecipeId.HasValue ||
                    observation.ResolvedRecipeId.Value ==
                    pending.SnapshotEntry.RecipeId)
                {
                    throw new InvalidOperationException(
                        "A terminal native recipe mismatch requires " +
                        "a definite differing recipe ID.");
                }

                pending.State.Decide(
                    nativeIdentityResolved: true,
                    observation.ResolvedRecipeId.Value);
                RollbackFallback(RemovePending(node));
                break;

            case NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady:
            case NeonLetterMultiplayerRestoreObservationKind.FallbackTargetReady:
                ApplyReadyTarget(
                    node,
                    pending,
                    observation,
                    advanceEpoch,
                    applyRestored);
                break;

            case NeonLetterMultiplayerRestoreObservationKind.ReadyToSpawnFallback:
                if (fallbackSpawns >= maxFallbackSpawns)
                {
                    break;
                }

                if (pending.SnapshotEntry.NativeSaveId != 0)
                {
                    NeonLetterMultiplayerRestoreObservation<TTarget>
                        finalObservation = observe(
                            pending.Entry,
                            pending.State.FallbackSpawnStarted,
                            pending.SpawnedTarget);
                    if (!IsCurrent(node, pending, advanceEpoch))
                    {
                        break;
                    }

                    if (finalObservation.Kind !=
                        NeonLetterMultiplayerRestoreObservationKind
                            .ReadyToSpawnFallback)
                    {
                        ProcessObservation(
                            node,
                            pending,
                            finalObservation,
                            advanceEpoch,
                            maxFallbackSpawns,
                            ref fallbackSpawns,
                            observe,
                            startFallback,
                            applyRestored,
                            onEntryError);
                        break;
                    }
                }

                if (pending.State.Decide(
                        nativeIdentityResolved: false,
                        resolvedRecipeId: default) ==
                    NeonLetterMultiplayerRestoreDecision.SpawnFallback)
                {
                    pending.State.MarkFallbackSpawnStarted();
                    if (node.List == _pending)
                    {
                        _startedFallbackCount++;
                    }

                    fallbackSpawns++;
                    pending.SpawnedTarget = startFallback(pending.Entry);
                    pending.ArmFallback(
                        pending.SpawnedTarget,
                        onEntryError);
                    if (!IsCurrent(node, pending, advanceEpoch))
                    {
                        HandleStaleFallback(
                            node,
                            pending,
                            advanceEpoch);
                    }
                }
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported multiplayer restore observation " +
                    $"{observation.Kind}.");
        }
    }

    private void ApplyReadyTarget(
        LinkedListNode<PendingRestore> node,
        PendingRestore pending,
        NeonLetterMultiplayerRestoreObservation<TTarget> observation,
        ulong advanceEpoch,
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored)
    {
        if (observation.Target == null)
        {
            throw new InvalidOperationException(
                $"Restore observation {observation.Kind} requires a target.");
        }

        bool applied = applyRestored(pending.Entry, observation.Target);
        if (!IsCurrent(node, pending, advanceEpoch))
        {
            return;
        }

        if (applied)
        {
            RemovePending(node);
        }
    }

    private OwnedFallback? RemovePending(
        LinkedListNode<PendingRestore> node)
    {
        if (node.List != _pending)
        {
            return node.Value.TakeOwnedFallback();
        }

        if (node.Value.State.FallbackSpawnStarted)
        {
            _startedFallbackCount--;
        }

        LinkedListNode<PendingRestore>? next =
            node.Next ?? _pending.First;
        _pending.Remove(node);
        if (ReferenceEquals(_nextPending, node))
        {
            _nextPending = next?.List == _pending
                ? next
                : _pending.First;
        }

        return node.Value.TakeOwnedFallback();
    }

    private bool IsCurrent(
        LinkedListNode<PendingRestore> node,
        PendingRestore pending,
        ulong epoch)
    {
        return _canAdvance &&
               _role == NeonLetterMultiplayerRestoreRole.Host &&
               epoch == _epochs.Current &&
               node.List == _pending &&
               ReferenceEquals(node.Value, pending);
    }

    private void HandleStaleFallback(
        LinkedListNode<PendingRestore> node,
        PendingRestore pending,
        ulong advanceEpoch)
    {
        if (_lastAbandonmentEpoch > advanceEpoch)
        {
            pending.TakeOwnedFallback();
            return;
        }

        if (node.List != _pending)
        {
            RollbackFallback(pending.TakeOwnedFallback());
        }
    }

    private void BeginNextRestoreEpoch(bool abandonWithoutMutation)
    {
        _canAdvance = false;
        try
        {
            ulong epoch = _epochs.Advance();
            if (abandonWithoutMutation)
            {
                _lastAbandonmentEpoch = epoch;
            }

            _canAdvance = true;
        }
        catch
        {
            List<OwnedFallback>? ownedFallbacks = DetachLoadedState();
            _role = NeonLetterMultiplayerRestoreRole.Unknown;
            if (!abandonWithoutMutation)
            {
                RollbackFallbacks(ownedFallbacks);
            }

            throw;
        }
    }

    private static void ValidateNowSeconds(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "Restore time must be finite and non-negative.");
        }
    }

    private static void ValidateReadinessToken(ulong readinessToken)
    {
        if (readinessToken == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(readinessToken),
                readinessToken,
                "The readiness token must be non-zero.");
        }
    }

    private static void ValidateBudgets(
        int maxItems,
        int maxFallbackSpawns)
    {
        if (maxItems < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxItems),
                maxItems,
                "Restore item budget cannot be negative.");
        }

        if (maxFallbackSpawns < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFallbackSpawns),
                maxFallbackSpawns,
                "Restore fallback-spawn budget cannot be negative.");
        }
    }

    private void AcceptStagedEnvelopeForHost()
    {
        if (_role != NeonLetterMultiplayerRestoreRole.Host ||
            !_hasStagedEnvelope)
        {
            return;
        }

        NeonLetterMultiplayerRestoreSnapshot accepted = _stagedSnapshot;
        _stagedSnapshot = NeonLetterMultiplayerRestoreSnapshot.Empty;
        _hasStagedEnvelope = false;
        int transferredCount = 0;
        foreach (NeonLetterMultiplayerRestoreEntrySnapshot entry in
                 accepted.Entries)
        {
            _pending.AddLast(new PendingRestore(entry));
            accepted.NotifyEntryTransferred(++transferredCount);
        }
        _nextPending = _pending.First;
    }

    private List<OwnedFallback>? ApplyRoleToLoadedState()
    {
        switch (_role)
        {
            case NeonLetterMultiplayerRestoreRole.Host:
                AcceptStagedEnvelopeForHost();
                return null;

            case NeonLetterMultiplayerRestoreRole.Client:
            case NeonLetterMultiplayerRestoreRole.SinglePlayer:
                return DetachLoadedState();

            default:
                return null;
        }
    }

    private List<OwnedFallback>? DetachLoadedState()
    {
        List<OwnedFallback>? ownedFallbacks = null;
        foreach (PendingRestore pending in _pending)
        {
            OwnedFallback? ownedFallback = pending.TakeOwnedFallback();
            if (ownedFallback != null)
            {
                (ownedFallbacks ??= new List<OwnedFallback>())
                    .Add(ownedFallback);
            }
        }

        _stagedSnapshot = NeonLetterMultiplayerRestoreSnapshot.Empty;
        _hasStagedEnvelope = false;
        _pending.Clear();
        _nextPending = null;
        _startedFallbackCount = 0;
        _readinessToken = 0;
        _readinessWaveAttemptsRemaining = 0;
        _deferredReadinessToken = null;

        return ownedFallbacks;
    }

    private static void RollbackFallbacks(
        List<OwnedFallback>? ownedFallbacks)
    {
        if (ownedFallbacks == null)
        {
            return;
        }

        foreach (OwnedFallback ownedFallback in ownedFallbacks)
        {
            RollbackFallback(ownedFallback);
        }
    }

    private sealed class PendingRestore
    {
        private OwnedFallback? _ownedFallback;

        public PendingRestore(
            NeonLetterMultiplayerRestoreEntrySnapshot snapshotEntry)
        {
            SnapshotEntry = snapshotEntry;
            Entry = snapshotEntry.RestoreEntry;
            State = new NeonLetterMultiplayerRestoreEntryState(Entry);
        }

        public NeonLetterMultiplayerRestoreEntrySnapshot SnapshotEntry
        {
            get;
        }
        public NeonLetterMultiplayerSaveEntry Entry { get; }
        public NeonLetterMultiplayerRestoreEntryState State { get; }
        public TTarget? SpawnedTarget { get; set; }

        public void ArmFallback(
            TTarget target,
            Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError)
        {
            if (target is not IDisposable rollback)
            {
                return;
            }

            _ownedFallback = new OwnedFallback(
                Entry,
                rollback,
                onEntryError);
        }

        public OwnedFallback? TakeOwnedFallback()
        {
            OwnedFallback? ownedFallback = _ownedFallback;
            _ownedFallback = null;
            return ownedFallback;
        }
    }

    private sealed record OwnedFallback(
        NeonLetterMultiplayerSaveEntry Entry,
        IDisposable Rollback,
        Action<NeonLetterMultiplayerSaveEntry, Exception> OnError);

    private static Exception? TryRollbackFallback(
        OwnedFallback? ownedFallback)
    {
        if (ownedFallback == null)
        {
            return null;
        }

        try
        {
            ownedFallback.Rollback.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void RollbackFallback(OwnedFallback? ownedFallback)
    {
        Exception? rollbackException = TryRollbackFallback(ownedFallback);
        ReportRollbackError(ownedFallback, rollbackException);
    }

    private static void ReportRollbackError(
        OwnedFallback? ownedFallback,
        Exception? rollbackException)
    {
        if (ownedFallback == null || rollbackException == null)
        {
            return;
        }

        try
        {
            ownedFallback.OnError(
                ownedFallback.Entry,
                rollbackException);
        }
        catch
        {
            // Rollback reporting cannot restore the fallback or safely retry it.
        }
    }
}

public static class NeonLetterMultiplayerPersistencePolicy
{
    public const float QuaternionMagnitudeTolerance = 0.001f;

    private static readonly IReadOnlySet<int> KnownRecipeIds =
        NeonLetterSmallCatalog.All
            .Select(definition => definition.RecipeId)
            .ToHashSet();

    public static NeonLetterMultiplayerSaveEnvelope CreateWorldPayload(
        bool isMultiplayer,
        bool isHost,
        NeonLetterMultiplayerSaveEnvelope? envelope)
    {
        return isMultiplayer && isHost
            ? Sanitize(envelope)
            : new NeonLetterMultiplayerSaveEnvelope();
    }

    public static NeonLetterMultiplayerSaveEnvelope AcceptLoadedWorldPayload(
        bool isMultiplayer,
        bool isHost,
        NeonLetterMultiplayerSaveEnvelope? envelope)
    {
        return isMultiplayer && isHost
            ? Sanitize(envelope)
            : new NeonLetterMultiplayerSaveEnvelope();
    }

    public static NeonLetterMultiplayerSaveEnvelope Sanitize(
        NeonLetterMultiplayerSaveEnvelope? envelope)
    {
        return NeonLetterMultiplayerRestoreSnapshot
            .Sanitize(envelope)
            .ToEnvelope();
    }

    internal static NeonLetterMultiplayerRestoreEntrySnapshot?
        CreateRestoreSnapshotEntry(
            NeonLetterMultiplayerSaveEntry? entry)
    {
        if (entry == null ||
            !KnownRecipeIds.Contains(entry.RecipeId) ||
            !IsFinite(entry.Position) ||
            !IsValidRotation(entry.Rotation) ||
            !TryDecodeColor(
                NeonLetterNetworkProtocol.CurrentVersion,
                entry.PackedColor,
                out _))
        {
            return null;
        }

        return new NeonLetterMultiplayerRestoreEntrySnapshot(entry);
    }

    public static bool TryDecodeColor(
        byte protocolVersion,
        uint packedColor,
        out NeonRgba color)
    {
        color = default;
        try
        {
            color = NeonLetterNetworkProtocol.Unpack(
                protocolVersion,
                packedColor);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsFinite(NeonVector3? position)
    {
        return position != null &&
               float.IsFinite(position.X) &&
               float.IsFinite(position.Y) &&
               float.IsFinite(position.Z);
    }

    private static bool IsValidRotation(NeonQuaternion? rotation)
    {
        if (rotation == null ||
            !float.IsFinite(rotation.X) ||
            !float.IsFinite(rotation.Y) ||
            !float.IsFinite(rotation.Z) ||
            !float.IsFinite(rotation.W))
        {
            return false;
        }

        float magnitudeSquared =
            rotation.X * rotation.X +
            rotation.Y * rotation.Y +
            rotation.Z * rotation.Z +
            rotation.W * rotation.W;
        return float.IsFinite(magnitudeSquared) &&
               magnitudeSquared > 0f &&
               MathF.Abs(magnitudeSquared - 1f) <= QuaternionMagnitudeTolerance;
    }
}
