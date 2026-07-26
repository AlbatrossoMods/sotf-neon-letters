using SOTFNeonLetters;
using Xunit;

public sealed class SnapshotBatchProtocolTests
{
    [Fact]
    public void NewRequestUsesANonzeroMonotonicIdAndMakesOlderFramesStale()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong firstRequestId = coordinator.StartRequest();
        bool firstBeginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            firstRequestId,
            count: 1);

        ulong secondRequestId = coordinator.StartRequest();
        bool staleEntryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            firstRequestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        int publishCount = 0;
        bool staleCompleteAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            firstRequestId,
            count: 1,
            _ => publishCount++,
            () => { });

        Assert.Equal(
            (1ul, 2ul, true, false, false, 0),
            (
                firstRequestId,
                secondRequestId,
                firstBeginAccepted,
                staleEntryAccepted,
                staleCompleteAccepted,
                publishCount));
    }

    [Fact]
    public void EmptyBatchCompletesAndPublishesOneEmptySnapshot()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        var publishedCounts = new List<int>();
        int completionCount = 0;

        bool beginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0);
        bool completeAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0,
            entries => publishedCounts.Add(entries.Count),
            () => completionCount++);

        Assert.Equal(
            (true, true, "0", 1),
            (
                beginAccepted,
                completeAccepted,
                string.Join(",", publishedCounts),
                completionCount));
    }

    [Fact]
    public void EntryAndCompleteBeforeBeginDoNotPublish()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        int publishCount = 0;

        bool entryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        bool completeAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            _ => publishCount++,
            () => { });

        Assert.Equal((false, false, 0), (entryAccepted, completeAccepted, publishCount));
    }

    [Fact]
    public void WrongRequestFramesAreIgnoredWithoutDamagingTheCurrentBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);

        bool wrongEntryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId + 1,
            index: 0,
            identity: 99,
            Pack(0.9f));
        bool currentEntryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        IReadOnlyList<NeonLetterSnapshotEntry>? published = null;
        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            entries => published = entries,
            () => { });

        Assert.Equal(
            (false, true, true, 10ul),
            (
                wrongEntryAccepted,
                currentEntryAccepted,
                completed,
                published?.Single().Identity ?? 0ul));
    }

    [Fact]
    public void IncompleteBatchDoesNotPublishOrComplete()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        int callbackCount = 0;

        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2,
            _ => callbackCount++,
            () => callbackCount++);

        Assert.Equal((false, 0), (completed, callbackCount));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(NeonLetterSnapshotProtocol.MaxSnapshotEntries + 1)]
    public void InvalidDeclaredCountIsRejectedWithoutAllocatingABatch(int count)
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();

        bool accepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count);

        Assert.False(accepted);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void OutOfRangeIndexInvalidatesTheBatch(int index)
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2);
        int publishCount = 0;

        bool entryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index,
            identity: 10,
            Pack(0.1f));
        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2,
            _ => publishCount++,
            () => { });

        Assert.Equal((false, false, 0), (entryAccepted, completed, publishCount));
    }

    [Fact]
    public void DuplicateIndexInvalidatesTheBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);

        bool firstAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        bool duplicateAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 11,
            Pack(0.2f));
        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            _ => { },
            () => { });

        Assert.Equal((true, false, false), (firstAccepted, duplicateAccepted, completed));
    }

    [Fact]
    public void DuplicateIdentityAtDifferentIndexesInvalidatesTheBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2);

        bool firstAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            packedColor: 0xFF000011u);
        bool duplicateIdentityAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 1,
            identity: 10,
            packedColor: 0xFF000022u);
        int publishCount = 0;
        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2,
            _ => publishCount++,
            () => { });

        Assert.Equal(
            (true, false, false, 0),
            (
                firstAccepted,
                duplicateIdentityAccepted,
                completed,
                publishCount));
    }

    [Fact]
    public void CompleteCountMismatchInvalidatesTheBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));

        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0,
            _ => { },
            () => { });

        Assert.False(completed);
    }

    [Fact]
    public void OutOfOrderUniqueEntriesPublishOnceInDeclaredIndexOrder()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 3);
        var publishedIdentitySequences = new List<string>();

        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 2,
            identity: 30,
            Pack(0.3f));
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        bool invisibleBeforeComplete = publishedIdentitySequences.Count == 0;
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 1,
            identity: 20,
            Pack(0.2f));
        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 3,
            entries => publishedIdentitySequences.Add(
                string.Join(",", entries.Select(entry => entry.Identity))),
            () => { });

        Assert.Equal(
            (true, true, "10,20,30"),
            (
                invisibleBeforeComplete,
                completed,
                publishedIdentitySequences.Single()));
    }

    [Fact]
    public void LiveColorAfterBeginWinsOverTheMatchingSnapshotEntry()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        coordinator.RecordLiveColor(identity: 10);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 1,
            identity: 20,
            Pack(0.2f));
        IReadOnlyList<NeonLetterSnapshotEntry>? published = null;

        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2,
            entries => published = entries,
            () => { });

        Assert.Equal(
            (true, "20"),
            (
                completed,
                string.Join(",", published!.Select(entry => entry.Identity))));
    }

    [Fact]
    public void LiveColorBeforeBeginDoesNotHideTheRequestedSnapshotEntry()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.RecordLiveColor(identity: 10);
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        IReadOnlyList<NeonLetterSnapshotEntry>? published = null;

        coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            entries => published = entries,
            () => { });

        Assert.Equal(10ul, published!.Single().Identity);
    }

    [Fact]
    public void LiteralPackedColorPublishesItsExactRgbaComponents()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            packedColor: 0x44332211u);
        NeonRgba publishedColor = default;

        coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            entries => publishedColor = entries.Single().Color,
            () => { });

        Assert.Equal(
            new NeonRgba(
                17f / 255f,
                34f / 255f,
                51f / 255f,
                68f / 255f),
            publishedColor);
    }

    [Fact]
    public void UnsupportedOrMalformedCurrentFrameCannotCompleteTheBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);

        bool unsupportedAccepted = coordinator.TryAcceptEntry(
            version: byte.MaxValue,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        bool completedAfterUnsupported = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            _ => { },
            () => { });

        requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        coordinator.RejectMalformedFrame(requestId);
        bool completedAfterMalformed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            _ => { },
            () => { });

        Assert.Equal(
            (false, false, false),
            (
                unsupportedAccepted,
                completedAfterUnsupported,
                completedAfterMalformed));
    }

    [Fact]
    public void SchedulerCompletesOnlyAfterAnExactBatchIsPublished()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        var scheduler = new NeonLetterSnapshotRequestScheduler();
        scheduler.RecordSuccessfulSend(nowSeconds: 0d);
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        bool incompleteAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            _ => { },
            scheduler.Complete);
        bool retryStillArmed = scheduler.CanAttempt;

        requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        var callbackOrder = new List<string>();
        bool completeAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            _ => callbackOrder.Add("publish"),
            () =>
            {
                callbackOrder.Add("complete");
                scheduler.Complete();
            });

        Assert.Equal(
            (false, true, true, "publish,complete", false),
            (
                incompleteAccepted,
                retryStillArmed,
                completeAccepted,
                string.Join(",", callbackOrder),
                scheduler.CanAttempt));
    }

    [Fact]
    public void PublishFailureLeavesSnapshotRetryArmed()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        var scheduler = new NeonLetterSnapshotRequestScheduler();
        scheduler.RecordSuccessfulSend(nowSeconds: 0d);
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        var publishFailure = new InvalidOperationException("publish failed");
        int completionCount = 0;

        Exception? propagated = Record.Exception(
            () => coordinator.TryComplete(
                NeonLetterSnapshotProtocol.ProtocolVersion,
                requestId,
                count: 1,
                _ => throw publishFailure,
                () =>
                {
                    completionCount++;
                    scheduler.Complete();
                }));

        Assert.Equal(
            (true, 0, true),
            (
                ReferenceEquals(publishFailure, propagated),
                completionCount,
                scheduler.CanAttempt));
    }

    [Fact]
    public void StorageCapacityFailureLeavesSnapshotRetryArmed()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        var scheduler = new NeonLetterSnapshotRequestScheduler();
        var replicatedState = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity: 1,
            pendingLifetimeSeconds: 15d);
        replicatedState.Receive(
            identity: 1,
            new NeonRgba(0.1f, 0.2f, 0.3f, 1f),
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });
        scheduler.RecordSuccessfulSend(nowSeconds: 0d);
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            packedColor: 0xFF000011u);

        Exception? propagated = Record.Exception(
            () => coordinator.TryComplete(
                NeonLetterSnapshotProtocol.ProtocolVersion,
                requestId,
                count: 1,
                entries =>
                    replicatedState.ReceiveBatch(
                        entries,
                        nowSeconds: 1d,
                        static entry => entry.Identity,
                        static entry => entry.Color,
                        isReady: _ => false,
                        apply: (_, _) => { }),
                scheduler.Complete));

        Assert.Equal(
            (typeof(InvalidOperationException), 1, true),
            (
                propagated?.GetType(),
                replicatedState.PendingCount,
                scheduler.CanAttempt));
    }

    [Fact]
    public void CompleteBatchLargerThanLegacyPendingCapacityIsStoredBeforeCompletion()
    {
        const int entryCount = 256;
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        var scheduler = new NeonLetterSnapshotRequestScheduler();
        var replicatedState = new NeonLetterReplicatedColorState<ulong>(
            NeonLetterSnapshotProtocol.MaxSnapshotEntries,
            pendingLifetimeSeconds: 15d);
        scheduler.RecordSuccessfulSend(nowSeconds: 0d);
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            entryCount);
        for (int index = 0; index < entryCount; index++)
        {
            coordinator.TryAcceptEntry(
                NeonLetterSnapshotProtocol.ProtocolVersion,
                requestId,
                index,
                identity: (ulong)index + 1,
                packedColor: 0xFF336699u);
        }

        int pendingCountAtCompletion = -1;
        int resolverCallCount = 0;
        bool completed = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            entryCount,
            entries => replicatedState.ReceiveBatch(
                entries,
                nowSeconds: 1d,
                static entry => entry.Identity,
                static entry => entry.Color,
                isReady: _ =>
                {
                    resolverCallCount++;
                    return false;
                },
                apply: (_, _) => { }),
            () =>
            {
                pendingCountAtCompletion = replicatedState.PendingCount;
                scheduler.Complete();
            });

        Assert.Equal(
            (true, entryCount, entryCount, true, false),
            (
                completed,
                replicatedState.PendingCount,
                pendingCountAtCompletion,
                resolverCallCount >= entryCount &&
                    resolverCallCount <= entryCount * 2,
                scheduler.CanAttempt));
    }

    [Fact]
    public void SnapshotSendJobRespectsTheGlobalFrameBudgetAcrossTicks()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        NeonLetterSnapshotEntry[] snapshot = CreateSnapshotEntries(count: 256);
        coordinator.Stage("connection", requestId: 7, () => snapshot);
        var sentFrames = new List<NeonLetterSnapshotSendFrame>();

        int firstTickCount = coordinator.Advance(
            NeonLetterSnapshotProtocol.MaxSendFramesPerUpdate,
            (_, frame) =>
            {
                sentFrames.Add(frame);
                return true;
            });
        bool completedOnFirstTick = sentFrames.Any(
            frame => frame.Kind == NeonLetterSnapshotSendFrameKind.Complete);
        int secondTickCount = coordinator.Advance(
            NeonLetterSnapshotProtocol.MaxSendFramesPerUpdate,
            (_, frame) =>
            {
                sentFrames.Add(frame);
                return true;
            });

        Assert.Equal(
            (256, false, 2, true, 0),
            (
                firstTickCount,
                completedOnFirstTick,
                secondTickCount,
                sentFrames.Last().Kind ==
                    NeonLetterSnapshotSendFrameKind.Complete,
                coordinator.PendingJobCount));
    }

    [Fact]
    public void SnapshotFreezesWhenFirstAdvanceEmitsBeginAndLaterLiveColorWins()
    {
        var sender = new NeonLetterSnapshotSendCoordinator<string>();
        var receiver = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = receiver.StartRequest();
        var authoritativeColor = new NeonRgba(0.1f, 0.2f, 0.3f, 1f);
        int freezeCount = 0;
        sender.Stage(
            "connection",
            requestId,
            () =>
            {
                freezeCount++;
                return new[]
                {
                    new NeonLetterSnapshotEntry(
                        Identity: 10,
                        authoritativeColor)
                };
            });

        authoritativeColor = new NeonRgba(0.4f, 0.5f, 0.6f, 1f);
        NeonLetterSnapshotSendFrame beginFrame = default;
        sender.Advance(
            maxFrames: 1,
            (_, frame) =>
            {
                beginFrame = frame;
                return receiver.TryBegin(
                    NeonLetterSnapshotProtocol.ProtocolVersion,
                    frame.RequestId,
                    frame.Count);
            });

        authoritativeColor = new NeonRgba(0.7f, 0.8f, 0.9f, 1f);
        receiver.RecordLiveColor(identity: 10);
        NeonRgba clientColor = authoritativeColor;
        int publishedCount = -1;
        NeonLetterSnapshotSendFrame entryFrame = default;
        sender.Advance(
            maxFrames: 1,
            (_, frame) =>
            {
                entryFrame = frame;
                return receiver.TryAcceptEntry(
                    NeonLetterSnapshotProtocol.ProtocolVersion,
                    frame.RequestId,
                    frame.Index,
                    frame.Entry.Identity,
                    NeonLetterNetworkProtocol.Pack(frame.Entry.Color));
            });
        sender.Advance(
            maxFrames: 1,
            (_, frame) => receiver.TryComplete(
                NeonLetterSnapshotProtocol.ProtocolVersion,
                frame.RequestId,
                frame.Count,
                entries =>
                {
                    publishedCount = entries.Count;
                    clientColor = entries.Aggregate(
                        clientColor,
                        static (_, entry) => entry.Color);
                },
                () => { }));

        Assert.Equal(
            (
                NeonLetterSnapshotSendFrameKind.Begin,
                1,
                new NeonRgba(0.4f, 0.5f, 0.6f, 1f),
                new NeonRgba(0.7f, 0.8f, 0.9f, 1f),
                0,
                0),
            (
                beginFrame.Kind,
                freezeCount,
                entryFrame.Entry.Color,
                clientColor,
                publishedCount,
                sender.PendingJobCount));
    }

    [Fact]
    public void SnapshotFreezeFailureAbortsWithoutSendingAnyFrames()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        var freezeFailure = new InvalidOperationException("freeze failed");
        coordinator.Stage(
            "connection",
            requestId: 7,
            () => throw freezeFailure);
        int sendCount = 0;
        Exception? reportedFailure = null;

        int attemptedCount = coordinator.Advance(
            NeonLetterSnapshotProtocol.MaxSendFramesPerUpdate,
            (_, _) =>
            {
                sendCount++;
                return true;
            },
            (_, _, exception) => reportedFailure = exception);

        Assert.Equal(
            (0, 0, true, 0),
            (
                attemptedCount,
                sendCount,
                ReferenceEquals(freezeFailure, reportedFailure),
                coordinator.PendingJobCount));
    }

    [Fact]
    public void EmptySnapshotSendJobEmitsBeginThenComplete()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "connection",
            requestId: 7,
            static () => Array.Empty<NeonLetterSnapshotEntry>());
        var sentKinds = new List<NeonLetterSnapshotSendFrameKind>();

        int sentCount = coordinator.Advance(
            NeonLetterSnapshotProtocol.MaxSendFramesPerUpdate,
            (_, frame) =>
            {
                sentKinds.Add(frame.Kind);
                return true;
            });

        Assert.Equal(
            (
                2,
                "Begin,Complete",
                0),
            (
                sentCount,
                string.Join(",", sentKinds),
                coordinator.PendingJobCount));
    }

    [Fact]
    public void NewRequestReplacesTheConnectionsPartiallySentJob()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "connection",
            requestId: 7,
            static () => CreateSnapshotEntries(count: 2));
        var sentFrames = new List<NeonLetterSnapshotSendFrame>();
        coordinator.Advance(
            maxFrames: 1,
            (_, frame) =>
            {
                sentFrames.Add(frame);
                return true;
            });

        coordinator.Stage(
            "connection",
            requestId: 8,
            static () => CreateSnapshotEntries(count: 1));
        coordinator.Advance(
            NeonLetterSnapshotProtocol.MaxSendFramesPerUpdate,
            (_, frame) =>
            {
                sentFrames.Add(frame);
                return true;
            });

        Assert.Equal(
            ("7:Begin,8:Begin,8:Entry,8:Complete", 0),
            (
                string.Join(
                    ",",
                    sentFrames.Select(
                        frame => $"{frame.RequestId}:{frame.Kind}")),
                coordinator.PendingJobCount));
    }

    [Fact]
    public void SendFailureAbortsTheJobWithoutSendingComplete()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "connection",
            requestId: 7,
            static () => CreateSnapshotEntries(count: 2));
        var attemptedKinds = new List<NeonLetterSnapshotSendFrameKind>();

        int attemptedCount = coordinator.Advance(
            NeonLetterSnapshotProtocol.MaxSendFramesPerUpdate,
            (_, frame) =>
            {
                attemptedKinds.Add(frame.Kind);
                return frame.Kind ==
                    NeonLetterSnapshotSendFrameKind.Begin;
            });

        Assert.Equal(
            (2, "Begin,Entry", false, 0),
            (
                attemptedCount,
                string.Join(",", attemptedKinds),
                attemptedKinds.Contains(
                    NeonLetterSnapshotSendFrameKind.Complete),
                coordinator.PendingJobCount));
    }

    [Fact]
    public void ClearingSnapshotSendJobsPreventsFurtherFrames()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "connection",
            requestId: 7,
            static () => CreateSnapshotEntries(count: 2));
        int callbackCount = 0;

        coordinator.Clear();
        int attemptedCount = coordinator.Advance(
            NeonLetterSnapshotProtocol.MaxSendFramesPerUpdate,
            (_, _) =>
            {
                callbackCount++;
                return true;
            });

        Assert.Equal(
            (0, 0, 0),
            (
                attemptedCount,
                callbackCount,
                coordinator.PendingJobCount));
    }

    [Fact]
    public void AcceptedBeginAndEntryProgressExtendRetryWithoutCompleting()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        var scheduler = new NeonLetterSnapshotRequestScheduler();
        scheduler.RecordSuccessfulSend(nowSeconds: 0d);
        ulong requestId = coordinator.StartRequest();

        bool beginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 2);
        scheduler.DeferRetryForProgress(nowSeconds: 0.5d);
        bool entryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            packedColor: 0xFF000011u);
        scheduler.DeferRetryForProgress(nowSeconds: 1d);

        Assert.Equal(
            (true, true, false, true, true),
            (
                beginAccepted,
                entryAccepted,
                scheduler.IsDue(nowSeconds: 2.999d),
                scheduler.IsDue(nowSeconds: 3d),
                scheduler.CanAttempt));
    }

    [Fact]
    public void WrongOrMalformedProgressDoesNotExtendRetry()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        var scheduler = new NeonLetterSnapshotRequestScheduler();
        scheduler.RecordSuccessfulSend(nowSeconds: 0d);
        ulong requestId = coordinator.StartRequest();

        bool wrongBeginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId + 1,
            count: 1);
        bool validBeginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
        scheduler.DeferRetryForProgress(nowSeconds: 0d);
        bool malformedEntryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 0,
            packedColor: 0xFF000011u);

        Assert.Equal(
            (false, true, false, true),
            (
                wrongBeginAccepted,
                validBeginAccepted,
                malformedEntryAccepted,
                scheduler.IsDue(nowSeconds: 2d)));
    }

    [Fact]
    public void OldRequestProgressCannotExtendANewerRequestsRetryDeadline()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        var scheduler = new NeonLetterSnapshotRequestScheduler();
        scheduler.RecordSuccessfulSend(nowSeconds: 0d);
        ulong oldRequestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            oldRequestId,
            count: 1);

        ulong currentRequestId = coordinator.StartRequest();
        scheduler.RecordSuccessfulSend(nowSeconds: 2d);
        bool staleEntryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            oldRequestId,
            index: 0,
            identity: 10,
            packedColor: 0xFF000011u);

        Assert.Equal(
            (false, 2ul, true),
            (
                staleEntryAccepted,
                currentRequestId,
                scheduler.IsDue(nowSeconds: 4d)));
    }

    [Fact]
    public void ResetRejectsStaleFramesWithoutReusingRequestIds()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong staleRequestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            staleRequestId,
            count: 0);

        coordinator.Reset();
        bool staleCompleteAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            staleRequestId,
            count: 0,
            _ => { },
            () => { });
        ulong nextRequestId = coordinator.StartRequest();

        Assert.Equal(
            (false, staleRequestId + 1),
            (staleCompleteAccepted, nextRequestId));
    }

    private static uint Pack(float red)
    {
        return NeonLetterNetworkProtocol.Pack(new NeonRgba(red, 1f, 1f, 1f));
    }

    private static NeonLetterSnapshotEntry[] CreateSnapshotEntries(int count)
    {
        return Enumerable.Range(1, count)
            .Select(
                identity => new NeonLetterSnapshotEntry(
                    (ulong)identity,
                    new NeonRgba(0.1f, 0.2f, 0.3f, 1f)))
            .ToArray();
    }
}
