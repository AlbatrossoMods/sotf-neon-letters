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

    private readonly List<PendingRestore> _pending = new();
    private NeonLetterMultiplayerSaveEnvelope _stagedEnvelope = new();
    private NeonLetterMultiplayerRestoreRole _role;
    private bool _hasStagedEnvelope;

    public NeonLetterMultiplayerRestoreRole Role => _role;
    public bool HasStagedEnvelope => _hasStagedEnvelope;
    public int PendingCount => _pending.Count;
    public int StartedFallbackCount =>
        _pending.Count(entry => entry.State.FallbackSpawnStarted);

    public void Stage(NeonLetterMultiplayerSaveEnvelope? envelope)
    {
        _stagedEnvelope = NeonLetterMultiplayerPersistencePolicy.Sanitize(envelope);
        _hasStagedEnvelope = true;
        _pending.Clear();
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
        ValidateNowSeconds(nowSeconds);
        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(startFallback);
        ArgumentNullException.ThrowIfNull(applyRestored);
        ArgumentNullException.ThrowIfNull(onEntryError);

        if (_role != NeonLetterMultiplayerRestoreRole.Host)
        {
            return;
        }

        PendingRestore[] snapshot = _pending.ToArray();
        foreach (PendingRestore pending in snapshot)
        {
            try
            {
                NeonLetterMultiplayerRestoreObservation<TTarget> observation =
                    observe(
                        pending.Entry,
                        pending.State.FallbackSpawnStarted,
                        pending.SpawnedTarget);
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
                        _pending.Remove(pending);
                        break;

                    case NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady:
                        ApplyReadyTarget(
                            pending,
                            observation,
                            applyRestored,
                            nowSeconds);
                        break;

                    case NeonLetterMultiplayerRestoreObservationKind.ReadyToSpawnFallback:
                        pending.ResetReadiness();
                        if (pending.State.Decide(
                                nativeIdentityResolved: false,
                                resolvedRecipeId: default) ==
                            NeonLetterMultiplayerRestoreDecision.SpawnFallback)
                        {
                            pending.State.MarkFallbackSpawnStarted();
                            pending.SpawnedTarget = startFallback(pending.Entry);
                        }
                        break;

                    case NeonLetterMultiplayerRestoreObservationKind.FallbackTargetReady:
                        ApplyReadyTarget(
                            pending,
                            observation,
                            applyRestored,
                            nowSeconds);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported multiplayer restore observation " +
                            $"{observation.Kind}.");
                }
            }
            catch (Exception exception)
            {
                _pending.Remove(pending);
                onEntryError(pending.Entry, exception);
            }
        }
    }

    private void ApplyReadyTarget(
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
            _pending.Remove(pending);
            return;
        }

        TrackReadinessOrThrow(
            pending,
            observation.Kind,
            nowSeconds);
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
            _pending.Add(new PendingRestore(entry));
        }
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
