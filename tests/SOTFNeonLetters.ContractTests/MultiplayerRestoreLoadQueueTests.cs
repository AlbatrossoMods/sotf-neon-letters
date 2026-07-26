using SOTFNeonLetters;
using Xunit;

public sealed class MultiplayerRestoreLoadQueueTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

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

        Assert.True(queue.TryDequeue(
            out NeonLetterMultiplayerRestoreSnapshot load));
        coordinator.StageSnapshot(load);

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

        Assert.True(queue.TryDequeue(
            out NeonLetterMultiplayerRestoreSnapshot load));

        Assert.Equal(
            (2, false),
            (
                load.Entries.Single().RestoreEntry.NativeSaveId,
                queue.HasPending));
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
        ulong latestGeneration = queue.SuspendAndClear();
        queue.Resume(latestGeneration);

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

    [Fact]
    public void CallerMutationAfterEnqueueCannotChangeQueuedSnapshot()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        NeonLetterMultiplayerSaveEnvelope envelope =
            CreateEnvelope(nativeSaveId: 7);
        NeonLetterMultiplayerSaveEntry originalEntry =
            envelope.Entries.Single();
        int originalRecipeId = originalEntry.RecipeId;
        uint originalColor = originalEntry.PackedColor;

        queue.Enqueue(envelope);
        originalEntry.RecipeId = -1;
        originalEntry.NativeSaveId = 99;
        originalEntry.Position.X = 99f;
        originalEntry.PackedColor = 0;
        envelope.Entries.Clear();

        Assert.True(queue.TryDequeue(
            out NeonLetterMultiplayerRestoreSnapshot snapshot));
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>();
        coordinator.StageSnapshot(snapshot);
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        (int RecipeId, int NativeSaveId, float PositionX, uint Color)
            observed = default;
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (entry, _, _) =>
            {
                observed = (
                    entry.RecipeId,
                    entry.NativeSaveId,
                    entry.Position.X,
                    entry.PackedColor);
                return new NeonLetterMultiplayerRestoreObservation<
                    RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (originalRecipeId, 7, 0f, originalColor),
            observed);
    }

    [Fact]
    public void QueuedSnapshotIsSanitizedAndEnumeratedExactlyOnce()
    {
        const int entryCount = 512;
        int sanitizationCount = 0;
        int enumerationCount = 0;
        var queue = new NeonLetterMultiplayerRestoreLoadQueue(
            envelope =>
            {
                sanitizationCount++;
                return NeonLetterMultiplayerRestoreSnapshot.Sanitize(
                    envelope,
                    () => enumerationCount++);
            });
        NeonLetterMultiplayerSaveEnvelope envelope =
            CreateLargeEnvelope(entryCount);

        queue.Enqueue(envelope);
        Assert.True(queue.TryDequeue(
            out NeonLetterMultiplayerRestoreSnapshot snapshot));
        NeonLetterMultiplayerSaveEntry firstOwnedEntry =
            snapshot.Entries[0].RestoreEntry;
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>();
        coordinator.StageSnapshot(snapshot);
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        NeonLetterMultiplayerSaveEntry? observedEntry = null;
        coordinator.Advance(
            nowSeconds: 0d,
            maxItems: 1,
            maxFallbackSpawns: 0,
            observe: (entry, _, _) =>
            {
                observedEntry = entry;
                return new NeonLetterMultiplayerRestoreObservation<
                    RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetUnavailable);
            },
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (1, entryCount, entryCount, true),
            (
                sanitizationCount,
                enumerationCount,
                coordinator.PendingCount,
                ReferenceEquals(firstOwnedEntry, observedEntry)));
    }

    [Fact]
    public async Task ConcurrentLoadsPublishOnlyTheHighestSequenceAsync()
    {
        using var firstSanitizationBarrier = new Barrier(2);
        using var releaseFirstSanitization = new ManualResetEventSlim();
        var queue = new NeonLetterMultiplayerRestoreLoadQueue(
            envelope =>
            {
                if (envelope!.Entries.Single().NativeSaveId == 1)
                {
                    Assert.True(
                        firstSanitizationBarrier.SignalAndWait(TestTimeout));
                    Assert.True(
                        releaseFirstSanitization.Wait(TestTimeout));
                }

                return NeonLetterMultiplayerRestoreSnapshot.Sanitize(envelope);
            });
        ulong firstSequence = 0;
        bool firstAccepted = false;
        Task firstLoad = Task.Run(
            () => firstAccepted = queue.Enqueue(
                CreateEnvelope(nativeSaveId: 1),
                out firstSequence));
        Assert.True(
            firstSanitizationBarrier.SignalAndWait(TestTimeout));

        bool secondAccepted;
        ulong secondSequence;
        int latestNativeSaveId;
        try
        {
            secondAccepted = queue.Enqueue(
                CreateEnvelope(nativeSaveId: 2),
                out secondSequence);
            Assert.True(queue.TryDequeue(
                out NeonLetterMultiplayerRestoreSnapshot latest));
            latestNativeSaveId =
                latest.Entries.Single().RestoreEntry.NativeSaveId;
        }
        finally
        {
            releaseFirstSanitization.Set();
        }

        await firstLoad.WaitAsync(TestTimeout);

        Assert.Equal(
            (true, true, true, 2, false),
            (
                firstAccepted,
                secondAccepted,
                secondSequence > firstSequence,
                latestNativeSaveId,
                queue.TryDequeue(out _)));
    }

    [Fact]
    public async Task SuspendRejectsAnInFlightLoadAcrossResumeAsync()
    {
        using var sanitizationBarrier = new Barrier(2);
        using var releaseSanitization = new ManualResetEventSlim();
        var queue = new NeonLetterMultiplayerRestoreLoadQueue(
            envelope =>
            {
                Assert.True(
                    sanitizationBarrier.SignalAndWait(TestTimeout));
                Assert.True(releaseSanitization.Wait(TestTimeout));
                return NeonLetterMultiplayerRestoreSnapshot.Sanitize(envelope);
            });
        bool accepted = true;
        Task racingLoad = Task.Run(
            () => accepted = queue.Enqueue(
                CreateEnvelope(nativeSaveId: 1)));
        Assert.True(sanitizationBarrier.SignalAndWait(TestTimeout));

        try
        {
            ulong suspensionGeneration = queue.SuspendAndClear();
            queue.Resume(suspensionGeneration);
        }
        finally
        {
            releaseSanitization.Set();
        }

        await racingLoad.WaitAsync(TestTimeout);

        Assert.Equal(
            (false, false),
            (accepted, queue.TryDequeue(out _)));
    }

    [Fact]
    public void OnlyLatestSuspensionCanResumeQueue()
    {
        var queue = new NeonLetterMultiplayerRestoreLoadQueue();
        ulong firstGeneration = queue.SuspendAndClear();
        ulong secondGeneration = queue.SuspendAndClear();

        bool staleResume = queue.Resume(firstGeneration);
        bool latestResume = queue.Resume(secondGeneration);
        bool duplicateResume = queue.Resume(secondGeneration);
        bool loadAccepted = queue.Enqueue(CreateEnvelope(nativeSaveId: 1));

        Assert.Equal(
            (false, true, false, true, true, true),
            (
                staleResume,
                latestResume,
                duplicateResume,
                loadAccepted,
                firstGeneration > 0,
                secondGeneration > firstGeneration));
    }

    [Fact]
    public void ExhaustedSuspensionGenerationKeepsQueueClosed()
    {
        var generations =
            new NeonLetterMonotonicSequence(ulong.MaxValue - 1);
        var queue =
            new NeonLetterMultiplayerRestoreLoadQueue(generations);
        ulong finalGeneration = queue.SuspendAndClear();
        Assert.True(queue.Resume(finalGeneration));

        Assert.Throws<InvalidOperationException>(
            () => queue.SuspendAndClear());
        bool staleResume = queue.Resume(finalGeneration);
        bool loadAccepted = queue.Enqueue(CreateEnvelope(nativeSaveId: 1));

        Assert.Equal(
            (ulong.MaxValue, false, false),
            (finalGeneration, staleResume, loadAccepted));
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

    private static NeonLetterMultiplayerSaveEnvelope CreateLargeEnvelope(
        int entryCount)
    {
        return new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = Enumerable.Range(1, entryCount)
                .Select(index => CreateEnvelope(index).Entries.Single())
                .ToList()
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
