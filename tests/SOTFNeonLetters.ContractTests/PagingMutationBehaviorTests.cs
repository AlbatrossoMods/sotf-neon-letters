using SOTFNeonLetters;
using Xunit;

public sealed class PagingMutationBehaviorTests
{
    private static readonly NeonRgba Red = new(1f, 0f, 0f, 1f);
    private static readonly NeonRgba Green = new(0f, 1f, 0f, 1f);

    [Fact]
    public void WireParserDecodesEveryEntryAndTheExactHeader()
    {
        object[] tokens =
        {
            NeonLetterColorPageProtocol.ProtocolVersion,
            17ul,
            23ul,
            9ul,
            8ul,
            2,
            (byte)0,
            101ul,
            5ul,
            Red,
            202ul,
            7ul,
            Green
        };

        NeonLetterColorPageResponse<ulong> response =
            ReadWireResponse(tokens);

        Assert.Equal(
            (
                NeonLetterColorPageProtocol.ProtocolVersion,
                17ul,
                23ul,
                9ul,
                8ul,
                false,
                "101:5,202:7",
                Red,
                Green),
            (
                response.ProtocolVersion,
                response.SyncId,
                response.Sequence,
                response.WatermarkChangeSerial,
                response.NextCursorChangeSerial,
                response.Complete,
                string.Join(
                    ",",
                    response.Entries.Select(
                        entry =>
                            $"{entry.Identity}:{entry.EntityRevision}")),
                response.Entries[0].Color,
                response.Entries[1].Color));
    }

    [Fact]
    public void WireParserAcceptsAnEmptyCompleteResponse()
    {
        object[] tokens = HeaderTokens(
            syncId: 3,
            sequence: 4,
            count: 0,
            complete: 1);

        NeonLetterColorPageResponse<ulong> response =
            ReadWireResponse(tokens);

        Assert.Equal(
            (3ul, 4ul, true, 0),
            (
                response.SyncId,
                response.Sequence,
                response.Complete,
                response.Entries.Count));
    }

    [Fact]
    public void WireParserAcceptsExactlyTheMaximumEntryCount()
    {
        var tokens = new List<object>(
            HeaderTokens(
                syncId: 1,
                sequence: 1,
                count: NeonLetterColorPageProtocol.MaxPageEntries,
                complete: 1));
        for (ulong identity = 1;
             identity <= NeonLetterColorPageProtocol.MaxPageEntries;
             identity++)
        {
            tokens.Add(identity);
            tokens.Add(identity + 10);
            tokens.Add(identity == 1 ? Red : Green);
        }

        NeonLetterColorPageResponse<ulong> response =
            ReadWireResponse(tokens.ToArray());

        Assert.Equal(
            (
                NeonLetterColorPageProtocol.MaxPageEntries,
                1ul,
                64ul,
                11ul,
                74ul),
            (
                response.Entries.Count,
                response.Entries[0].Identity,
                response.Entries[^1].Identity,
                response.Entries[0].EntityRevision,
                response.Entries[^1].EntityRevision));
    }

