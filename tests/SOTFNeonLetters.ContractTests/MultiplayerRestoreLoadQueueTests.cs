using SOTFNeonLetters;
using Xunit;

public sealed class MultiplayerRestoreLoadQueueTests
{
    [Fact]
    public void QueuedReplacementDefersOwnedFallbackRollbackUntilUpdate()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator(CreateEnvelope(nativeSaveId: 0));
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        int rollbackCount = 0;
        ActivateFallback(coordinator, () => rollbackCount++);

        queue.Enqueue(CreateEnvelope(nativeSaveId: 2));

        Assert.Equal(
            (0, 1, true),
            (rollbackCount, coordinator.PendingCount, queue.HasPending));

        Assert.True(queue.TryDequeue(out NeonLetterMultiplayerSaveEnvelope load));
        coordinator.Stage(load);

        Assert.Equal(
            (1, 1, false),
            (rollbackCount, coordinator.PendingCount, queue.HasPending));
    }

    [Fact]
    public void MultipleQueuedLoadsCoalesceToLatestPayload()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        queue.Enqueue(CreateEnvelope(nativeSaveId: 1));
        queue.Enqueue(CreateEnvelope(nativeSaveId: 2));

        Assert.True(queue.TryDequeue(out NeonLetterMultiplayerSaveEnvelope load));

        Assert.Equal(
            (2, false),
            (load.Entries.Single().NativeSaveId, queue.HasPending));
    }

    [Fact]
    public void WorldExitDropsQueuedLoadWithoutRestoreMutation()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator(CreateEnvelope(nativeSaveId: 0));
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        int rollbackCount = 0;
        ActivateFallback(coordinator, () => rollbackCount++);
        queue.Enqueue(CreateEnvelope(nativeSaveId: 2));

        queue.Clear();
        coordinator.AbandonWithoutWorldMutation();

        Assert.False(queue.TryDequeue(out _));
        Assert.Equal(
            (0, 0, false),
            (rollbackCount, coordinator.PendingCount, queue.HasPending));
    }

    [Fact]
    public void RestoreEpochIsNonZeroAndAdvancesAcrossReplacementAndClear()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>();
        coordinator.Stage(CreateEnvelope(nativeSaveId: 1));
        ulong stagedEpoch = coordinator.RestoreEpoch;

        coordinator.Stage(CreateEnvelope(nativeSaveId: 2));
        ulong replacementEpoch = coordinator.RestoreEpoch;
        coordinator.Clear();

        Assert.Equal(
            (true, true, true),
            (
                stagedEpoch > 0,
                replacementEpoch > stagedEpoch,
                coordinator.RestoreEpoch > replacementEpoch));
    }

    [Fact]
    public void ActiveWorldDeinitializeRollsBackOwnedFallbackExactlyOnce()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator(CreateEnvelope(nativeSaveId: 0));
        int rollbackCount = 0;
        ActivateFallback(coordinator, () => rollbackCount++);

        coordinator.Clear();
        coordinator.AbandonWithoutWorldMutation();

        Assert.Equal(
            (1, 0),
            (rollbackCount, coordinator.PendingCount));
    }

    [Fact]
    public void UnknownRoleRoundTripPreservesPendingHostRestore()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator(CreateEnvelope(nativeSaveId: 1));
        int appliedCount = 0;

        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Unknown);
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetReady,
                    new RestoreTarget(() => { })),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) =>
            {
                appliedCount++;
                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (1, 0),
            (appliedCount, coordinator.PendingCount));
    }

    [Fact]
    public void ReentrantWorldExitDuringFallbackStartDoesNotRollbackTarget()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator(CreateEnvelope(nativeSaveId: 0));
        int rollbackCount = 0;

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ =>
            {
                coordinator.AbandonWithoutWorldMutation();
                return new RestoreTarget(() => rollbackCount++);
            },
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (0, 0),
            (rollbackCount, coordinator.PendingCount));
    }

    [Fact]
    public void DeinitializeRejectsLoadsQueuedByRollbackCallbacks()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator(CreateEnvelope(nativeSaveId: 0));
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        ActivateFallback(
            coordinator,
            () => queue.Enqueue(CreateEnvelope(nativeSaveId: 2)));

        queue.SuspendAndClear();
        coordinator.Clear();
        queue.SuspendAndClear();
        queue.Resume();

        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void ExhaustedEpochClearCancelsOldRestoreAndFailsClosed()
    {
        var epochs = new NeonLetterMonotonicSequence(ulong.MaxValue - 2);
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>(epochs);
        coordinator.Stage(CreateEnvelope(nativeSaveId: 1));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int observeCount = 0;

        Assert.Throws<InvalidOperationException>(() => coordinator.Clear());
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
            {
                observeCount++;
                return new NeonLetterMultiplayerRestoreObservation<
                    RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (0, 0),
            (observeCount, coordinator.PendingCount));
    }

    [Fact]
    public void ExhaustedEpochAbandonDoesNotRollbackAndFailsClosed()
    {
        var epochs = new NeonLetterMonotonicSequence(ulong.MaxValue - 2);
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>(epochs);
        coordinator.Stage(CreateEnvelope(nativeSaveId: 0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int rollbackCount = 0;
        ActivateFallback(coordinator, () => rollbackCount++);

        Assert.Throws<InvalidOperationException>(
            coordinator.AbandonWithoutWorldMutation);
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                throw new InvalidOperationException(
                    "An exhausted restore must remain inactive."),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (0, 0),
            (rollbackCount, coordinator.PendingCount));
    }

    private static NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>
        CreateCoordinator(NeonLetterMultiplayerSaveEnvelope envelope)
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>();
        coordinator.Stage(envelope);
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        return coordinator;
    }

    private static void ActivateFallback(
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator,
        Action rollback)
    {
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ => new RestoreTarget(rollback),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
    }

    private static NeonLetterMultiplayerSaveEnvelope CreateEnvelope(
        int nativeSaveId)
    {
        return new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry>
            {
                new()
                {
                    RecipeId = NeonLetterSmallCatalog.Get('A').RecipeId,
                    NativeSaveId = nativeSaveId,
                    Position = new NeonVector3(),
                    Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                    PackedColor = NeonLetterNetworkProtocol.Pack(
                        NeonRgba.ProjectCyan)
                }
            }
        };
    }

    private sealed class RestoreTarget : IDisposable
    {
        private readonly Action _rollback;

        internal RestoreTarget(Action rollback)
        {
            _rollback = rollback;
        }

        public void Dispose()
        {
            _rollback();
        }
    }
}
