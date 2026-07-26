using SOTFNeonLetters;
using Xunit;

public sealed class SnapshotMutationBehaviorTests
{
    [Fact]
    public void RepeatedBeginInvalidatesThePreviouslyAcceptedBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        bool firstBeginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);

        bool repeatedBeginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);
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
            _ => { },
            () => { });

        Assert.Equal(
            (true, false, false, false),
            (
                firstBeginAccepted,
                repeatedBeginAccepted,
                entryAccepted,
                completeAccepted));
    }

    [Fact]
    public void RepeatedBeginInvalidatesAnEmptyBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        bool firstBeginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0);

        bool repeatedBeginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0);
        bool completeAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0,
            _ => { },
            () => { });

        Assert.Equal(
            (true, false, false),
            (
                firstBeginAccepted,
                repeatedBeginAccepted,
                completeAccepted));
    }

    [Fact]
    public void ProtocolMaximumDeclaredCountIsAccepted()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();

        bool beginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            NeonLetterSnapshotProtocol.MaxSnapshotEntries);

        Assert.True(beginAccepted);
    }

    [Fact]
    public void UnsupportedEntryVersionInvalidatesTheCurrentBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1);

        bool unsupportedEntryAccepted = coordinator.TryAcceptEntry(
            version: 0,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        bool laterValidEntryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            index: 0,
            identity: 10,
            Pack(0.1f));

        Assert.Equal(
            (false, false),
            (unsupportedEntryAccepted, laterValidEntryAccepted));
    }

    [Fact]
    public void MismatchedCompleteInvalidatesAlreadyReceivedEntries()
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

        bool mismatchedCompleteAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0,
            _ => { },
            () => { });
        bool correctedCompleteAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 1,
            _ => { },
            () => { });

        Assert.Equal(
            (false, false),
            (mismatchedCompleteAccepted, correctedCompleteAccepted));
    }

    [Fact]
    public void StaleMalformedFrameDoesNotInvalidateTheCurrentBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong staleRequestId = coordinator.StartRequest();
        ulong currentRequestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            currentRequestId,
            count: 0);

        coordinator.RejectMalformedFrame(staleRequestId);
        int completionCount = 0;
        bool completeAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            currentRequestId,
            count: 0,
            _ => { },
            () => completionCount++);

        Assert.Equal((true, 1), (completeAccepted, completionCount));
    }

    [Fact]
    public void CurrentMalformedFramePreventsSnapshotPublication()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0);

        coordinator.RejectMalformedFrame(requestId);
        int callbackCount = 0;
        bool completeAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0,
            _ => callbackCount++,
            () => callbackCount++);

        Assert.Equal((false, 0), (completeAccepted, callbackCount));
    }

    [Fact]
    public void CompletedSnapshotDoesNotLeakEntriesOrLiveWatermarks()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong firstRequestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            firstRequestId,
            count: 1);
        coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            firstRequestId,
            index: 0,
            identity: 10,
            Pack(0.1f));
        coordinator.RecordLiveColor(identity: 10);
        coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            firstRequestId,
            count: 1,
            _ => { },
            () => { });

        ulong secondRequestId = coordinator.StartRequest();
        bool beginAccepted = coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            secondRequestId,
            count: 1);
        bool entryAccepted = coordinator.TryAcceptEntry(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            secondRequestId,
            index: 0,
            identity: 10,
            Pack(0.2f));
        IReadOnlyList<NeonLetterSnapshotEntry>? published = null;
        bool completeAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            secondRequestId,
            count: 1,
            entries => published = entries,
            () => { });

        Assert.Equal(
            (true, true, true, 1, 10ul),
            (
                beginAccepted,
                entryAccepted,
                completeAccepted,
                published?.Count ?? -1,
                published?[0].Identity ?? 0ul));
    }

    [Fact]
    public void LiveObservationBeyondProtocolCapacityInvalidatesTheBatch()
    {
        var coordinator = new NeonLetterSnapshotBatchCoordinator();
        ulong requestId = coordinator.StartRequest();
        coordinator.TryBegin(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0);
        for (ulong identity = 1;
             identity <=
                 (ulong)NeonLetterSnapshotProtocol.MaxSnapshotEntries + 1;
             identity++)
        {
            coordinator.RecordLiveColor(identity);
        }

        int callbackCount = 0;
        bool completeAccepted = coordinator.TryComplete(
            NeonLetterSnapshotProtocol.ProtocolVersion,
            requestId,
            count: 0,
            _ => callbackCount++,
            () => callbackCount++);

        Assert.Equal((false, 0), (completeAccepted, callbackCount));
    }

    [Fact]
    public void RestagingDuringSendRestartsTheReplacementAtBegin()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "connection",
            requestId: 1,
            static () => CreateEntries(count: 1));
        var sentFrames = new List<string>();

        int attemptedCount = coordinator.Advance(
            maxFrames: 2,
            (_, frame) =>
            {
                sentFrames.Add($"{frame.RequestId}:{frame.Kind}");
                if (frame.RequestId == 1)
                {
                    coordinator.Stage(
                        "connection",
                        requestId: 2,
                        static () => CreateEntries(count: 1));
                }

                return true;
            });

        Assert.Equal(
            (2, "1:Begin,2:Begin", 1),
            (
                attemptedCount,
                string.Join(",", sentFrames),
                coordinator.PendingJobCount));
    }

    [Fact]
    public void RestagingOneConnectionDoesNotSkipTheNextConnection()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "first",
            requestId: 1,
            static () => CreateEntries(count: 1));
        coordinator.Stage(
            "second",
            requestId: 2,
            static () => CreateEntries(count: 1));
        coordinator.Advance(maxFrames: 1, static (_, _) => true);
        coordinator.Stage(
            "first",
            requestId: 3,
            static () => CreateEntries(count: 1));
        string? nextConnection = null;

        coordinator.Advance(
            maxFrames: 1,
            (connection, _) =>
            {
                nextConnection = connection;
                return true;
            });

        Assert.Equal("second", nextConnection);
    }

    [Fact]
    public void NewConnectionDoesNotJumpAheadOfAnAlreadyStagedConnection()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "first",
            requestId: 1,
            static () => CreateEntries(count: 1));
        coordinator.Stage(
            "second",
            requestId: 2,
            static () => CreateEntries(count: 1));
        string? firstConnection = null;

        coordinator.Advance(
            maxFrames: 1,
            (connection, _) =>
            {
                firstConnection = connection;
                return true;
            });

        Assert.Equal("first", firstConnection);
    }

    [Fact]
    public void NullSnapshotProviderResultRemovesJobAndReportsFreezeFailure()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "connection",
            requestId: 1,
            static () => null!);
        Exception? reportedException = null;

        int attemptedCount = coordinator.Advance(
            maxFrames: 1,
            static (_, _) => true,
            (_, _, exception) => reportedException = exception);

        Assert.Equal(
            (0, typeof(InvalidOperationException), 0),
            (
                attemptedCount,
                reportedException?.GetType(),
                coordinator.PendingJobCount));
    }

    [Theory]
    [InlineData(0, "connection")]
    [InlineData(1, "snapshotProvider")]
    [InlineData(2, "requestId")]
    public void SnapshotSendStageRejectsInvalidRequiredInput(
        int invalidInput,
        string expectedParameterName)
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        string connection = invalidInput == 0 ? null! : "connection";
        Func<NeonLetterSnapshotEntry[]> snapshotProvider =
            invalidInput == 1
                ? null!
                : static () => Array.Empty<NeonLetterSnapshotEntry>();
        ulong requestId = invalidInput == 2 ? 0ul : 1ul;

        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(
            () => coordinator.Stage(
                connection,
                requestId,
                snapshotProvider));

        Assert.Equal(expectedParameterName, exception.ParamName);
    }

    [Fact]
    public void SnapshotSendFramesExposeProtocolIndexes()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "connection",
            requestId: 1,
            static () => CreateEntries(count: 1));
        var frames = new List<NeonLetterSnapshotSendFrame>();

        coordinator.Advance(
            maxFrames: 3,
            (_, frame) =>
            {
                frames.Add(frame);
                return true;
            });

        Assert.Equal(
            "Begin:-1,Entry:0,Complete:-1",
            string.Join(
                ",",
                frames.Select(frame => $"{frame.Kind}:{frame.Index}")));
    }

    [Fact]
    public void ProtocolMaximumSnapshotCanStartSending()
    {
        var coordinator =
            new NeonLetterSnapshotSendCoordinator<string>();
        coordinator.Stage(
            "connection",
            requestId: 1,
            static () =>
                new NeonLetterSnapshotEntry[
                    NeonLetterSnapshotProtocol.MaxSnapshotEntries]);
        NeonLetterSnapshotSendFrame sentFrame = default;
        Exception? freezeError = null;

        int attemptedCount = coordinator.Advance(
            maxFrames: 1,
            (_, frame) =>
            {
                sentFrame = frame;
                return true;
            },
            (_, _, exception) => freezeError = exception);

        Assert.Equal(
            (
                1,
                NeonLetterSnapshotSendFrameKind.Begin,
                NeonLetterSnapshotProtocol.MaxSnapshotEntries,
                null),
            (
                attemptedCount,
                sentFrame.Kind,
                sentFrame.Count,
                freezeError));
    }

    private static uint Pack(float red)
    {
        return NeonLetterNetworkProtocol.Pack(
            new NeonRgba(red, 0.2f, 0.3f, 1f));
    }

    private static NeonLetterSnapshotEntry[] CreateEntries(int count)
    {
        return Enumerable.Range(1, count)
            .Select(
                identity => new NeonLetterSnapshotEntry(
                    (ulong)identity,
                    NeonRgba.ProjectCyan))
            .ToArray();
    }
}