    [Fact]
    public void WireParserRejectsOnlyTheZeroSyncIdentifier()
    {
        object[] tokens = HeaderTokens(
            syncId: 0,
            sequence: 1,
            count: 0,
            complete: 1);

        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(tokens));
    }

    [Fact]
    public void WireParserRejectsOnlyTheZeroSequence()
    {
        object[] tokens = HeaderTokens(
            syncId: 1,
            sequence: 0,
            count: 0,
            complete: 1);

        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(tokens));
    }

    [Fact]
    public void DisabledSchedulingRejectsAnOtherwiseValidRequest()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 1));
        NeonLetterColorPageRequest request = InitialRequest(syncId: 1);

        NeonLetterColorPageScheduleResult result =
            host.TryScheduleRequest(
                "peer",
                canSchedule: false,
                request);

        Assert.Equal(
            (NeonLetterColorPageScheduleResult.Rejected, 0),
            (result, host.PendingRequestCount));
    }

    [Fact]
    public void EnabledSchedulingRejectsAnInvalidRequest()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 1));
        var request = new NeonLetterColorPageRequest(
            ProtocolVersion:
                (byte)(NeonLetterColorPageProtocol.ProtocolVersion + 1),
            SyncId: 1,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);

        NeonLetterColorPageScheduleResult result =
            host.TryScheduleRequest(
                "peer",
                canSchedule: true,
                request);

        Assert.Equal(
            (NeonLetterColorPageScheduleResult.Rejected, 0),
            (result, host.PendingRequestCount));
    }

    [Fact]
    public void NewPeerRejectsACursorOnlyBootstrapRequest()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 1));
        var request = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 1,
            CursorChangeSerial: 1,
            WatermarkChangeSerial: 0);

        bool created = host.TryCreateResponse(
            "peer",
            canSend: true,
            request,
            out _);

        Assert.Equal((false, 0), (created, host.PeerCount));
    }

    [Fact]
    public void NewPeerRejectsAWatermarkOnlyBootstrapRequest()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 1));
        var request = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 1,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 1);

        bool created = host.TryCreateResponse(
            "peer",
            canSend: true,
            request,
            out _);

        Assert.Equal((false, 0), (created, host.PeerCount));
    }

    [Fact]
    public void FixedWatermarkRequestRequiresCursorBeforeWatermark()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 1));
        var request = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 1,
            CursorChangeSerial: 1,
            WatermarkChangeSerial: 1);

        NeonLetterColorPageScheduleResult result =
            host.TryScheduleRequest(
                "peer",
                canSchedule: true,
                request);

        Assert.Equal(
            (NeonLetterColorPageScheduleResult.Rejected, 0),
            (result, host.PendingRequestCount));
    }

    [Fact]
    public void FixedWatermarkRequestCannotExceedCurrentSerial()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 1));
        var request = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 1,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 2);

        NeonLetterColorPageScheduleResult result =
            host.TryScheduleRequest(
                "peer",
                canSchedule: true,
                request);

        Assert.Equal(
            (NeonLetterColorPageScheduleResult.Rejected, 0),
            (result, host.PendingRequestCount));
    }

    [Fact]
    public void CompletePageFollowUpRequiresZeroWatermark()
    {
        NeonLetterAuthoritativeColors<ulong> colors =
            CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(colors);
        bool firstCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            InitialRequest(syncId: 7),
            out NeonLetterTargetedColorPage<string, ulong> first);
        AcceptColor(colors, identity: 2, Green);
        var cursorOnlyMatch = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 7,
            CursorChangeSerial:
                first.Response.WatermarkChangeSerial,
            WatermarkChangeSerial: colors.CurrentChangeSerial);

        bool followUpCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            cursorOnlyMatch,
            out _);

        Assert.Equal(
            (true, false),
            (firstCreated, followUpCreated));
    }

    [Fact]
    public void CompletePageFollowUpRequiresTheWatermarkCursor()
    {
        NeonLetterAuthoritativeColors<ulong> colors =
            CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                colors);
        bool firstCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            InitialRequest(syncId: 7),
            out _);
        AcceptColor(colors, identity: 2, Green);
        var watermarkOnlyMatch = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 7,
            CursorChangeSerial: 2,
            WatermarkChangeSerial: 0);

        bool followUpCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            watermarkOnlyMatch,
            out _);

        Assert.Equal(
            (true, false),
            (firstCreated, followUpCreated));
    }

    [Fact]
    public void IncompletePageFollowUpRequiresTheOriginalWatermark()
    {
        NeonLetterAuthoritativeColors<ulong> colors =
            CreateColors(count: 65);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(colors);
        bool firstCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            InitialRequest(syncId: 9),
            out NeonLetterTargetedColorPage<string, ulong> first);
        AcceptColor(colors, identity: 66, Green);
        var cursorOnlyMatch = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 9,
            CursorChangeSerial:
                first.Response.NextCursorChangeSerial,
            WatermarkChangeSerial: colors.CurrentChangeSerial);

        bool followUpCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            cursorOnlyMatch,
            out _);

        Assert.Equal(
            (true, false),
            (firstCreated, followUpCreated));
    }

    [Fact]
    public void IncompletePageFollowUpRequiresTheNextCursor()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 65));
        bool firstCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            InitialRequest(syncId: 9),
            out NeonLetterTargetedColorPage<string, ulong> first);
        var watermarkOnlyMatch = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 9,
            CursorChangeSerial:
                first.Response.NextCursorChangeSerial - 1,
            WatermarkChangeSerial:
                first.Response.WatermarkChangeSerial);

        bool followUpCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            watermarkOnlyMatch,
            out _);

        Assert.Equal(
            (true, false),
            (firstCreated, followUpCreated));
    }

    [Fact]
    public void NewerSyncCanRebootstrapAnExistingPeer()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 1));
        bool firstCreated = host.TryCreateResponse(
            "peer",
            canSend: true,
            InitialRequest(syncId: 1),
            out _);

        bool restarted = host.TryCreateResponse(
            "peer",
            canSend: true,
            InitialRequest(syncId: 2),
            out NeonLetterTargetedColorPage<string, ulong> delivery);

        Assert.Equal(
            (true, true, 2ul, 1ul, 1),
            (
                firstCreated,
                restarted,
                delivery.Response.SyncId,
                delivery.Response.Sequence,
                host.PeerCount));
    }

    [Fact]
    public void HostClearRemovesThePendingLinkedListAndPeerIndexes()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 1));
        host.TryScheduleRequest(
            "first",
            canSchedule: true,
            InitialRequest(syncId: 1));
        host.TryScheduleRequest(
            "second",
            canSchedule: true,
            InitialRequest(syncId: 2));
        host.Clear();
        int callbackCount = 0;

        int sentCount = host.DrainScheduledRequests(
            canSend: _ =>
            {
                callbackCount++;
                return true;
            },
            send: _ => callbackCount++,
            onFailure: (_, _) => callbackCount++);

        Assert.Equal(
            (0, 0, 0, 0),
            (
                sentCount,
                callbackCount,
                host.PendingRequestCount,
                host.PeerCount));
    }

    [Fact]
    public void StaleQueuedRequestIsDroppedBeforeTheSendCallback()
    {
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(
                CreateColors(count: 65));
        host.TryCreateResponse(
            "peer",
            canSend: true,
            InitialRequest(syncId: 1),
            out NeonLetterTargetedColorPage<string, ulong> first);
        var secondRequest = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 1,
            CursorChangeSerial:
                first.Response.NextCursorChangeSerial,
            WatermarkChangeSerial:
                first.Response.WatermarkChangeSerial);
        host.TryScheduleRequest(
            "peer",
            canSchedule: true,
            secondRequest);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            secondRequest,
            out NeonLetterTargetedColorPage<string, ulong> second);
        var thirdRequest = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 1,
            CursorChangeSerial:
                second.Response.WatermarkChangeSerial,
            WatermarkChangeSerial: 0);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            thirdRequest,
            out _);
        int sendCount = 0;
        int failureCount = 0;

        int sentCount = host.DrainScheduledRequests(
            canSend: _ => true,
            send: _ => sendCount++,
            onFailure: (_, _) => failureCount++);

        Assert.Equal(
            (0, 0, 0, 0),
            (
                sentCount,
                sendCount,
                failureCount,
                host.PendingRequestCount));
    }

    [Fact]
    public void StartSessionReportsItsGateAndActiveState()
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>();

        bool disabled = client.StartSession(canStart: false);
        bool started = client.StartSession(canStart: true);
        bool alreadyActive = client.StartSession(canStart: true);

        Assert.Equal(
            (false, true, true),
            (disabled, started, alreadyActive));
    }

    [Fact]
    public void DisabledClientRequestGateReturnsNoRequest()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out _);

        bool due = client.TryGetDueRequest(
            canRequest: false,
            nowSeconds: 0d,
            out _);

        Assert.False(due);
    }

    [Fact]
    public void InactiveClientReturnsNoRequest()
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>();

        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out _);

        Assert.False(due);
    }

    [Fact]
    public void ClientRetryDeadlineIsInclusive()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out _);
        client.RecordRequestAttempt(nowSeconds: 3d);

        bool early = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 4.999d,
            out _);
        bool atDeadline = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 5d,
            out _);

        Assert.Equal((false, true), (early, atDeadline));
    }

    [Fact]
    public void DisabledApplyGateCannotPublishOrAdvance()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        int publishCount = 0;

        bool accepted = client.TryAcceptResponse(
            canApply: false,
            StableEmptyResponse(request),
            nowSeconds: 0d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest stillOutstanding);

        Assert.Equal(
            (false, 0, false, true, request),
            (
                accepted,
                publishCount,
                client.IsComplete,
                due,
                stillOutstanding));
    }

    [Fact]
    public void InactiveClientCannotAcceptAResponse()
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        var response = new NeonLetterColorPageResponse<ulong>(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 0,
            Sequence: 0,
            WatermarkChangeSerial: 0,
            NextCursorChangeSerial: 0,
            Complete: true,
            Array.Empty<NeonLetterColorPageEntry<ulong>>());
        int publishCount = 0;

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            response,
            nowSeconds: 0d,
            _ =>
            {
                publishCount++;
                return true;
            });

        Assert.Equal(
            (false, 0, false),
            (accepted, publishCount, client.IsComplete));
    }

    [Fact]
    public void CompletedClientCannotAcceptAnotherResponse()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        bool completed = client.TryAcceptResponse(
            canApply: true,
            StableEmptyResponse(request),
            nowSeconds: 0d,
            _ => true);
        var later = new NeonLetterColorPageResponse<ulong>(
            NeonLetterColorPageProtocol.ProtocolVersion,
            request.SyncId,
            Sequence: 2,
            WatermarkChangeSerial: 0,
            NextCursorChangeSerial: 0,
            Complete: true,
            Array.Empty<NeonLetterColorPageEntry<ulong>>());
        int publishCount = 0;

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            later,
            nowSeconds: 0d,
            _ =>
            {
                publishCount++;
                return true;
            });

        Assert.Equal(
            (true, false, 0, true),
            (completed, accepted, publishCount, client.IsComplete));
    }

    [Fact]
    public void StaleSequenceCannotRepublishAValidCurrentPage()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        int publishCount = 0;
        var entry =
            new NeonLetterColorPageEntry<ulong>(1, 1, Red);
        bool firstAccepted = client.TryAcceptResponse(
            canApply: true,
            Response(
                request,
                sequence: 1,
                watermark: 1,
                nextCursor: 1,
                complete: true,
                entry),
            nowSeconds: 0d,
            _ =>
            {
                publishCount++;
                return true;
            });
        var staleButCurrentShape = new NeonLetterColorPageResponse<ulong>(
            NeonLetterColorPageProtocol.ProtocolVersion,
            request.SyncId,
            Sequence: 1,
            WatermarkChangeSerial: 1,
            NextCursorChangeSerial: 1,
            Complete: true,
            Array.Empty<NeonLetterColorPageEntry<ulong>>());

        bool staleAccepted = client.TryAcceptResponse(
            canApply: true,
            staleButCurrentShape,
            nowSeconds: 0d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest catchUp);

        Assert.Equal(
            (true, false, 1, false, true, 1ul, 0ul),
            (
                firstAccepted,
                staleAccepted,
                publishCount,
                client.IsComplete,
                due,
                catchUp.CursorChangeSerial,
                catchUp.WatermarkChangeSerial));
    }

    [Fact]
    public void FutureSequenceCannotCompleteTheClient()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        var future = new NeonLetterColorPageResponse<ulong>(
            NeonLetterColorPageProtocol.ProtocolVersion,
            request.SyncId,
            Sequence: 2,
            WatermarkChangeSerial: 0,
            NextCursorChangeSerial: 0,
            Complete: true,
            Array.Empty<NeonLetterColorPageEntry<ulong>>());

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            future,
            nowSeconds: 5d,
            _ => true);
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 5d,
            out NeonLetterColorPageRequest retry);

        Assert.Equal(
            (false, false, true, request),
            (accepted, client.IsComplete, due, retry));
    }

    [Fact]
    public void CompleteNonemptyPageRequiresAnEmptyCatchUp()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        int publishCount = 0;
        var entry =
            new NeonLetterColorPageEntry<ulong>(1, 1, Red);

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            Response(
                request,
                sequence: 1,
                watermark: 1,
                nextCursor: 1,
                complete: true,
                entry),
            nowSeconds: 0d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest catchUp);

        Assert.Equal(
            (true, 1, false, true, 1ul, 0ul),
            (
                accepted,
                publishCount,
                client.IsComplete,
                due,
                catchUp.CursorChangeSerial,
                catchUp.WatermarkChangeSerial));
    }

    [Fact]
    public void EmptyPageAheadOfTheCursorRequiresConfirmation()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        var response = new NeonLetterColorPageResponse<ulong>(
            NeonLetterColorPageProtocol.ProtocolVersion,
            request.SyncId,
            Sequence: 1,
            WatermarkChangeSerial: 1,
            NextCursorChangeSerial: 1,
            Complete: true,
            Array.Empty<NeonLetterColorPageEntry<ulong>>());

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            response,
            nowSeconds: 0d,
            _ => true);
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest confirmation);

        Assert.Equal(
            (true, false, true, 1ul, 0ul),
            (
                accepted,
                client.IsComplete,
                due,
                confirmation.CursorChangeSerial,
                confirmation.WatermarkChangeSerial));
    }

    [Fact]
    public void IncompletePageCannotBeEmpty()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 2,
            nextCursor: 1,
            complete: false);

        AssertRejectedResponseRemainsDue(client, request, response);
    }

    [Fact]
    public void IncompletePageMustAdvanceBeyondOutstandingCursor()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 2,
            nextCursor: 0,
            complete: false,
            new NeonLetterColorPageEntry<ulong>(1, 1, Red));

        AssertRejectedResponseRemainsDue(client, request, response);
    }

    [Fact]
    public void IncompletePageMustStopBeforeItsWatermark()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 2,
            nextCursor: 2,
            complete: false,
            new NeonLetterColorPageEntry<ulong>(1, 1, Red));

        AssertRejectedResponseRemainsDue(client, request, response);
    }

    [Fact]
    public void CompletePageCursorMustEqualItsWatermark()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 2,
            nextCursor: 1,
            complete: true,
            new NeonLetterColorPageEntry<ulong>(1, 1, Red));

        AssertRejectedResponseRemainsDue(client, request, response);
    }

    [Fact]
    public void StrictlyProgressingIncompletePageAdvancesTheRequest()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 2,
            nextCursor: 1,
            complete: false,
            new NeonLetterColorPageEntry<ulong>(1, 1, Red));

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            response,
            nowSeconds: 0d,
            _ => true);
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest next);

        Assert.Equal(
            (true, false, true, 1ul, 2ul),
            (
                accepted,
                client.IsComplete,
                due,
                next.CursorChangeSerial,
                next.WatermarkChangeSerial));
    }

    [Fact]
    public void DuplicatePageIdentityIsRejectedBeforePublishing()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        var first =
            new NeonLetterColorPageEntry<ulong>(1, 1, Red);
        var duplicate =
            new NeonLetterColorPageEntry<ulong>(1, 2, Green);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 2,
            nextCursor: 2,
            complete: true,
            first,
            duplicate);
        int publishCount = 0;

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            response,
            nowSeconds: 0d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest retry);

        Assert.Equal(
            (false, 0, true, request),
            (accepted, publishCount, due, retry));
    }

    [Fact]
    public void ClientRejectsAResponseFromAnotherProtocolVersion()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        var response = new NeonLetterColorPageResponse<ulong>(
            (byte)(NeonLetterColorPageProtocol.ProtocolVersion + 1),
            request.SyncId,
            Sequence: 1,
            WatermarkChangeSerial: 0,
            NextCursorChangeSerial: 0,
            Complete: true,
            Array.Empty<NeonLetterColorPageEntry<ulong>>());

        AssertRejectedResponseRemainsDue(client, request, response);
    }

    [Fact]
    public void ClientRejectsAZeroIdentityBeforePublishing()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 1,
            nextCursor: 1,
            complete: true,
            new NeonLetterColorPageEntry<ulong>(0, 1, Red));

        AssertRejectedResponseRemainsDue(client, request, response);
    }

    [Fact]
    public void ClientRejectsAZeroRevisionBeforePublishing()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 1,
            nextCursor: 1,
            complete: true,
            new NeonLetterColorPageEntry<ulong>(1, 0, Red));

        AssertRejectedResponseRemainsDue(client, request, response);
    }

    [Fact]
    public void ThrowingMidPagePublishRetriesTheExactRequestAndPage()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        client.RecordRequestAttempt(nowSeconds: 0d);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 2,
            nextCursor: 2,
            complete: true,
            new NeonLetterColorPageEntry<ulong>(1, 1, Red),
            new NeonLetterColorPageEntry<ulong>(2, 1, Green));
        int publishAttemptCount = 0;

        Exception? error = Record.Exception(
            () => client.TryAcceptResponse(
                canApply: true,
                response,
                nowSeconds: 1d,
                _ =>
                {
                    publishAttemptCount++;
                    if (publishAttemptCount == 2)
                    {
                        throw new InvalidOperationException("publish");
                    }

                    return true;
                }));
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 1d,
            out NeonLetterColorPageRequest retry);
        bool accepted = client.TryAcceptResponse(
            canApply: true,
            response,
            nowSeconds: 1d,
            _ =>
            {
                publishAttemptCount++;
                return true;
            });

        Assert.Equal(
            (
                typeof(InvalidOperationException),
                true,
                request,
                true,
                4),
            (
                error?.GetType(),
                due,
                retry,
                accepted,
                publishAttemptCount));
    }

    [Fact]
    public void ClientClearResetsCompletionAndRequiresANewSession()
    {
        NeonLetterColorPageClientCoordinator<ulong> client =
            StartClient(out NeonLetterColorPageRequest request);
        client.TryAcceptResponse(
            canApply: true,
            StableEmptyResponse(request),
            nowSeconds: 0d,
            _ => true);

        client.Clear();
        bool completeAfterClear = client.IsComplete;
        bool dueBeforeStart = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out _);
        bool started = client.StartSession(canStart: true);
        bool dueAfterStart = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out _);

        Assert.Equal(
            (false, false, true, true),
            (
                completeAfterClear,
                dueBeforeStart,
                started,
                dueAfterStart));
    }

    [Fact]
    public void OtherIdentityChangesInvalidateAnOldReservation()
    {
        var colors = new NeonLetterAuthoritativeColors<ulong>();
        AcceptColor(colors, identity: 2, Red);
        bool reserved = colors.TryReserve(
            isHost: true,
            identity: 1,
            isLive: true,
            NeonLetterSmallCatalog.Get('A').RecipeId,
            Green,
            out NeonLetterColorReservation<ulong> reservation);
        AcceptColor(colors, identity: 2, Green);
        AcceptColor(colors, identity: 2, Red);

        Exception? error = Record.Exception(
            () => colors.Commit(reservation));
        NeonLetterAuthoritativeColor state = colors.ResolveState(1);

        Assert.Equal(
            (true, typeof(InvalidOperationException), 0ul, 3ul),
            (
                reserved,
                error?.GetType(),
                state.Revision,
                colors.CurrentChangeSerial));
    }

    [Fact]
    public void RevisionMismatchAloneInvalidatesAReservation()
    {
        var colors = new NeonLetterAuthoritativeColors<ulong>(
            initialIdentity: 1,
            initialColor: Red,
            initialRevision: 1,
            initialChangeSerial: 1);
        var wrongRevision = new NeonLetterColorReservation<ulong>(
            Identity: 1,
            Color: Green,
            ExpectedRevision: 0,
            ExpectedChangeSerial: 1,
            ExpectedLastChangeSerial: 1,
            Revision: 2,
            ChangeSerial: 2);

        Exception? error = Record.Exception(
            () => colors.Commit(wrongRevision));
        NeonLetterAuthoritativeColor state = colors.ResolveState(1);

        Assert.Equal(
            (
                typeof(InvalidOperationException),
                1ul,
                Red,
                1ul),
            (
                error?.GetType(),
                state.Revision,
                state.Color,
                colors.CurrentChangeSerial));
    }

    [Fact]
    public void ReusedSerialFailureRestoresThePreviousIndexEntry()
    {
        var colors = new NeonLetterAuthoritativeColors<ulong>(
            initialIdentity: 1,
            initialColor: Red,
            initialRevision: 1,
            initialChangeSerial: 1);
        AcceptColor(colors, identity: 2, Green);
        var reusedSerial = new NeonLetterColorReservation<ulong>(
            Identity: 1,
            Color: Green,
            ExpectedRevision: 1,
            ExpectedChangeSerial: 1,
            ExpectedLastChangeSerial: 2,
            Revision: 2,
            ChangeSerial: 2);

        Exception? error = Record.Exception(
            () => colors.Commit(reusedSerial));
        NeonLetterAuthoritativeColorPage<ulong> page =
            colors.CreatePage(
                cursorChangeSerial: 0,
                watermarkChangeSerial: 0);

        Assert.Equal(
            (
                typeof(InvalidOperationException),
                2,
                2,
                2ul,
                "1,2",
                1ul),
            (
                error?.GetType(),
                colors.CurrentEntryCount,
                colors.IndexedEntryCount,
                colors.CurrentChangeSerial,
                string.Join(
                    ",",
                    page.Entries.Select(entry => entry.Identity)),
                colors.ResolveState(1).Revision));
    }

    [Fact]
    public void AuthoritativeClearRemovesTheOldSerialIndex()
    {
        NeonLetterAuthoritativeColors<ulong> colors =
            CreateColors(count: 3);

        colors.Clear();
        NeonLetterColorAcceptance accepted =
            colors.TryAccept(
                isHost: true,
                identity: 9,
                isLive: true,
                NeonLetterSmallCatalog.Get('A').RecipeId,
                Green);
        NeonLetterAuthoritativeColorPage<ulong> page =
            colors.CreatePage(
                cursorChangeSerial: 0,
                watermarkChangeSerial: 0);

        Assert.Equal(
            (true, 1, 1, 1ul, "9", 1ul),
            (
                accepted.Accepted,
                colors.CurrentEntryCount,
                colors.IndexedEntryCount,
                colors.CurrentChangeSerial,
                string.Join(
                    ",",
                    page.Entries.Select(entry => entry.Identity)),
                page.Entries.Single().EntityRevision));
    }

    [Fact]
    public void PageRangeRejectsEachIndependentInvalidBound()
    {
        NeonLetterAuthoritativeColors<ulong> colors =
            CreateColors(count: 2);

        Exception? cursorError = Record.Exception(
            () => colors.CreatePage(
                cursorChangeSerial: 2,
                watermarkChangeSerial: 1));
        Exception? watermarkError = Record.Exception(
            () => colors.CreatePage(
                cursorChangeSerial: 0,
                watermarkChangeSerial: 3));

        Assert.Equal(
            (
                typeof(ArgumentOutOfRangeException),
                typeof(ArgumentOutOfRangeException)),
            (cursorError?.GetType(), watermarkError?.GetType()));
    }

    [Fact]
    public void PersistentReplacementKeepsItsStatusAndNewestColor()
    {
        var pending = new NeonLetterPendingColors<ulong>(
            capacity: 2,
            lifetimeSeconds: 5d);
        bool retained = pending.TryEnqueuePersistent(
            identity: 1,
            Red,
            nowSeconds: 0d);
        pending.Enqueue(
            identity: 1,
            Green,
            nowSeconds: 1d);

        pending.Prune(nowSeconds: 100d);
        int countAfterPrune = pending.Count;
        NeonRgba appliedColor = default;
        int appliedCount = pending.ApplyReady(
            nowSeconds: 100d,
            isReady: _ => true,
            apply: (_, color) => appliedColor = color);

        Assert.Equal(
            (true, 1, 1, Green, 0),
            (
                retained,
                countAfterPrune,
                appliedCount,
                appliedColor,
                pending.Count));
    }

    [Fact]
    public void TransientPendingColorExpiresAtTheExactBoundary()
    {
        var pending = new NeonLetterPendingColors<ulong>(
            capacity: 1,
            lifetimeSeconds: 5d);
        pending.Enqueue(
            identity: 1,
            Red,
            nowSeconds: 0d);
        int applyCount = 0;

        bool applied = pending.TryApply(
            identity: 1,
            nowSeconds: 5d,
            isReady: _ => true,
            apply: (_, _) => applyCount++);

        Assert.Equal(
            (false, 0, 0),
            (applied, applyCount, pending.Count));
    }

    [Fact]
    public void MissingPendingIdentityCannotApply()
    {
        var pending = new NeonLetterPendingColors<ulong>(
            capacity: 1,
            lifetimeSeconds: 5d);
        int callbackCount = 0;

        bool applied = pending.TryApply(
            identity: 1,
            nowSeconds: 0d,
            isReady: _ =>
            {
                callbackCount++;
                return true;
            },
            apply: (_, _) => callbackCount++);

        Assert.Equal(
            (false, 0, 0),
            (applied, callbackCount, pending.Count));
    }

    [Fact]
    public void PersistentHeadCannotBeEvictedByTransientOverflow()
    {
        var pending = new NeonLetterPendingColors<ulong>(
            capacity: 1,
            lifetimeSeconds: 5d);
        bool retained = pending.TryEnqueuePersistent(
            identity: 1,
            Red,
            nowSeconds: 0d);
        pending.Enqueue(
            identity: 2,
            Green,
            nowSeconds: 1d);
        int countAfterOverflow = pending.Count;
        ulong appliedIdentity = 0;

        int appliedCount = pending.ApplyReady(
            nowSeconds: 100d,
            isReady: _ => true,
            apply: (identity, _) => appliedIdentity = identity);

        Assert.Equal(
            (true, 1, 1, 1ul, 0),
            (
                retained,
                countAfterOverflow,
                appliedCount,
                appliedIdentity,
                pending.Count));
    }

    [Fact]
    public void RepeatedPersistentReplacementRemainsNonEvictable()
    {
        var pending = new NeonLetterPendingColors<ulong>(
            capacity: 1,
            lifetimeSeconds: 5d);
        bool firstRetained = pending.TryEnqueuePersistent(
            identity: 1,
            Red,
            nowSeconds: 0d);
        bool replacementRetained = pending.TryEnqueuePersistent(
            identity: 1,
            Green,
            nowSeconds: 1d);
        pending.Enqueue(
            identity: 2,
            Red,
            nowSeconds: 2d);
        int countAfterOverflow = pending.Count;
        ulong appliedIdentity = 0;
        NeonRgba appliedColor = default;

        int appliedCount = pending.ApplyReady(
            nowSeconds: 100d,
            isReady: _ => true,
            apply: (identity, color) =>
            {
                appliedIdentity = identity;
                appliedColor = color;
            });

        Assert.Equal(
            (true, true, 1, 1, 1ul, Green),
            (
                firstRetained,
                replacementRetained,
                countAfterOverflow,
                appliedCount,
                appliedIdentity,
                appliedColor));
    }

    [Fact]
    public void AuthoritativeReceiveAppliesImmediatelyWhenReady()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity: 2,
            pendingLifetimeSeconds: 5d);
        int applyCount = 0;
        NeonRgba appliedColor = default;

        bool retained = state.TryReceiveAuthoritative(
            identity: 1,
            Red,
            nowSeconds: 0d,
            isReady: _ => true,
            apply: (_, color) =>
            {
                applyCount++;
                appliedColor = color;
            });

        Assert.Equal(
            (true, 1, Red, 0, Red),
            (
                retained,
                applyCount,
                appliedColor,
                state.PendingCount,
                state.Resolve(1)));
    }

    private static void AssertRejectedResponseRemainsDue(
        NeonLetterColorPageClientCoordinator<ulong> client,
        NeonLetterColorPageRequest request,
        NeonLetterColorPageResponse<ulong> response)
    {
        int publishCount = 0;
        bool accepted = client.TryAcceptResponse(
            canApply: true,
            response,
            nowSeconds: 5d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool due = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 5d,
            out NeonLetterColorPageRequest retry);

        Assert.Equal(
            (false, 0, true, request),
            (accepted, publishCount, due, retry));
    }

    private static NeonLetterAuthoritativeColors<ulong> CreateColors(
        int count)
    {
        var colors = new NeonLetterAuthoritativeColors<ulong>();
        for (ulong identity = 1; identity <= (ulong)count; identity++)
        {
            AcceptColor(
                colors,
                identity,
                new NeonRgba(identity / 255f, 0f, 0f, 1f));
        }

        return colors;
    }

    private static NeonLetterColorAcceptance AcceptColor(
        NeonLetterAuthoritativeColors<ulong> colors,
        ulong identity,
        NeonRgba color)
    {
        return colors.TryAccept(
            isHost: true,
            identity,
            isLive: true,
            NeonLetterSmallCatalog.Get('A').RecipeId,
            color);
    }

    private static NeonLetterColorPageClientCoordinator<ulong> StartClient(
        out NeonLetterColorPageRequest request)
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out request);
        return client;
    }

    private static NeonLetterColorPageRequest InitialRequest(ulong syncId)
    {
        return new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            syncId,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);
    }

    private static NeonLetterColorPageResponse<ulong> StableEmptyResponse(
        NeonLetterColorPageRequest request)
    {
        return Response(
            request,
            sequence: 1,
            watermark: 0,
            nextCursor: 0,
            complete: true);
    }

    private static NeonLetterColorPageResponse<ulong> Response(
        NeonLetterColorPageRequest request,
        ulong sequence,
        ulong watermark,
        ulong nextCursor,
        bool complete,
        params NeonLetterColorPageEntry<ulong>[] entries)
    {
        return new NeonLetterColorPageResponse<ulong>(
            NeonLetterColorPageProtocol.ProtocolVersion,
            request.SyncId,
            sequence,
            watermark,
            nextCursor,
            complete,
            entries);
    }

    private static object[] HeaderTokens(
        ulong syncId,
        ulong sequence,
        int count,
        byte complete)
    {
        return new object[]
        {
            NeonLetterColorPageProtocol.ProtocolVersion,
            syncId,
            sequence,
            1ul,
            1ul,
            count,
            complete
        };
    }

    private static NeonLetterColorPageResponse<ulong> ReadWireResponse(
        object[] tokens)
    {
        var reader = new PagingWireReader(tokens);
        return NeonLetterColorPageWireParser.ReadResponse<
            PagingWireReader,
            ulong>(ref reader);
    }

    private struct PagingWireReader :
        INeonLetterColorPageWireReader<ulong>
    {
        private readonly object[] _tokens;
        private int _index;

        internal PagingWireReader(object[] tokens)
        {
            _tokens = tokens;
            _index = 0;
        }

        public bool IsFullyConsumed => _index == _tokens.Length;

        public byte ReadByte()
        {
            return Read<byte>();
        }

        public ulong ReadUInt64()
        {
            return Read<ulong>();
        }

        public int ReadInt32()
        {
            return Read<int>();
        }

        public ulong ReadIdentity()
        {
            return Read<ulong>();
        }

        public NeonRgba ReadColor(byte protocolVersion)
        {
            return Read<NeonRgba>();
        }

        private T Read<T>()
        {
            if (_index >= _tokens.Length)
            {
                throw new EndOfStreamException();
            }

            return (T)_tokens[_index++];
        }
    }
}
