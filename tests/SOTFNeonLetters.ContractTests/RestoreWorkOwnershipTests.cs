using SOTFNeonLetters;
using Xunit;

public sealed class RestoreWorkOwnershipTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task UpdateCallbackDoesNotBlockSignalsOrLoadsAsync()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        Assert.True(ownership.TryBeginUpdate(out var update));
        using var callbackBarrier = new Barrier(2);
        using var releaseCallback = new ManualResetEventSlim();
        var signals = new NeonLetterMonotonicSequence();
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.Stage(CreateEnvelope(nativeSaveId: 1));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        Task callback = Task.Run(
            () =>
            {
                coordinator.Advance(
                    nowSeconds: 0d,
                    observe: (_, _, _) =>
                    {
                        Assert.True(
                            callbackBarrier.SignalAndWait(TestTimeout));
                        Assert.True(releaseCallback.Wait(TestTimeout));
                        return new NeonLetterMultiplayerRestoreObservation<
                            string>(
                            NeonLetterMultiplayerRestoreObservationKind
                                .NativeTargetUnavailable);
                    },
                    startFallback: _ => "fallback",
                    applyRestored: (_, _) => true,
                    onEntryError: (_, exception) => throw exception);
            });
        Assert.True(callbackBarrier.SignalAndWait(TestTimeout));
        bool signalled = false;

        Task<(bool LoadAccepted, bool SignalCompleted)> concurrent =
            Task.Run(
                () =>
                {
                    bool loadAccepted = queue.Enqueue(CreateEnvelope(1));
                    signalled = ownership.RecordSignal(signals) == 1;
                    return (loadAccepted, signalled);
                });
        (bool loadAccepted, bool signalCompleted) result;
        try
        {
            result = await concurrent.WaitAsync(TestTimeout);
        }
        finally
        {
            releaseCallback.Set();
        }

        await callback.WaitAsync(TestTimeout);
        ownership.CompleteUpdate(update, out _);

        Assert.Equal(
            (true, true, 1),
            (
                result.loadAccepted,
                result.signalCompleted,
                coordinator.PendingCount));
    }

    [Fact]
    public async Task ResetCallbackDoesNotBlockLifecycleSignalsOrLoadsAsync()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        queue.SuspendAndClear();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: true,
                resumeLoads: false,
                out var reset));
        using var callbackBarrier = new Barrier(2);
        using var releaseCallback = new ManualResetEventSlim();
        var signals = new NeonLetterMonotonicSequence();
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateEnvelope(nativeSaveId: 0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int resetCount = 0;
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ =>
                new DisposableTarget(
                    () =>
                    {
                        resetCount++;
                        Assert.True(
                            callbackBarrier.SignalAndWait(TestTimeout));
                        Assert.True(releaseCallback.Wait(TestTimeout));
                    }),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        Task resetWork = Task.Run(
            () =>
            {
                NeonLetterRestoreResetRequest request =
                    ownership.GetResetRequest(reset);
                coordinator.Clear();
                Assert.False(
                    ownership.TryCompleteReset(
                        reset,
                        request.Version,
                        rollbackSatisfied: true,
                        out request,
                        out _));
                Assert.True(
                    ownership.TryCompleteReset(
                        reset,
                        request.Version,
                        rollbackSatisfied: true,
                        out _,
                        out _));
            });
        Assert.True(callbackBarrier.SignalAndWait(TestTimeout));
        bool signalled = false;

        Task<(bool LoadAccepted, bool SignalCompleted)> concurrent =
            Task.Run(
                () =>
                {
                    bool loadAccepted = queue.Enqueue(CreateEnvelope(1));
                    signalled = ownership.RecordSignal(signals) == 1;
                    return (loadAccepted, signalled);
                });
        (bool loadAccepted, bool signalCompleted) result;
        bool secondOwner;
        try
        {
            result = await concurrent.WaitAsync(TestTimeout);
            secondOwner = ownership.RequestReset(
                rollbackOwnedFallbacks: true,
                resumeLoads: false,
                out _);
        }
        finally
        {
            releaseCallback.Set();
        }

        await resetWork.WaitAsync(TestTimeout);

        Assert.Equal(
            (false, true, false, 1, 0),
            (
                result.loadAccepted,
                result.signalCompleted,
                secondOwner,
                resetCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void ResetRacingUpdateTransfersExactlyOneCleanupOwner()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        Assert.True(ownership.TryBeginUpdate(out var update));
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateEnvelope(nativeSaveId: 0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int rollbackCount = 0;
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ =>
                new DisposableTarget(() => rollbackCount++),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        bool resetOwnedByRequester = ownership.RequestReset(
            rollbackOwnedFallbacks: true,
            resumeLoads: false,
            out _);
        bool cancellationObserved =
            ownership.TryGetPendingResetRequest(update, out var request);
        bool duplicateOwner = ownership.RequestReset(
            rollbackOwnedFallbacks: true,
            resumeLoads: false,
            out _);
        coordinator.Clear();
        bool transferred =
            ownership.CompleteUpdate(update, out var reset);
        NeonLetterRestoreResetRequest transferredRequest =
            ownership.GetResetRequest(reset);
        Assert.True(
            ownership.TryCompleteReset(
                reset,
                transferredRequest.Version,
                rollbackSatisfied: true,
                out _,
                out NeonLetterRestoreResetCompletion completion));

        Assert.Equal(
            (false, true, false, true, true, true, false, false, 1, 0),
            (
                resetOwnedByRequester,
                cancellationObserved,
                duplicateOwner,
                request.RollbackOwnedFallbacks,
                transferred,
                transferredRequest.RollbackOwnedFallbacks,
                completion.ResumeLoads,
                ownership.IsUpdateCurrent(update),
                rollbackCount,
                coordinator.PendingCount));
    }

    [Fact]
    public async Task LargeSnapshotStageDoesNotBlockLifecycleSignalAsync()
    {
        const int entryCount = 4_096;
        using var stageBarrier = new Barrier(2);
        using var releaseStage = new ManualResetEventSlim();
        NeonLetterMultiplayerRestoreSnapshot snapshot =
            NeonLetterMultiplayerRestoreSnapshot.Sanitize(
                CreateLargeEnvelope(entryCount),
                onEntryVisited: null,
                onEntryTransferred: transferred =>
                {
                    if (transferred == entryCount / 2)
                    {
                        Assert.True(
                            stageBarrier.SignalAndWait(TestTimeout));
                        Assert.True(releaseStage.Wait(TestTimeout));
                    }
                });
        var ownership = new NeonLetterRestoreWorkOwnership();
        var signals = new NeonLetterMonotonicSequence();
        Assert.True(ownership.TryBeginUpdate(out var update));
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        Task stage = Task.Run(() => coordinator.StageSnapshot(snapshot));
        Assert.True(stageBarrier.SignalAndWait(TestTimeout));
        bool signalled = false;

        try
        {
            await Task.Run(
                    () => signalled =
                        ownership.RecordSignal(signals) == 1)
                .WaitAsync(TestTimeout);
        }
        finally
        {
            releaseStage.Set();
        }

        await stage.WaitAsync(TestTimeout);
        ownership.CompleteUpdate(update, out _);

        Assert.Equal(
            (true, entryCount),
            (signalled, coordinator.PendingCount));
    }

    [Fact]
    public async Task WeakResetUpgradeRollsBackDetachedFallbackExactlyOnceAsync()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: false,
                resumeLoads: true,
                out var reset));
        int rollbackCount = 0;
        NeonLetterMultiplayerRestoreCoordinator<DisposableTarget> coordinator =
            CreateOwnedFallbackCoordinator(() => rollbackCount++);
        using var detachedBarrier = new Barrier(2);
        using var releaseDetached = new ManualResetEventSlim();
        Task<(bool FirstCompletion, bool FinalCompletion)> weakReset =
            Task.Run(
                () =>
                {
                    NeonLetterRestoreResetRequest request =
                        ownership.GetResetRequest(reset);
                    NeonLetterDetachedRestoreCleanup cleanup =
                        coordinator.DetachForReset();
                    Assert.True(
                        detachedBarrier.SignalAndWait(TestTimeout));
                    Assert.True(releaseDetached.Wait(TestTimeout));
                    bool firstCompletion = ownership.TryCompleteReset(
                        reset,
                        request.Version,
                        rollbackSatisfied: false,
                        out NeonLetterRestoreResetRequest upgraded,
                        out _);
                    cleanup.Rollback();
                    bool finalCompletion = ownership.TryCompleteReset(
                        reset,
                        upgraded.Version,
                        rollbackSatisfied: true,
                        out _,
                        out _);
                    return (firstCompletion, finalCompletion);
                });
        Assert.True(detachedBarrier.SignalAndWait(TestTimeout));

        bool upgradeOwned = ownership.RequestReset(
            rollbackOwnedFallbacks: true,
            resumeLoads: false,
            out _);
        releaseDetached.Set();
        (bool firstCompletion, bool finalCompletion) =
            await weakReset.WaitAsync(TestTimeout);

        Assert.Equal(
            (false, false, true, 1, 0),
            (
                upgradeOwned,
                firstCompletion,
                finalCompletion,
                rollbackCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void UpdateWeakResetHonorsUpgradeBeforeOwnershipTransfer()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        Assert.True(ownership.TryBeginUpdate(out var update));
        int rollbackCount = 0;
        NeonLetterMultiplayerRestoreCoordinator<DisposableTarget> coordinator =
            CreateOwnedFallbackCoordinator(() => rollbackCount++);
        Assert.False(
            ownership.RequestReset(
                rollbackOwnedFallbacks: false,
                resumeLoads: true,
                out _));
        Assert.True(
            ownership.TryGetPendingResetRequest(
                update,
                out NeonLetterRestoreResetRequest weakRequest));
        NeonLetterDetachedRestoreCleanup cleanup =
            coordinator.DetachForReset();

        Assert.False(
            ownership.RequestReset(
                rollbackOwnedFallbacks: true,
                resumeLoads: false,
                out _));
        Assert.True(ownership.CompleteUpdate(update, out var reset));
        bool weakCompletion = ownership.TryCompleteReset(
            reset,
            weakRequest.Version,
            rollbackSatisfied: false,
            out NeonLetterRestoreResetRequest upgraded,
            out _);
        cleanup.Rollback();
        bool strongCompletion = ownership.TryCompleteReset(
            reset,
            upgraded.Version,
            rollbackSatisfied: true,
            out _,
            out _);

        Assert.Equal(
            (false, true, 1, 0),
            (
                weakCompletion,
                strongCompletion,
                rollbackCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void MultipleStrongUpgradesConsumeDetachedCleanupExactlyOnce()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: false,
                resumeLoads: true,
                out var reset));
        int rollbackCount = 0;
        NeonLetterMultiplayerRestoreCoordinator<DisposableTarget> coordinator =
            CreateOwnedFallbackCoordinator(() => rollbackCount++);
        NeonLetterRestoreResetRequest weakRequest =
            ownership.GetResetRequest(reset);
        NeonLetterDetachedRestoreCleanup cleanup =
            coordinator.DetachForReset();

        ownership.RequestReset(true, false, out _);
        ownership.RequestReset(true, false, out _);
        ownership.RequestReset(true, false, out _);
        Assert.False(
            ownership.TryCompleteReset(
                reset,
                weakRequest.Version,
                rollbackSatisfied: false,
                out NeonLetterRestoreResetRequest upgraded,
                out _));
        cleanup.Rollback();
        cleanup.Rollback();
        Assert.True(
            ownership.TryCompleteReset(
                reset,
                upgraded.Version,
                rollbackSatisfied: true,
                out _,
                out _));

        Assert.Equal(1, rollbackCount);
    }

    [Fact]
    public void StrongResetAfterCompletedWeakResetDoesNotDisposeAbandonedFallback()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: false,
                resumeLoads: true,
                out var weakReset));
        int rollbackCount = 0;
        NeonLetterMultiplayerRestoreCoordinator<DisposableTarget> coordinator =
            CreateOwnedFallbackCoordinator(() => rollbackCount++);
        NeonLetterRestoreResetRequest weakRequest =
            ownership.GetResetRequest(weakReset);
        NeonLetterDetachedRestoreCleanup cleanup =
            coordinator.DetachForReset();

        Assert.True(
            ownership.TryCompleteReset(
                weakReset,
                weakRequest.Version,
                rollbackSatisfied: false,
                out _,
                out _));
        cleanup.Abandon();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: true,
                resumeLoads: false,
                out var strongReset));
        NeonLetterRestoreResetRequest strongRequest =
            ownership.GetResetRequest(strongReset);
        coordinator.Clear();
        Assert.True(
            ownership.TryCompleteReset(
                strongReset,
                strongRequest.Version,
                rollbackSatisfied: true,
                out _,
                out _));

        Assert.Equal(
            (0, 0),
            (rollbackCount, coordinator.PendingCount));
    }

    private static NeonLetterMultiplayerSaveEnvelope CreateEnvelope(
        int nativeSaveId)
    {
        return new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry>
            {
                CreateEntry(nativeSaveId)
            }
        };
    }

    private static NeonLetterMultiplayerSaveEnvelope CreateLargeEnvelope(
        int entryCount)
    {
        return new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = Enumerable.Range(1, entryCount)
                .Select(CreateEntry)
                .ToList()
        };
    }

    private static NeonLetterMultiplayerSaveEntry CreateEntry(int nativeSaveId)
    {
        return new NeonLetterMultiplayerSaveEntry
        {
            RecipeId = NeonLetterSmallCatalog.Get('A').RecipeId,
            NativeSaveId = nativeSaveId,
            Position = new NeonVector3(),
            Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
            PackedColor = NeonLetterNetworkProtocol.Pack(NeonRgba.ProjectCyan)
        };
    }

    private static
        NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>
        CreateOwnedFallbackCoordinator(Action dispose)
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateEnvelope(nativeSaveId: 0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ => new DisposableTarget(dispose),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        return coordinator;
    }

    private sealed class DisposableTarget : IDisposable
    {
        private readonly Action _dispose;

        internal DisposableTarget(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            _dispose();
        }
    }
}
