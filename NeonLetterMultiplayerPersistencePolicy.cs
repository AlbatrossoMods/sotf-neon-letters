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

public sealed class NeonLetterMultiplayerRestoreCoordinator<TTarget>
    where TTarget : class
{
    private readonly LinkedList<PendingRestore> _pending = new();
    private readonly NeonLetterReentrantSnapshotPool<
        LinkedListNode<PendingRestore>> _snapshotPool =
            new();
    private NeonLetterMultiplayerSaveEnvelope _stagedEnvelope = new();
    private LinkedListNode<PendingRestore>? _nextPending;
    private NeonLetterMultiplayerRestoreRole _role;
    private bool _hasStagedEnvelope;
    private int _startedFallbackCount;
    private ulong _readinessToken;
    private int _readinessWaveAttemptsRemaining;

    public NeonLetterMultiplayerRestoreRole Role => _role;
    public bool HasStagedEnvelope => _hasStagedEnvelope;
    public int PendingCount => _pending.Count;
    public int StartedFallbackCount => _startedFallbackCount;

    /// <summary>
    /// Returns whether the current host restore has entries left for a token.
    /// </summary>
    public bool HasWorkForToken(ulong readinessToken)
    {
        ValidateReadinessToken(readinessToken);
        return _role == NeonLetterMultiplayerRestoreRole.Host &&
               _pending.Count > 0 &&
               (readinessToken != _readinessToken ||
                _readinessWaveAttemptsRemaining > 0);
    }

    public void Stage(NeonLetterMultiplayerSaveEnvelope? envelope)
    {
        NeonLetterMultiplayerSaveEnvelope stagedEnvelope =
            NeonLetterMultiplayerPersistencePolicy.Sanitize(envelope);
        List<OwnedFallback>? ownedFallbacks = DetachLoadedState();
        _stagedEnvelope = stagedEnvelope;
        _hasStagedEnvelope = true;
        List<OwnedFallback>? displacedFallbacks = ApplyRoleToLoadedState();
        RollbackFallbacks(ownedFallbacks);
        RollbackFallbacks(displacedFallbacks);
    }

    public void SetRole(NeonLetterMultiplayerRestoreRole role)
    {
        _role = role;
        RollbackFallbacks(ApplyRoleToLoadedState());
    }

    public void Clear()
    {
        List<OwnedFallback>? ownedFallbacks = DetachLoadedState();
        _role = NeonLetterMultiplayerRestoreRole.Unknown;
        RollbackFallbacks(ownedFallbacks);
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

        AdvanceCore(
            readinessToken: null,
            maxItems,
            maxFallbackSpawns,
            observe,
            startFallback,
            applyRestored,
            onEntryError);
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

        AdvanceCore(
            readinessToken,
            maxItems,
            maxFallbackSpawns,
            observe,
            startFallback,
            applyRestored,
            onEntryError);
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
        if (_role != NeonLetterMultiplayerRestoreRole.Host ||
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
                if (readinessToken.HasValue)
                {
                    if (readinessToken.Value != _readinessToken)
                    {
                        break;
                    }

                    _readinessWaveAttemptsRemaining--;
                }

                PendingRestore pending = currentNode.Value;
                try
                {
                    NeonLetterMultiplayerRestoreObservation<TTarget>
                        observation = observe(
                            pending.Entry,
                            pending.State.FallbackSpawnStarted,
                            pending.SpawnedTarget);
                    ProcessObservation(
                        currentNode,
                        pending,
                        observation,
                        maxFallbackSpawns,
                        ref fallbackSpawns,
                        observe,
                        startFallback,
                        applyRestored,
                        onEntryError);
                }
                catch (Exception exception)
                {
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
        int maxFallbackSpawns,
        ref int fallbackSpawns,
        Func<NeonLetterMultiplayerSaveEntry, bool, TTarget?,
            NeonLetterMultiplayerRestoreObservation<TTarget>> observe,
        Func<NeonLetterMultiplayerSaveEntry, TTarget> startFallback,
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored,
        Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError)
    {
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
                    pending.Entry.RecipeId)
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
                    applyRestored);
                break;

            case NeonLetterMultiplayerRestoreObservationKind.ReadyToSpawnFallback:
                if (fallbackSpawns >= maxFallbackSpawns)
                {
                    break;
                }

                if (pending.Entry.NativeSaveId != 0)
                {
                    NeonLetterMultiplayerRestoreObservation<TTarget>
                        finalObservation = observe(
                            pending.Entry,
                            pending.State.FallbackSpawnStarted,
                            pending.SpawnedTarget);
                    if (finalObservation.Kind !=
                        NeonLetterMultiplayerRestoreObservationKind
                            .ReadyToSpawnFallback)
                    {
                        ProcessObservation(
                            node,
                            pending,
                            finalObservation,
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
                    if (node.List != _pending)
                    {
                        RollbackFallback(pending.TakeOwnedFallback());
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
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored)
    {
        if (observation.Target == null)
        {
            throw new InvalidOperationException(
                $"Restore observation {observation.Kind} requires a target.");
        }

        if (applyRestored(pending.Entry, observation.Target))
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

        NeonLetterMultiplayerSaveEnvelope accepted =
            NeonLetterMultiplayerPersistencePolicy.AcceptLoadedWorldPayload(
                isMultiplayer: true,
                isHost: true,
                _stagedEnvelope);
        _stagedEnvelope = new NeonLetterMultiplayerSaveEnvelope();
        _hasStagedEnvelope = false;
        foreach (NeonLetterMultiplayerSaveEntry entry in accepted.Entries)
        {
            _pending.AddLast(new PendingRestore(entry));
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

        _stagedEnvelope = new NeonLetterMultiplayerSaveEnvelope();
        _hasStagedEnvelope = false;
        _pending.Clear();
        _nextPending = null;
        _startedFallbackCount = 0;
        _readinessToken = 0;
        _readinessWaveAttemptsRemaining = 0;

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

        public PendingRestore(NeonLetterMultiplayerSaveEntry entry)
        {
            Entry = entry;
            State = new NeonLetterMultiplayerRestoreEntryState(entry);
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
        var snapshot = new NeonLetterMultiplayerSaveEnvelope();
        if (envelope == null ||
            envelope.Version != NeonLetterMultiplayerSaveEnvelope.CurrentVersion ||
            envelope.Entries == null)
        {
            return snapshot;
        }

        foreach (NeonLetterMultiplayerSaveEntry? entry in envelope.Entries)
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
                continue;
            }

            snapshot.Entries.Add(Copy(entry));
        }

        return snapshot;
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

    private static NeonLetterMultiplayerSaveEntry Copy(
        NeonLetterMultiplayerSaveEntry entry)
    {
        return new NeonLetterMultiplayerSaveEntry
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
