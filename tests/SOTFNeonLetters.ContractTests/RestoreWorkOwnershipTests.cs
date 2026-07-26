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
        ulong resetQueueGeneration = queue.SuspendAndClear();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: true,
                resumeLoads: false,
                resetQueueGeneration,
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
            ulong secondQueueGeneration = queue.SuspendAndClear();
            secondOwner = ownership.RequestReset(
                rollbackOwnedFallbacks: true,
                resumeLoads: false,
                secondQueueGeneration,
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
            queueSuspensionGeneration: 1,
            out _);
        bool cancellationObserved =
            ownership.TryGetPendingResetRequest(update, out var request);
        bool duplicateOwner = ownership.RequestReset(
            rollbackOwnedFallbacks: true,
            resumeLoads: false,
            queueSuspensionGeneration: 2,
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
                queueSuspensionGeneration: 1,
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
            queueSuspensionGeneration: 2,
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
                queueSuspensionGeneration: 1,
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
                queueSuspensionGeneration: 2,
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
                queueSuspensionGeneration: 1,
                out var reset));
        int rollbackCount = 0;
        NeonLetterMultiplayerRestoreCoordinator<DisposableTarget> coordinator =
            CreateOwnedFallbackCoordinator(() => rollbackCount++);
        NeonLetterRestoreResetRequest weakRequest =
            ownership.GetResetRequest(reset);
        NeonLetterDetachedRestoreCleanup cleanup =
            coordinator.DetachForReset();

        ownership.RequestReset(true, false, 2, out _);
        ownership.RequestReset(true, false, 3, out _);
        ownership.RequestReset(true, false, 4, out _);
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
                queueSuspensionGeneration: 1,
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
                queueSuspensionGeneration: 2,
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

    [Fact]
    public async Task CompletedWorldExitCannotResumeQueueAfterDeinitializeAsync()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        var ownership = new NeonLetterRestoreWorkOwnership();
        ulong worldExitGeneration = queue.SuspendAndClear();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: false,
                resumeLoads: true,
                worldExitGeneration,
                out var worldExitReset));
        NeonLetterRestoreResetRequest worldExitRequest =
            ownership.GetResetRequest(worldExitReset);
        Assert.True(
            ownership.TryCompleteReset(
                worldExitReset,
                worldExitRequest.Version,
                rollbackSatisfied: false,
                out _,
                out NeonLetterRestoreResetCompletion worldExitCompletion));
        using var resumeBarrier = new Barrier(2);
        using var releaseResume = new ManualResetEventSlim();
        Task<bool> staleResume = Task.Run(
            () =>
            {
                Assert.True(resumeBarrier.SignalAndWait(TestTimeout));
                Assert.True(releaseResume.Wait(TestTimeout));
                return queue.Resume(
                    worldExitCompletion.QueueSuspensionGeneration);
            });
        Assert.True(resumeBarrier.SignalAndWait(TestTimeout));

        bool resumed;
        ulong deinitializeGeneration;
        NeonLetterRestoreResetCompletion deinitializeCompletion;
        try
        {
            deinitializeGeneration = queue.SuspendAndClear();
            Assert.True(
                ownership.RequestReset(
                    rollbackOwnedFallbacks: true,
                    resumeLoads: false,
                    deinitializeGeneration,
                    out var deinitializeReset));
            NeonLetterRestoreResetRequest deinitializeRequest =
                ownership.GetResetRequest(deinitializeReset);
            Assert.True(
                ownership.TryCompleteReset(
                    deinitializeReset,
                    deinitializeRequest.Version,
                    rollbackSatisfied: true,
                    out _,
                    out deinitializeCompletion));
        }
        finally
        {
            releaseResume.Set();
        }

        resumed = await staleResume.WaitAsync(TestTimeout);
        bool loadAccepted = queue.Enqueue(CreateEnvelope(nativeSaveId: 1));

        Assert.Equal(
            (false, false, false, true),
            (
                resumed,
                loadAccepted,
                deinitializeCompletion.ResumeLoads,
                deinitializeCompletion.QueueSuspensionGeneration ==
                deinitializeGeneration));
    }

    [Fact]
    public void CompletedWorldExitResumesMatchingQueueSuspension()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        var ownership = new NeonLetterRestoreWorkOwnership();
        ulong suspensionGeneration = queue.SuspendAndClear();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: false,
                resumeLoads: true,
                suspensionGeneration,
                out var reset));
        NeonLetterRestoreResetRequest request =
            ownership.GetResetRequest(reset);
        Assert.True(
            ownership.TryCompleteReset(
                reset,
                request.Version,
                rollbackSatisfied: false,
                out _,
                out NeonLetterRestoreResetCompletion completion));

        bool resumed = queue.Resume(
            completion.QueueSuspensionGeneration);
        bool loadAccepted = queue.Enqueue(CreateEnvelope(nativeSaveId: 1));

        Assert.Equal(
            (true, true, suspensionGeneration),
            (
                resumed,
                loadAccepted,
                completion.QueueSuspensionGeneration));
    }

    [Fact]
    public async Task OlderResetRegistrationCannotReplaceNewerQueueSuspensionAsync()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        var ownership = new NeonLetterRestoreWorkOwnership();
        using var suspensionBarrier = new Barrier(2);
        using var releaseRegistration = new ManualResetEventSlim();
        Task<(ulong Generation, bool Owned)> olderRegistration =
            Task.Run(
                () =>
                {
                    ulong generation = queue.SuspendAndClear();
                    Assert.True(
                        suspensionBarrier.SignalAndWait(TestTimeout));
                    Assert.True(
                        releaseRegistration.Wait(TestTimeout));
                    bool owned = ownership.RequestReset(
                        rollbackOwnedFallbacks: true,
                        resumeLoads: true,
                        generation,
                        out _);
                    return (generation, owned);
                });
        Assert.True(suspensionBarrier.SignalAndWait(TestTimeout));

        ulong newerGeneration;
        NeonLetterRestoreResetOwnership reset;
        try
        {
            newerGeneration = queue.SuspendAndClear();
            Assert.True(
                ownership.RequestReset(
                    rollbackOwnedFallbacks: false,
                    resumeLoads: true,
                    newerGeneration,
                    out reset));
        }
        finally
        {
            releaseRegistration.Set();
        }

        (ulong olderGeneration, bool olderOwned) =
            await olderRegistration.WaitAsync(TestTimeout);
        NeonLetterRestoreResetRequest request =
            ownership.GetResetRequest(reset);
        Assert.True(
            ownership.TryCompleteReset(
                reset,
                request.Version,
                rollbackSatisfied: true,
                out _,
                out NeonLetterRestoreResetCompletion completion));
        bool resumed = queue.Resume(
            completion.QueueSuspensionGeneration);
        bool loadAccepted = queue.Enqueue(CreateEnvelope(nativeSaveId: 1));

        Assert.Equal(
            (false, true, newerGeneration, true, true, true),
            (
                olderOwned,
                request.RollbackOwnedFallbacks,
                completion.QueueSuspensionGeneration,
                newerGeneration > olderGeneration,
                resumed,
                loadAccepted));
    }

    [Fact]
    public void NewerResetRegistrationReplacesOlderQueueSuspension()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        var ownership = new NeonLetterRestoreWorkOwnership();
        ulong olderGeneration = queue.SuspendAndClear();
        Assert.True(
            ownership.RequestReset(
                rollbackOwnedFallbacks: false,
                resumeLoads: true,
                olderGeneration,
                out var reset));
        ulong newerGeneration = queue.SuspendAndClear();
        Assert.False(
            ownership.RequestReset(
                rollbackOwnedFallbacks: true,
                resumeLoads: true,
                newerGeneration,
                out _));
        NeonLetterRestoreResetRequest request =
            ownership.GetResetRequest(reset);
        Assert.True(
            ownership.TryCompleteReset(
                reset,
                request.Version,
                rollbackSatisfied: true,
                out _,
                out NeonLetterRestoreResetCompletion completion));

        bool resumed = queue.Resume(
            completion.QueueSuspensionGeneration);
        bool loadAccepted = queue.Enqueue(CreateEnvelope(nativeSaveId: 1));

        Assert.Equal(
            (true, newerGeneration, true, true),
            (
                request.RollbackOwnedFallbacks,
                completion.QueueSuspensionGeneration,
                resumed,
                loadAccepted));
    }

    [Fact]
    public async Task DeinitializeSealSurvivesLaterWeakResetCompletionAsync()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        var ownership = new NeonLetterRestoreWorkOwnership();
        using var sealedBarrier = new Barrier(2);
        using var releaseRegistration = new ManualResetEventSlim();
        Task<(ulong SealedGeneration, ulong FinalGeneration)> deinitialize =
            Task.Run(
                () =>
                {
                    ulong sealedGeneration = queue.SealAndClear();
                    Assert.True(sealedBarrier.SignalAndWait(TestTimeout));
                    Assert.True(
                        releaseRegistration.Wait(TestTimeout));
                    Assert.True(
                        ownership.RequestReset(
                            rollbackOwnedFallbacks: true,
                            resumeLoads: false,
                            sealedGeneration,
                            out var reset));
                    NeonLetterRestoreResetRequest request =
                        ownership.GetResetRequest(reset);
                    Assert.True(
                        ownership.TryCompleteReset(
                            reset,
                            request.Version,
                            rollbackSatisfied: true,
                            out _,
                            out NeonLetterRestoreResetCompletion completion));
                    Assert.False(completion.ResumeLoads);
                    return (
                        sealedGeneration,
                        queue.SuspendAndClear());
                });
        Assert.True(sealedBarrier.SignalAndWait(TestTimeout));

        ulong weakGeneration;
        NeonLetterRestoreResetCompletion weakCompletion;
        bool weakResume;
        try
        {
            weakGeneration = queue.SuspendAndClear();
            Assert.True(
                ownership.RequestReset(
                    rollbackOwnedFallbacks: false,
                    resumeLoads: true,
                    weakGeneration,
                    out var weakReset));
            NeonLetterRestoreResetRequest weakRequest =
                ownership.GetResetRequest(weakReset);
            Assert.True(
                ownership.TryCompleteReset(
                    weakReset,
                    weakRequest.Version,
                    rollbackSatisfied: false,
                    out _,
                    out weakCompletion));
            weakResume = queue.Resume(
                weakCompletion.QueueSuspensionGeneration);
        }
        finally
        {
            releaseRegistration.Set();
        }

        (ulong sealedGeneration, ulong finalGeneration) =
            await deinitialize.WaitAsync(TestTimeout);
        bool staleResume = queue.Resume(
            weakCompletion.QueueSuspensionGeneration);
        bool freshResume = queue.Resume(finalGeneration);
        bool loadAccepted = queue.Enqueue(CreateEnvelope(nativeSaveId: 1));

        Assert.Equal(
            (false, false, false, false, true, true),
            (
                weakResume,
                staleResume,
                freshResume,
                loadAccepted,
                weakGeneration > sealedGeneration,
                finalGeneration > weakGeneration));
    }

    [Fact]
    public async Task RoleLossReassertsSuspensionAfterWeakResetCompletionAsync()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        var ownership = new NeonLetterRestoreWorkOwnership();
        using var suspensionBarrier = new Barrier(2);
        using var releaseRegistration = new ManualResetEventSlim();
        Task<ulong> roleLoss = Task.Run(
            () =>
            {
                ulong generation = queue.SuspendAndClear();
                Assert.True(
                    suspensionBarrier.SignalAndWait(TestTimeout));
                Assert.True(releaseRegistration.Wait(TestTimeout));
                Assert.True(
                    ownership.RequestReset(
                        rollbackOwnedFallbacks: true,
                        resumeLoads: false,
                        generation,
                        out var reset));
                NeonLetterRestoreResetRequest request =
                    ownership.GetResetRequest(reset);
                Assert.True(
                    ownership.TryCompleteReset(
                        reset,
                        request.Version,
                        rollbackSatisfied: true,
                        out _,
                        out NeonLetterRestoreResetCompletion completion));
                Assert.False(completion.ResumeLoads);
                return queue.SuspendAndClear();
            });
        Assert.True(suspensionBarrier.SignalAndWait(TestTimeout));

        NeonLetterRestoreResetCompletion weakCompletion;
        try
        {
            ulong weakGeneration = queue.SuspendAndClear();
            Assert.True(
                ownership.RequestReset(
                    rollbackOwnedFallbacks: false,
                    resumeLoads: true,
                    weakGeneration,
                    out var weakReset));
            NeonLetterRestoreResetRequest weakRequest =
                ownership.GetResetRequest(weakReset);
            Assert.True(
                ownership.TryCompleteReset(
                    weakReset,
                    weakRequest.Version,
                    rollbackSatisfied: false,
                    out _,
                    out weakCompletion));
            Assert.True(
                queue.Resume(
                    weakCompletion.QueueSuspensionGeneration));
        }
        finally
        {
            releaseRegistration.Set();
        }

        ulong currentGeneration =
            await roleLoss.WaitAsync(TestTimeout);
        bool loadWhileSuspended =
            queue.Enqueue(CreateEnvelope(nativeSaveId: 1));
        bool staleResume = queue.Resume(
            weakCompletion.QueueSuspensionGeneration);
        bool hostResume = queue.Resume(currentGeneration);
        bool hostLoad = queue.Enqueue(CreateEnvelope(nativeSaveId: 2));

        Assert.Equal(
            (false, false, true, true),
            (
                loadWhileSuspended,
                staleResume,
                hostResume,
                hostLoad));
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
