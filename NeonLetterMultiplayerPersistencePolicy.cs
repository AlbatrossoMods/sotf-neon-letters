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
    public const double ReadinessTimeoutSeconds = 15d;

    private readonly LinkedList<PendingRestore> _pending = new();
    private readonly NeonLetterReentrantSnapshotPool<
        LinkedListNode<PendingRestore>> _snapshotPool =
            new();
    private NeonLetterMultiplayerSaveEnvelope _stagedEnvelope = new();
    private LinkedListNode<PendingRestore>? _nextPending;
    private NeonLetterMultiplayerRestoreRole _role;
    private bool _hasStagedEnvelope;
    private int _startedFallbackCount;

    public NeonLetterMultiplayerRestoreRole Role => _role;
    public bool HasStagedEnvelope => _hasStagedEnvelope;
    public int PendingCount => _pending.Count;
    public int StartedFallbackCount => _startedFallbackCount;

    public void Stage(NeonLetterMultiplayerSaveEnvelope? envelope)
    {
        _stagedEnvelope = NeonLetterMultiplayerPersistencePolicy.Sanitize(envelope);
        _hasStagedEnvelope = true;
        _pending.Clear();
        _nextPending = null;
        _startedFallbackCount = 0;
        ApplyRoleToLoadedState();
    }

    public void SetRole(NeonLetterMultiplayerRestoreRole role)
    {
        ResetPendingReadiness();
        _role = role;
        ApplyRoleToLoadedState();
    }

    public void Clear()
    {
        ClearLoadedState();
        _role = NeonLetterMultiplayerRestoreRole.Unknown;
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

        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(startFallback);
        ArgumentNullException.ThrowIfNull(applyRestored);
        ArgumentNullException.ThrowIfNull(onEntryError);

        if (_role != NeonLetterMultiplayerRestoreRole.Host ||
            _pending.Count == 0 ||
            maxItems == 0)
        {
            return;
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
                        nowSeconds,
                        maxFallbackSpawns,
                        ref fallbackSpawns,
                        observe,
                        startFallback,
                        applyRestored);
                }
                catch (Exception exception)
                {
                    RemovePending(currentNode);
                    onEntryError(pending.Entry, exception);
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
        double nowSeconds,
        int maxFallbackSpawns,
        ref int fallbackSpawns,
        Func<NeonLetterMultiplayerSaveEntry, bool, TTarget?,
            NeonLetterMultiplayerRestoreObservation<TTarget>> observe,
        Func<NeonLetterMultiplayerSaveEntry, TTarget> startFallback,
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored)
    {
        switch (observation.Kind)
        {
            case NeonLetterMultiplayerRestoreObservationKind.ProcessedRecipeUnavailable:
            case NeonLetterMultiplayerRestoreObservationKind.FallbackPrefabUnavailable:
            case NeonLetterMultiplayerRestoreObservationKind.NativeRecipeUnavailable:
            case NeonLetterMultiplayerRestoreObservationKind.NativeTargetUnavailable:
            case NeonLetterMultiplayerRestoreObservationKind.FallbackTargetUnavailable:
                TrackReadinessOrThrow(
                    pending,
                    observation.Kind,
                    nowSeconds);
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
                RemovePending(node);
                break;

            case NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady:
            case NeonLetterMultiplayerRestoreObservationKind.FallbackTargetReady:
                ApplyReadyTarget(
                    node,
                    pending,
                    observation,
                    applyRestored,
                    nowSeconds);
                break;

            case NeonLetterMultiplayerRestoreObservationKind.ReadyToSpawnFallback:
                pending.ResetReadiness();
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
                            nowSeconds,
                            maxFallbackSpawns,
                            ref fallbackSpawns,
                            observe,
                            startFallback,
                            applyRestored);
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
        Func<NeonLetterMultiplayerSaveEntry, TTarget, bool> applyRestored,
        double nowSeconds)
    {
        if (observation.Target == null)
        {
            throw new InvalidOperationException(
                $"Restore observation {observation.Kind} requires a target.");
        }

        if (applyRestored(pending.Entry, observation.Target))
        {
            RemovePending(node);
            return;
        }

        TrackReadinessOrThrow(
            pending,
            observation.Kind,
            nowSeconds);
    }

    private void RemovePending(LinkedListNode<PendingRestore> node)
    {
        if (node.List != _pending)
        {
            return;
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
    }

    private static void TrackReadinessOrThrow(
        PendingRestore pending,
        NeonLetterMultiplayerRestoreObservationKind observationKind,
        double nowSeconds)
    {
        if (pending.ReadinessObservationKind != observationKind)
        {
            pending.BeginReadiness(observationKind, nowSeconds);
            return;
        }

        if (nowSeconds - pending.ReadinessStartedAtSeconds <
            ReadinessTimeoutSeconds)
        {
            return;
        }

        throw new TimeoutException(
            $"Multiplayer neon restore readiness stage {observationKind} " +
            $"remained unchanged for {ReadinessTimeoutSeconds} seconds.");
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

    private void ApplyRoleToLoadedState()
    {
        switch (_role)
        {
            case NeonLetterMultiplayerRestoreRole.Host:
                AcceptStagedEnvelopeForHost();
                break;

            case NeonLetterMultiplayerRestoreRole.Client:
            case NeonLetterMultiplayerRestoreRole.SinglePlayer:
                ClearLoadedState();
                break;
        }
    }

    private void ClearLoadedState()
    {
        _stagedEnvelope = new NeonLetterMultiplayerSaveEnvelope();
        _hasStagedEnvelope = false;
        _pending.Clear();
        _nextPending = null;
        _startedFallbackCount = 0;
    }

    private void ResetPendingReadiness()
    {
        foreach (PendingRestore pending in _pending)
        {
            pending.ResetReadiness();
        }
    }

    private sealed class PendingRestore
    {
        public PendingRestore(NeonLetterMultiplayerSaveEntry entry)
        {
            Entry = entry;
            State = new NeonLetterMultiplayerRestoreEntryState(entry);
        }

        public NeonLetterMultiplayerSaveEntry Entry { get; }
        public NeonLetterMultiplayerRestoreEntryState State { get; }
        public TTarget? SpawnedTarget { get; set; }
        public NeonLetterMultiplayerRestoreObservationKind?
            ReadinessObservationKind { get; private set; }
        public double ReadinessStartedAtSeconds { get; private set; }

        public void BeginReadiness(
            NeonLetterMultiplayerRestoreObservationKind observationKind,
            double nowSeconds)
        {
            ReadinessObservationKind = observationKind;
            ReadinessStartedAtSeconds = nowSeconds;
        }

        public void ResetReadiness()
        {
            ReadinessObservationKind = null;
            ReadinessStartedAtSeconds = 0d;
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
