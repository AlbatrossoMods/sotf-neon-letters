using SOTFNeonLetters;
using Xunit;

public sealed class RestoreStateMachineBehaviorTests
{
    [Fact]
    public void LoadQueueResumeIsOneShotAndASealRequiresFreshInitialization()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();

        ulong suspendedGeneration = queue.SuspendAndClear();
        bool firstResume = queue.Resume(suspendedGeneration);
        bool repeatedResume = queue.Resume(suspendedGeneration);
        ulong sealedGeneration = queue.SealAndClear();
        bool sealedResume = queue.Resume(sealedGeneration);
        bool sealedLoad = queue.Enqueue(CreateMultiplayerEnvelope(1));
        ulong initializedGeneration = queue.ResetForInitialization();
        bool initializedResume = queue.Resume(initializedGeneration);
        bool initializedLoad = queue.Enqueue(CreateMultiplayerEnvelope(2));

        Assert.Equal(
            (true, false, false, false, true, true),
            (
                firstResume,
                repeatedResume,
                sealedResume,
                sealedLoad,
                initializedResume,
                initializedLoad));
    }

    [Fact]
    public void DetachedCleanupChoosesRollbackOrAbandonExactlyOnce()
    {
        int rolledBackCount = 0;
        int abandonedCount = 0;
        var rolledBack = new NeonLetterDetachedRestoreCleanup(
            () => rolledBackCount++);
        var abandoned = new NeonLetterDetachedRestoreCleanup(
            () => abandonedCount++);

        rolledBack.Rollback();
        rolledBack.Rollback();
        rolledBack.Abandon();
        abandoned.Abandon();
        abandoned.Rollback();

        Assert.Equal((1, 0), (rolledBackCount, abandonedCount));
    }

    [Fact]
    public void ReadinessIssuesTokensOnTheExactProbeBoundaryAndProgressChange()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();

        bool initialDue = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 10,
            waveActive: false,
            out ulong initialToken);
        bool earlyDue = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 17,
            waveActive: false,
            out ulong earlyToken);
        bool boundaryDue = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 18,
            waveActive: false,
            out ulong boundaryToken);
        bool changedDue = scheduler.TryGetDueToken(
            observedProgress: 2,
            updateTick: 18,
            waveActive: false,
            out ulong changedToken);
        bool repeatedDue = scheduler.TryGetDueToken(
            observedProgress: 2,
            updateTick: 18,
            waveActive: false,
            out ulong repeatedToken);

        Assert.Equal(
            (true, false, true, true, false, true, true, true),
            (
                initialDue,
                earlyDue,
                boundaryDue,
                changedDue,
                repeatedDue,
                earlyToken == initialToken,
                boundaryToken > initialToken,
                changedToken > boundaryToken &&
                    repeatedToken == changedToken));
    }

    [Fact]
    public void ReadinessRejectsARegressingUpdateTickWithoutChangingItsToken()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();
        scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 10,
            waveActive: false,
            out ulong token);

        Exception? exception = Record.Exception(
            () => scheduler.TryGetDueToken(
                observedProgress: 1,
                updateTick: 9,
                waveActive: false,
                out _));

        Assert.Equal(
            (typeof(ArgumentOutOfRangeException), token),
            (exception?.GetType(), scheduler.CurrentToken));
    }

    [Fact]
    public void StrongerResetRequestedDuringCleanupControlsFinalCompletion()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        bool updateStarted = ownership.TryBeginUpdate(out var update);
        bool weakResetStarted = ownership.RequestReset(
            rollbackOwnedFallbacks: false,
            resumeLoads: true,
            queueSuspensionGeneration: 3,
            out _);
        bool updateRemainsCurrent = ownership.IsUpdateCurrent(update);
        bool weakRequestFound = ownership.TryGetPendingResetRequest(
            update,
            out NeonLetterRestoreResetRequest weakRequest);
        bool ownershipTransferred = ownership.CompleteUpdate(
            update,
            out var reset);
        bool strongResetStarted = ownership.RequestReset(
            rollbackOwnedFallbacks: true,
            resumeLoads: false,
            queueSuspensionGeneration: 7,
            out _);
        bool weakCompletion = ownership.TryCompleteReset(
            reset,
            weakRequest.Version,
            rollbackSatisfied: false,
            out NeonLetterRestoreResetRequest strongerRequest,
            out _);
        bool strongCompletion = ownership.TryCompleteReset(
            reset,
            strongerRequest.Version,
            rollbackSatisfied: true,
            out _,
            out NeonLetterRestoreResetCompletion completion);
        bool nextUpdateStarted = ownership.TryBeginUpdate(out _);

        Assert.Equal(
            (
                true,
                false,
                false,
                true,
                true,
                false,
                false,
                true,
                false,
                7UL,
                true),
            (
                updateStarted,
                weakResetStarted,
                updateRemainsCurrent,
                weakRequestFound,
                ownershipTransferred,
                strongResetStarted,
                weakCompletion,
                strongCompletion,
                completion.ResumeLoads,
                completion.QueueSuspensionGeneration,
                nextUpdateStarted));
    }

    [Fact]
    public void FailedQueueSuspensionAndInitializationTransitionsRemainClosed()
    {
        var suspendedQueue = new NeonLetterMultiplayerRestoreLoadQueue(
            new NeonLetterMonotonicSequence(ulong.MaxValue));
        var conditionalQueue = new NeonLetterMultiplayerRestoreLoadQueue(
            new NeonLetterMonotonicSequence(ulong.MaxValue));
        var initializedQueue = new NeonLetterMultiplayerRestoreLoadQueue(
            new NeonLetterMonotonicSequence(ulong.MaxValue));

        Exception? suspensionFailure = Record.Exception(
            () => suspendedQueue.SuspendAndClear());
        Exception? conditionalFailure = Record.Exception(
            () => conditionalQueue.TrySuspendAndClear(
                ulong.MaxValue,
                out _));
        Exception? initializationFailure = Record.Exception(
            () => initializedQueue.ResetForInitialization());

        Assert.Equal(
            (
                typeof(InvalidOperationException),
                false,
                false,
                typeof(InvalidOperationException),
                false,
                false,
                typeof(InvalidOperationException),
                false,
                false),
            (
                suspensionFailure?.GetType(),
                suspendedQueue.Resume(ulong.MaxValue),
                suspendedQueue.Enqueue(CreateMultiplayerEnvelope(1)),
                conditionalFailure?.GetType(),
                conditionalQueue.Resume(ulong.MaxValue),
                conditionalQueue.Enqueue(CreateMultiplayerEnvelope(2)),
                initializationFailure?.GetType(),
                initializedQueue.Resume(ulong.MaxValue),
                initializedQueue.Enqueue(CreateMultiplayerEnvelope(3))));
    }

    [Fact]
    public void MultiplayerWorkTokensReflectRoleWaveAndPendingState()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);

        bool initialWork = coordinator.HasWorkForToken(readinessToken: 1);
        coordinator.AdvanceForReadinessToken(
            readinessToken: 1,
            maxItems: 1,
            maxFallbackSpawns: 0,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetUnavailable),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        bool exhaustedWave = coordinator.HasWorkForToken(readinessToken: 1);
        bool freshWave = coordinator.HasWorkForToken(readinessToken: 2);
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Unknown);
        bool unknownRoleWork = coordinator.HasWorkForToken(readinessToken: 2);
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        coordinator.AdvanceForReadinessToken(
            readinessToken: 2,
            maxItems: 1,
            maxFallbackSpawns: 0,
            observe: (entry, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetReady,
                    new DisposableTarget(() => { }),
                    entry.RecipeId),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (true, false, true, false, false, 0),
            (
                initialWork,
                exhaustedWave,
                freshWave,
                unknownRoleWork,
                coordinator.HasWorkForToken(readinessToken: 3),
                coordinator.PendingCount));
    }

    [Fact]
    public void ReapplyingTheHostRoleDoesNotReplaceTheRestoreEpoch()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        ulong activeEpoch = coordinator.RestoreEpoch;

        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);

        Assert.Equal(
            (activeEpoch, 1, NeonLetterMultiplayerRestoreRole.Host),
            (
                coordinator.RestoreEpoch,
                coordinator.PendingCount,
                coordinator.Role));
    }

    [Fact]
    public void ClearingDuringFallbackStartRollsBackTheDetachedTarget()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int rollbackCount = 0;

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ =>
            {
                coordinator.Clear();
                return new DisposableTarget(() => rollbackCount++);
            },
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (1, 0, 0, NeonLetterMultiplayerRestoreRole.Unknown),
            (
                rollbackCount,
                coordinator.PendingCount,
                coordinator.StartedFallbackCount,
                coordinator.Role));
    }

    [Fact]
    public void DeferredMultiplayerTokenStartsANewFairBoundedWave()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(
            CreateMultiplayerEnvelope(Enumerable.Range(1, 18).ToArray()));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var attemptedSaveIds = new List<int>();
        bool nestedAdvanceReturned = false;

        void RecordUnavailable(
            NeonLetterMultiplayerSaveEntry entry,
            bool _,
            DisposableTarget? __)
        {
            attemptedSaveIds.Add(entry.NativeSaveId);
        }

        coordinator.AdvanceForReadinessToken(
            readinessToken: 1,
            maxItems: 16,
            maxFallbackSpawns: 0,
            observe: (entry, fallbackStarted, target) =>
            {
                RecordUnavailable(entry, fallbackStarted, target);
                if (!nestedAdvanceReturned)
                {
                    coordinator.AdvanceForReadinessToken(
                        readinessToken: 2,
                        maxItems: 16,
                        maxFallbackSpawns: 0,
                        observe: (_, _, _) =>
                            throw new InvalidOperationException(
                                "Nested restore work must be deferred."),
                        startFallback: _ =>
                            throw new InvalidOperationException(),
                        applyRestored: (_, _) => true,
                        onEntryError: (_, exception) => throw exception);
                    nestedAdvanceReturned = true;
                }

                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        coordinator.AdvanceForReadinessToken(
            readinessToken: 1,
            maxItems: 16,
            maxFallbackSpawns: 0,
            observe: (entry, fallbackStarted, target) =>
            {
                RecordUnavailable(entry, fallbackStarted, target);
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        coordinator.AdvanceForReadinessToken(
            readinessToken: 2,
            maxItems: 16,
            maxFallbackSpawns: 0,
            observe: (entry, fallbackStarted, target) =>
            {
                RecordUnavailable(entry, fallbackStarted, target);
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (
                true,
                string.Join(
                    ",",
                    Enumerable.Range(1, 18)
                        .Concat(Enumerable.Range(1, 16))),
                false,
                18),
            (
                nestedAdvanceReturned,
                string.Join(",", attemptedSaveIds),
                coordinator.HasWorkForToken(readinessToken: 2),
                coordinator.PendingCount));
    }

    [Fact]
    public void ReentrantMultiplayerStageStopsTheStaleSliceBeforeApply()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1, 2));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var observedSaveIds = new List<int>();
        var appliedSaveIds = new List<int>();

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (entry, _, _) =>
            {
                observedSaveIds.Add(entry.NativeSaveId);
                coordinator.Stage(CreateMultiplayerEnvelope(9));
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetReady,
                    new DisposableTarget(() => { }),
                    entry.RecipeId);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (entry, _) =>
            {
                appliedSaveIds.Add(entry.NativeSaveId);
                return true;
            },
            onEntryError: (_, exception) => throw exception);
        int pendingAfterReplacement = coordinator.PendingCount;
        coordinator.Advance(
            nowSeconds: 1d,
            observe: (entry, _, _) =>
            {
                observedSaveIds.Add(entry.NativeSaveId);
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetReady,
                    new DisposableTarget(() => { }),
                    entry.RecipeId);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (entry, _) =>
            {
                appliedSaveIds.Add(entry.NativeSaveId);
                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            ("1,9", "9", 1, 0),
            (
                string.Join(",", observedSaveIds),
                string.Join(",", appliedSaveIds),
                pendingAfterReplacement,
                coordinator.PendingCount));
    }

    [Fact]
    public void MultiplayerCursorContinuesAfterRemovingMiddleAndLastEntries()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1, 2, 3));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var observedSaveIds = new List<int>();
        var appliedSaveIds = new List<int>();

        for (int slice = 0; slice < 4; slice++)
        {
            coordinator.Advance(
                nowSeconds: slice,
                maxItems: 1,
                maxFallbackSpawns: 0,
                observe: (entry, _, _) =>
                {
                    observedSaveIds.Add(entry.NativeSaveId);
                    return entry.NativeSaveId == 1 &&
                           observedSaveIds.Count == 1
                        ? new NeonLetterMultiplayerRestoreObservation<
                            DisposableTarget>(
                                NeonLetterMultiplayerRestoreObservationKind
                                    .NativeTargetUnavailable)
                        : new NeonLetterMultiplayerRestoreObservation<
                            DisposableTarget>(
                                NeonLetterMultiplayerRestoreObservationKind
                                    .NativeTargetReady,
                            new DisposableTarget(() => { }),
                            entry.RecipeId);
                },
                startFallback: _ => throw new InvalidOperationException(),
                applyRestored: (entry, _) =>
                {
                    appliedSaveIds.Add(entry.NativeSaveId);
                    return true;
                },
                onEntryError: (_, exception) => throw exception);
        }

        Assert.Equal(
            ("1,2,3,1", "2,3,1", 0),
            (
                string.Join(",", observedSaveIds),
                string.Join(",", appliedSaveIds),
                coordinator.PendingCount));
    }

    [Fact]
    public void ClearingMultipleFallbacksRollsBackEveryOwnedTarget()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(0, 0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int rollbackCount = 0;

        coordinator.Advance(
            nowSeconds: 0d,
            maxItems: 2,
            maxFallbackSpawns: 2,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ =>
                new DisposableTarget(() => rollbackCount++),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        int startedBeforeClear = coordinator.StartedFallbackCount;

        coordinator.Clear();

        Assert.Equal(
            (2, 2, 0, 0),
            (
                startedBeforeClear,
                rollbackCount,
                coordinator.StartedFallbackCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void CleanupReportsRollbackFailureAndStillReleasesOwnership()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var rollbackFailure = new InvalidOperationException(
            "rollback failed");
        var reportedFailures = new List<Exception>();

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ =>
                new DisposableTarget(() => throw rollbackFailure),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) =>
                reportedFailures.Add(exception));

        Exception? clearFailure = Record.Exception(coordinator.Clear);

        Assert.Equal(
            (null, 1, true, 0, 0),
            (
                clearFailure,
                reportedFailures.Count,
                ReferenceEquals(rollbackFailure, reportedFailures.Single()),
                coordinator.PendingCount,
                coordinator.StartedFallbackCount));
    }

    [Fact]
    public void RestoreUpdateOwnershipIsExclusiveAndResetInvalidatesItsOwner()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        bool firstStarted = ownership.TryBeginUpdate(out var first);
        bool secondStarted = ownership.TryBeginUpdate(out _);
        bool initiallyCurrent = ownership.IsUpdateCurrent(first);
        var initialImpostor = new NeonLetterRestoreUpdateOwnership(
            new object(),
            first.Generation);
        bool initialImpostorCurrent = ownership.IsUpdateCurrent(
            initialImpostor);
        bool resetStarted = ownership.RequestReset(
            rollbackOwnedFallbacks: false,
            resumeLoads: true,
            queueSuspensionGeneration: 1,
            out _);
        bool currentAfterReset = ownership.IsUpdateCurrent(first);
        bool pendingFound = ownership.TryGetPendingResetRequest(
            first,
            out _);
        var impostor = new NeonLetterRestoreUpdateOwnership(
            new object(),
            first.Generation);
        bool impostorCurrent = ownership.IsUpdateCurrent(impostor);
        bool impostorPending = ownership.TryGetPendingResetRequest(
            impostor,
            out _);
        bool transferred = ownership.CompleteUpdate(first, out var reset);
        bool staleCompletion = ownership.CompleteUpdate(first, out _);
        NeonLetterRestoreResetRequest request =
            ownership.GetResetRequest(reset);
        bool completed = ownership.TryCompleteReset(
            reset,
            request.Version,
            rollbackSatisfied: true,
            out _,
            out _);

        Assert.Equal(
            (
                true,
                false,
                true,
                false,
                false,
                false,
                true,
                false,
                false,
                true,
                false,
                true),
            (
                firstStarted,
                secondStarted,
                initiallyCurrent,
                initialImpostorCurrent,
                resetStarted,
                currentAfterReset,
                pendingFound,
                impostorCurrent,
                impostorPending,
                transferred,
                staleCompletion,
                completed));
    }

    [Fact]
    public void RoleChangeDuringObservationCannotApplyTheStaleTarget()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int staleApplyCount = 0;

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (entry, _, _) =>
            {
                coordinator.SetRole(
                    NeonLetterMultiplayerRestoreRole.Unknown);
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetReady,
                    new DisposableTarget(() => { }),
                    entry.RecipeId);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) =>
            {
                staleApplyCount++;
                return true;
            },
            onEntryError: (_, exception) => throw exception);
        int pendingWhileRoleUnknown = coordinator.PendingCount;
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        coordinator.Advance(
            nowSeconds: 1d,
            observe: (entry, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetReady,
                    new DisposableTarget(() => { }),
                    entry.RecipeId),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (0, 1, 0),
            (
                staleApplyCount,
                pendingWhileRoleUnknown,
                coordinator.PendingCount));
    }

    [Fact]
    public void CompletedStrongResetDoesNotStrengthenTheNextWeakReset()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        bool strongStarted = ownership.RequestReset(
            rollbackOwnedFallbacks: true,
            resumeLoads: false,
            queueSuspensionGeneration: 4,
            out var strongOwner);
        NeonLetterRestoreResetRequest strongRequest =
            ownership.GetResetRequest(strongOwner);
        bool strongCompleted = ownership.TryCompleteReset(
            strongOwner,
            strongRequest.Version,
            rollbackSatisfied: true,
            out _,
            out NeonLetterRestoreResetCompletion strongCompletion);
        bool weakStarted = ownership.RequestReset(
            rollbackOwnedFallbacks: false,
            resumeLoads: true,
            queueSuspensionGeneration: 9,
            out var weakOwner);
        NeonLetterRestoreResetRequest weakRequest =
            ownership.GetResetRequest(weakOwner);
        bool weakCompleted = ownership.TryCompleteReset(
            weakOwner,
            weakRequest.Version,
            rollbackSatisfied: false,
            out _,
            out NeonLetterRestoreResetCompletion weakCompletion);

        Assert.Equal(
            (
                true,
                true,
                false,
                4UL,
                true,
                false,
                true,
                true,
                9UL),
            (
                strongStarted,
                strongCompleted,
                strongCompletion.ResumeLoads,
                strongCompletion.QueueSuspensionGeneration,
                weakStarted,
                weakRequest.RollbackOwnedFallbacks,
                weakCompleted,
                weakCompletion.ResumeLoads,
                weakCompletion.QueueSuspensionGeneration));
    }

    [Fact]
    public void ACompletedResetOwnerCannotControlTheNextReset()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        ownership.RequestReset(
            rollbackOwnedFallbacks: false,
            resumeLoads: true,
            queueSuspensionGeneration: 1,
            out var firstOwner);
        NeonLetterRestoreResetRequest firstRequest =
            ownership.GetResetRequest(firstOwner);
        ownership.TryCompleteReset(
            firstOwner,
            firstRequest.Version,
            rollbackSatisfied: true,
            out _,
            out _);
        ownership.RequestReset(
            rollbackOwnedFallbacks: false,
            resumeLoads: true,
            queueSuspensionGeneration: 2,
            out var secondOwner);

        Exception? staleReadFailure = Record.Exception(
            () => ownership.GetResetRequest(firstOwner));
        Exception? staleCompletionFailure = Record.Exception(
            () => ownership.TryCompleteReset(
                firstOwner,
                firstRequest.Version,
                rollbackSatisfied: true,
                out _,
                out _));
        NeonLetterRestoreResetRequest secondRequest =
            ownership.GetResetRequest(secondOwner);
        bool secondCompleted = ownership.TryCompleteReset(
            secondOwner,
            secondRequest.Version,
            rollbackSatisfied: true,
            out _,
            out _);

        Assert.Equal(
            (
                typeof(InvalidOperationException),
                typeof(InvalidOperationException),
                true),
            (
                staleReadFailure?.GetType(),
                staleCompletionFailure?.GetType(),
                secondCompleted));
    }

    [Fact]
    public void ActiveReadinessWaveMovesTheSafetyProbeAtItsBoundary()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();
        scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 10,
            waveActive: false,
            out ulong initialToken);
        bool activeBoundaryDue = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 18,
            waveActive: true,
            out ulong activeBoundaryToken);
        bool immediatelyAfterDue = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 19,
            waveActive: false,
            out ulong immediatelyAfterToken);
        bool rescheduledBoundaryDue = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 26,
            waveActive: false,
            out ulong rescheduledToken);

        Assert.Equal(
            (false, false, true, true, true),
            (
                activeBoundaryDue,
                immediatelyAfterDue,
                rescheduledBoundaryDue,
                activeBoundaryToken == initialToken &&
                    immediatelyAfterToken == initialToken,
                rescheduledToken > initialToken));
    }

    [Fact]
    public void SinglePlayerWorkTokensReflectEpochWaveAndPendingState()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(new[] { 1 }),
            nowSeconds: 0d);
        bool initialWork = coordinator.HasWorkForToken(
            epoch,
            readinessToken: 1);
        bool staleEpochWork = coordinator.HasWorkForToken(
            epoch - 1,
            readinessToken: 1);
        coordinator.Advance(
            epoch,
            readinessToken: 1,
            _ => NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable);
        bool exhaustedWave = coordinator.HasWorkForToken(
            epoch,
            readinessToken: 1);
        bool freshWave = coordinator.HasWorkForToken(
            epoch,
            readinessToken: 2);
        int applied = coordinator.Advance(
            epoch,
            readinessToken: 2,
            _ => NeonLetterSinglePlayerRestoreAttemptResult.Applied);

        Assert.Equal(
            (true, false, false, true, 1, false, 0),
            (
                initialWork,
                staleEpochWork,
                exhaustedWave,
                freshWave,
                applied,
                coordinator.HasWorkForToken(epoch, readinessToken: 3),
                coordinator.PendingCount));
    }

    [Fact]
    public void ReentrantSinglePlayerStageStopsTheStaleTokenSlice()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(new[] { 1, 2 }),
            nowSeconds: 0d);
        long replacementEpoch = 0;
        var attemptedSaveIds = new List<int>();

        int staleApplied = coordinator.Advance(
            epoch,
            readinessToken: 1,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                replacementEpoch = coordinator.Stage(
                    CreateSinglePlayerEnvelope(new[] { 9 }),
                    nowSeconds: 1d);
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });
        int pendingAfterReplacement = coordinator.PendingCount;
        int replacementApplied = coordinator.Advance(
            replacementEpoch,
            readinessToken: 2,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            (0, 1, 1, "1,9", 0),
            (
                staleApplied,
                pendingAfterReplacement,
                replacementApplied,
                string.Join(",", attemptedSaveIds),
                coordinator.PendingCount));
    }

    [Fact]
    public void SinglePlayerCompatibilityRetryStopsAtItsDocumentedBudget()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(Enumerable.Range(1, 17)),
            nowSeconds: 0d);
        var outcomes = new Queue<NeonLetterSinglePlayerRestoreAttemptResult>(
            Enumerable.Repeat(
                NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable,
                NeonLetterSinglePlayerRestoreCoordinator.MaxAttemptsPerTick));
        var attemptedSaveIds = new List<int>();

        int applied = coordinator.Advance(
            epoch,
            nowSeconds: 1d,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return outcomes.Dequeue();
            });

        Assert.Equal(
            (
                0,
                string.Join(
                    ",",
                    Enumerable.Range(
                        1,
                        NeonLetterSinglePlayerRestoreCoordinator
                            .MaxAttemptsPerTick)),
                17,
                0),
            (
                applied,
                string.Join(",", attemptedSaveIds),
                coordinator.PendingCount,
                outcomes.Count));
    }

    [Fact]
    public void LeavingHostDuringFallbackStartRollsBackTheDetachedTarget()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int rollbackCount = 0;

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ =>
            {
                coordinator.SetRole(
                    NeonLetterMultiplayerRestoreRole.Client);
                return new DisposableTarget(() => rollbackCount++);
            },
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (1, 0, 0, NeonLetterMultiplayerRestoreRole.Client),
            (
                rollbackCount,
                coordinator.PendingCount,
                coordinator.StartedFallbackCount,
                coordinator.Role));
    }

    [Fact]
    public void MultiplayerCompatibilityAdvanceCannotBypassADeferredToken()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        Exception? nestedCompatibilityFailure = null;

        coordinator.AdvanceForReadinessToken(
            readinessToken: 1,
            maxItems: 1,
            maxFallbackSpawns: 0,
            observe: (_, _, _) =>
            {
                coordinator.AdvanceForReadinessToken(
                    readinessToken: 2,
                    maxItems: 1,
                    maxFallbackSpawns: 0,
                    observe: (_, _, _) =>
                        throw new InvalidOperationException(),
                    startFallback: _ => throw new InvalidOperationException(),
                    applyRestored: (_, _) => true,
                    onEntryError: (_, exception) => throw exception);
                nestedCompatibilityFailure = Record.Exception(
                    () => coordinator.Advance(
                        nowSeconds: 0d,
                        observe: (_, _, _) =>
                            throw new InvalidOperationException(),
                        startFallback: _ =>
                            throw new InvalidOperationException(),
                        applyRestored: (_, _) => true,
                        onEntryError: (_, exception) => throw exception));
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        int attemptsWithDeferredToken = 0;
        coordinator.AdvanceForReadinessToken(
            readinessToken: 1,
            maxItems: 1,
            maxFallbackSpawns: 0,
            observe: (_, _, _) =>
            {
                attemptsWithDeferredToken++;
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (null, 1, false, 1),
            (
                nestedCompatibilityFailure,
                attemptsWithDeferredToken,
                coordinator.HasWorkForToken(readinessToken: 2),
                coordinator.PendingCount));
    }

    [Fact]
    public void MultiplayerCursorResumesAfterTheMiddleOfAWrappedSlice()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1, 2, 3));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var observedSaveIds = new List<int>();

        void AdvanceSlice(int maxItems, bool applySecond)
        {
            coordinator.Advance(
                nowSeconds: observedSaveIds.Count,
                maxItems,
                maxFallbackSpawns: 0,
                observe: (entry, _, _) =>
                {
                    observedSaveIds.Add(entry.NativeSaveId);
                    return applySecond && entry.NativeSaveId == 2
                        ? new NeonLetterMultiplayerRestoreObservation<
                            DisposableTarget>(
                                NeonLetterMultiplayerRestoreObservationKind
                                    .NativeTargetReady,
                            new DisposableTarget(() => { }),
                            entry.RecipeId)
                        : new NeonLetterMultiplayerRestoreObservation<
                            DisposableTarget>(
                                NeonLetterMultiplayerRestoreObservationKind
                                    .NativeTargetUnavailable);
                },
                startFallback: _ => throw new InvalidOperationException(),
                applyRestored: (_, _) => true,
                onEntryError: (_, exception) => throw exception);
        }

        AdvanceSlice(maxItems: 1, applySecond: false);
        AdvanceSlice(maxItems: 3, applySecond: true);
        AdvanceSlice(maxItems: 1, applySecond: false);

        Assert.Equal(
            ("1,2,3,1,3", 2),
            (string.Join(",", observedSaveIds), coordinator.PendingCount));
    }

    [Fact]
    public void ExhaustedMultiplayerEpochRollsBackOwnedFallbacks()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>(
                new NeonLetterMonotonicSequence(ulong.MaxValue - 2));
        coordinator.Stage(CreateMultiplayerEnvelope(0));
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

        Exception? clearFailure = Record.Exception(coordinator.Clear);

        Assert.Equal(
            (
                typeof(InvalidOperationException),
                1,
                0,
                0,
                NeonLetterMultiplayerRestoreRole.Unknown),
            (
                clearFailure?.GetType(),
                rollbackCount,
                coordinator.PendingCount,
                coordinator.StartedFallbackCount,
                coordinator.Role));
    }

    [Fact]
    public void UpdateCompletionWithoutAResetReleasesOwnership()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        bool started = ownership.TryBeginUpdate(out var update);
        bool resetTransferred = ownership.CompleteUpdate(
            update,
            out NeonLetterRestoreResetOwnership reset);
        bool nextStarted = ownership.TryBeginUpdate(out _);

        Assert.Equal(
            (true, false, default, true),
            (started, resetTransferred, reset, nextStarted));
    }

    [Fact]
    public void ResetOwnerCannotBeImpersonatedByAnUpdateOwnership()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        ownership.TryBeginUpdate(out var update);
        ownership.RequestReset(
            rollbackOwnedFallbacks: false,
            resumeLoads: true,
            queueSuspensionGeneration: 1,
            out _);
        ownership.TryGetPendingResetRequest(
            update,
            out NeonLetterRestoreResetRequest request);
        ownership.CompleteUpdate(update, out var reset);
        var impersonator = new NeonLetterRestoreUpdateOwnership(
            reset.Token,
            request.Version);

        bool impersonatorCurrent = ownership.IsUpdateCurrent(impersonator);
        bool impersonatorRequest = ownership.TryGetPendingResetRequest(
            impersonator,
            out _);
        NeonLetterRestoreResetRequest ownedRequest =
            ownership.GetResetRequest(reset);
        bool completed = ownership.TryCompleteReset(
            reset,
            ownedRequest.Version,
            rollbackSatisfied: true,
            out _,
            out _);

        Assert.Equal(
            (false, false, true),
            (impersonatorCurrent, impersonatorRequest, completed));
    }

    [Fact]
    public void ActiveUpdateTokenCannotAuthorizeResetWork()
    {
        var ownership = new NeonLetterRestoreWorkOwnership();
        ownership.TryBeginUpdate(out var update);
        var resetImpersonator = new NeonLetterRestoreResetOwnership(
            update.Token);

        Exception? readFailure = Record.Exception(
            () => ownership.GetResetRequest(resetImpersonator));
        Exception? completionFailure = Record.Exception(
            () => ownership.TryCompleteReset(
                resetImpersonator,
                satisfiedVersion: 0,
                rollbackSatisfied: false,
                out _,
                out _));
        bool updateCompleted = !ownership.CompleteUpdate(update, out _);

        Assert.Equal(
            (
                typeof(InvalidOperationException),
                typeof(InvalidOperationException),
                true),
            (
                readFailure?.GetType(),
                completionFailure?.GetType(),
                updateCompleted));
    }

    [Fact]
    public void StaleSinglePlayerEpochCannotAttemptCurrentEntries()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long staleEpoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(new[] { 1 }),
            nowSeconds: 0d);
        long currentEpoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(new[] { 9 }),
            nowSeconds: 1d);
        int attemptCount = 0;

        int staleApplied = coordinator.Advance(
            staleEpoch,
            readinessToken: 1,
            _ =>
            {
                attemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });
        int currentApplied = coordinator.Advance(
            currentEpoch,
            readinessToken: 1,
            _ =>
            {
                attemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            (0, 1, 1, 0),
            (
                staleApplied,
                currentApplied,
                attemptCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void SinglePlayerCompatibilityAdvanceCannotBypassADeferredToken()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(new[] { 1 }),
            nowSeconds: 0d);
        Exception? nestedCompatibilityFailure = null;

        coordinator.Advance(
            epoch,
            readinessToken: 1,
            _ =>
            {
                coordinator.Advance(
                    epoch,
                    readinessToken: 2,
                    _ => throw new InvalidOperationException());
                nestedCompatibilityFailure = Record.Exception(
                    () => coordinator.Advance(
                        epoch,
                        nowSeconds: 0d,
                        _ => throw new InvalidOperationException()));
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });
        int attemptsWithDeferredToken = 0;
        coordinator.Advance(
            epoch,
            readinessToken: 1,
            _ =>
            {
                attemptsWithDeferredToken++;
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.Equal(
            (null, 1, false, 1),
            (
                nestedCompatibilityFailure,
                attemptsWithDeferredToken,
                coordinator.HasWorkForToken(epoch, readinessToken: 2),
                coordinator.PendingCount));
    }

    [Fact]
    public void DeferredSinglePlayerTokenStartsANewFairBoundedWave()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(Enumerable.Range(1, 18)),
            nowSeconds: 0d);
        var attemptedSaveIds = new List<int>();
        int nestedAppliedCount = -1;

        coordinator.Advance(
            epoch,
            readinessToken: 1,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                if (nestedAppliedCount < 0)
                {
                    nestedAppliedCount = coordinator.Advance(
                        epoch,
                        readinessToken: 2,
                        _ => throw new InvalidOperationException(
                            "Nested restore work must be deferred."));
                }

                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });
        coordinator.Advance(
            epoch,
            readinessToken: 1,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });
        coordinator.Advance(
            epoch,
            readinessToken: 2,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.Equal(
            (
                0,
                string.Join(
                    ",",
                    Enumerable.Range(1, 18)
                        .Concat(Enumerable.Range(1, 16))),
                false,
                18),
            (
                nestedAppliedCount,
                string.Join(",", attemptedSaveIds),
                coordinator.HasWorkForToken(epoch, readinessToken: 2),
                coordinator.PendingCount));
    }

    [Fact]
    public void SinglePlayerMixedResultsRetainOnlyUnavailableEntriesInOrder()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(Enumerable.Range(1, 4)),
            nowSeconds: 0d);
        var attemptedSaveIds = new List<int>();
        var outcomes = new Dictionary<
            int,
            NeonLetterSinglePlayerRestoreAttemptResult>
        {
            [1] = NeonLetterSinglePlayerRestoreAttemptResult.Applied,
            [2] = NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable,
            [3] = NeonLetterSinglePlayerRestoreAttemptResult.Terminal,
            [4] = NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable
        };

        coordinator.Advance(
            epoch,
            readinessToken: 1,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return outcomes[entry.SaveId];
            });
        coordinator.Advance(
            epoch,
            readinessToken: 2,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            ("1,2,3,4,2,4", 0),
            (string.Join(",", attemptedSaveIds), coordinator.PendingCount));
    }

    [Fact]
    public void ReplacingRestoreDuringFallbackStartRollsBackOnlyTheStaleTarget()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int observationCount = 0;
        int rollbackCount = 0;
        var appliedSaveIds = new List<int>();

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
            {
                observationCount++;
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .ReadyToSpawnFallback);
            },
            startFallback: _ =>
            {
                coordinator.Stage(CreateMultiplayerEnvelope(2));
                return new DisposableTarget(() => rollbackCount++);
            },
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        coordinator.Advance(
            nowSeconds: 1d,
            observe: (entry, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetReady,
                    new DisposableTarget(() => { }),
                    entry.RecipeId),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (entry, _) =>
            {
                appliedSaveIds.Add(entry.NativeSaveId);
                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (2, 1, "2", 0, 0),
            (
                observationCount,
                rollbackCount,
                string.Join(",", appliedSaveIds),
                coordinator.StartedFallbackCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void FailedFallbackApplyRemainsOwnedUntilCleanupAndRollsBackOnce()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(0));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int rollbackCount = 0;
        int applyCount = 0;
        var target = new DisposableTarget(() => rollbackCount++);

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ => target,
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, spawnedTarget) =>
                new NeonLetterMultiplayerRestoreObservation<DisposableTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .FallbackTargetReady,
                    spawnedTarget),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) =>
            {
                applyCount++;
                return false;
            },
            onEntryError: (_, exception) => throw exception);
        int pendingBeforeCleanup = coordinator.PendingCount;
        int startedBeforeCleanup = coordinator.StartedFallbackCount;

        coordinator.Clear();
        coordinator.Clear();

        Assert.Equal(
            (1, 1, 1, 1, 0, 0),
            (
                applyCount,
                pendingBeforeCleanup,
                startedBeforeCleanup,
                rollbackCount,
                coordinator.PendingCount,
                coordinator.StartedFallbackCount));
    }

    [Fact]
    public void NativeMismatchIsTerminalWhileUnavailableEntryCanLaterApply()
    {
        int differentRecipeId = NeonLetterSmallCatalog.Get('B').RecipeId;
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<DisposableTarget>();
        coordinator.Stage(CreateMultiplayerEnvelope(1, 2));
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var observedSaveIds = new List<int>();
        var appliedSaveIds = new List<int>();

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (entry, _, _) =>
            {
                observedSaveIds.Add(entry.NativeSaveId);
                return entry.NativeSaveId == 1
                    ? new NeonLetterMultiplayerRestoreObservation<
                        DisposableTarget>(
                            NeonLetterMultiplayerRestoreObservationKind
                                .NativeRecipeMismatch,
                            ResolvedRecipeId: differentRecipeId)
                    : new NeonLetterMultiplayerRestoreObservation<
                        DisposableTarget>(
                            NeonLetterMultiplayerRestoreObservationKind
                                .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        int pendingAfterClassification = coordinator.PendingCount;
        coordinator.Advance(
            nowSeconds: 1d,
            observe: (entry, _, _) =>
            {
                observedSaveIds.Add(entry.NativeSaveId);
                return new NeonLetterMultiplayerRestoreObservation<
                    DisposableTarget>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetReady,
                    new DisposableTarget(() => { }),
                    entry.RecipeId);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (entry, _) =>
            {
                appliedSaveIds.Add(entry.NativeSaveId);
                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            ("1,2,2", 1, "2", 0),
            (
                string.Join(",", observedSaveIds),
                pendingAfterClassification,
                string.Join(",", appliedSaveIds),
                coordinator.PendingCount));
    }

    private static NeonLetterColorSaveEnvelope CreateSinglePlayerEnvelope(
        IEnumerable<int> saveIds)
    {
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        return new NeonLetterColorSaveEnvelope
        {
            Entries = saveIds
                .Select(
                    saveId => new NeonLetterColorSaveEntry(
                        saveId,
                        recipeId,
                        NeonRgba.ProjectCyan))
                .ToList()
        };
    }

    private static NeonLetterMultiplayerSaveEnvelope
        CreateMultiplayerEnvelope(params int[] nativeSaveIds)
    {
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        return new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = nativeSaveIds
                .Select(
                    nativeSaveId => new NeonLetterMultiplayerSaveEntry
                    {
                        RecipeId = recipeId,
                        NativeSaveId = nativeSaveId,
                        Position = new NeonVector3(0f, 0f, 0f),
                        Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                        PackedColor = NeonLetterNetworkProtocol.Pack(
                            NeonRgba.ProjectCyan)
                    })
                .ToList()
        };
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
