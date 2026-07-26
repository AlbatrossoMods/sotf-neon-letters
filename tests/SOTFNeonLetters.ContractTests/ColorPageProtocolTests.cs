using SOTFNeonLetters;
using Xunit;

public sealed class ColorPageProtocolTests
{
    private static readonly NeonRgba Red = new(1f, 0f, 0f, 1f);
    private static readonly NeonRgba Green = new(0f, 1f, 0f, 1f);

    [Fact]
    public void SixtyFourCurrentColorsFitInOneCompletePage()
    {
        var colors = new NeonLetterAuthoritativeColors<ulong>();
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        for (ulong identity = 1;
             identity <= NeonLetterColorPageProtocol.MaxPageEntries;
             identity++)
        {
            colors.TryAccept(
                isHost: true,
                identity,
                isLive: true,
                recipeId,
                new NeonRgba(identity / 255f, 0f, 0f, 1f));
        }

        NeonLetterAuthoritativeColorPage<ulong> page = colors.CreatePage(
            cursorChangeSerial: 0,
            watermarkChangeSerial: 0);

        Assert.Equal(
            (
                NeonLetterColorPageProtocol.MaxPageEntries,
                64ul,
                64ul,
                true),
            (
                page.Entries.Count,
                page.WatermarkChangeSerial,
                page.NextCursorChangeSerial,
                page.Complete));
    }

    [Fact]
    public void SixtyFiveCurrentColorsAreReadAsTwoBoundedPages()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 65);

        NeonLetterAuthoritativeColorPage<ulong> first = colors.CreatePage(
            cursorChangeSerial: 0,
            watermarkChangeSerial: 0);
        NeonLetterAuthoritativeColorPage<ulong> second = colors.CreatePage(
            first.NextCursorChangeSerial,
            first.WatermarkChangeSerial);

        Assert.Equal(
            (64, false, 1, true, 65ul),
            (
                first.Entries.Count,
                first.Complete,
                second.Entries.Count,
                second.Complete,
                second.NextCursorChangeSerial));
    }

    [Fact]
    public void EmptyAuthoritativeStateProducesOneEmptyCompletePage()
    {
        var colors = new NeonLetterAuthoritativeColors<ulong>();

        NeonLetterAuthoritativeColorPage<ulong> page = colors.CreatePage(
            cursorChangeSerial: 0,
            watermarkChangeSerial: 0);

        Assert.Equal(
            (0, 0ul, 0ul, true),
            (
                page.Entries.Count,
                page.WatermarkChangeSerial,
                page.NextCursorChangeSerial,
                page.Complete));
    }

    [Fact]
    public void UpdatingAnEntityMovesOnlyItsCurrentValueInTheSerialIndex()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 2);
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;

        NeonLetterColorAcceptance update = colors.TryAccept(
            isHost: true,
            identity: 1,
            isLive: true,
            recipeId,
            Green);
        NeonLetterAuthoritativeColorPage<ulong> page = colors.CreatePage(
            cursorChangeSerial: 0,
            watermarkChangeSerial: 0);

        Assert.Equal(
            (2, 2, 3ul, 2ul, Green),
            (
                colors.CurrentEntryCount,
                colors.IndexedEntryCount,
                colors.CurrentChangeSerial,
                update.Revision,
                page.Entries.Single(entry => entry.Identity == 1).Color));
    }

    [Fact]
    public void EntityMovedBeyondAWatermarkAppearsInTheCatchUpPass()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 65);
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        NeonLetterAuthoritativeColorPage<ulong> first = colors.CreatePage(
            cursorChangeSerial: 0,
            watermarkChangeSerial: 0);

        colors.TryAccept(
            isHost: true,
            identity: 65,
            isLive: true,
            recipeId,
            Green);
        NeonLetterAuthoritativeColorPage<ulong> endOfInitialPass =
            colors.CreatePage(
                first.NextCursorChangeSerial,
                first.WatermarkChangeSerial);
        NeonLetterAuthoritativeColorPage<ulong> catchUp = colors.CreatePage(
            endOfInitialPass.NextCursorChangeSerial,
            watermarkChangeSerial: 0);

        Assert.Equal(
            (0, true, 1, 65ul, 2ul, Green),
            (
                endOfInitialPass.Entries.Count,
                endOfInitialPass.Complete,
                catchUp.Entries.Count,
                catchUp.Entries.Single().Identity,
                catchUp.Entries.Single().EntityRevision,
                catchUp.Entries.Single().Color));
    }

    [Fact]
    public void AlreadyEmittedIdentityMutationConvergesInCatchUp()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 65);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(colors);
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        var applied = new Dictionary<ulong, NeonLetterColorPageEntry<ulong>>();
        client.StartSession(canStart: true);

        ApplyNextPage(host, client, applied);
        colors.TryAccept(
            isHost: true,
            identity: 1,
            isLive: true,
            NeonLetterSmallCatalog.Get('A').RecipeId,
            Green);
        ApplyNextPage(host, client, applied);
        ApplyNextPage(host, client, applied);
        ApplyNextPage(host, client, applied);

        Assert.Equal(
            (true, 65, 2ul, Green),
            (
                client.IsComplete,
                applied.Count,
                applied[1].EntityRevision,
                applied[1].Color));
    }

    [Fact]
    public void ExhaustedChangeSerialFailsClosedUntilSessionReset()
    {
        var colors = new NeonLetterAuthoritativeColors<ulong>(
            initialChangeSerial: ulong.MaxValue);
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;

        NeonLetterColorAcceptance exhausted = colors.TryAccept(
            isHost: true,
            identity: 1,
            isLive: true,
            recipeId,
            Red);
        ulong exhaustedSerial = colors.CurrentChangeSerial;
        colors.Clear();
        NeonLetterColorAcceptance afterReset = colors.TryAccept(
            isHost: true,
            identity: 1,
            isLive: true,
            recipeId,
            Red);

        Assert.Equal(
            (false, 0ul, ulong.MaxValue, 1ul, 1ul),
            (
                exhausted.Accepted,
                exhausted.Revision,
                exhaustedSerial,
                colors.CurrentChangeSerial,
                afterReset.Revision));
    }

    [Fact]
    public void HostCachesOnlyTheOutstandingTargetedPageForRetry()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 65);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(colors);
        var request = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 7,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);

        bool created = host.TryCreateResponse(
            "requesting-peer",
            canSend: true,
            request,
            out NeonLetterTargetedColorPage<string, ulong> first);
        bool duplicateCreated = host.TryCreateResponse(
            "requesting-peer",
            canSend: true,
            request,
            out NeonLetterTargetedColorPage<string, ulong> duplicate);

        Assert.Equal(
            (true, true, "requesting-peer", first.Response, 1, 1),
            (
                created,
                duplicateCreated,
                first.Peer,
                duplicate.Response,
                host.PeerCount,
                host.OutstandingPageCount));
    }

    [Fact]
    public void HostRejectsUngatedStaleAndOutOfOrderPageRequests()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 65);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(colors);
        var firstRequest = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 7,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);
        bool gated = host.TryCreateResponse(
            "peer",
            canSend: false,
            firstRequest,
            out _);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            firstRequest,
            out NeonLetterTargetedColorPage<string, ulong> first);
        var outOfOrder = firstRequest with
        {
            CursorChangeSerial =
                first.Response.NextCursorChangeSerial + 1,
            WatermarkChangeSerial =
                first.Response.WatermarkChangeSerial
        };
        bool outOfOrderAccepted = host.TryCreateResponse(
            "peer",
            canSend: true,
            outOfOrder,
            out _);
        bool staleSessionAccepted = host.TryCreateResponse(
            "peer",
            canSend: true,
            firstRequest with { SyncId = 6 },
            out _);

        Assert.Equal(
            (false, false, false, 1),
            (
                gated,
                outOfOrderAccepted,
                staleSessionAccepted,
                host.OutstandingPageCount));
    }

    [Fact]
    public void HostDisconnectAndSessionCleanupRemovePageState()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(colors);
        var request = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 7,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);
        host.TryCreateResponse("first", true, request, out _);
        host.TryCreateResponse("second", true, request, out _);

        host.Remove("first");
        (int PeerCount, int OutstandingPageCount) afterDisconnect =
            (host.PeerCount, host.OutstandingPageCount);
        host.Clear();

        Assert.Equal(
            ((1, 1), (0, 0)),
            (
                afterDisconnect,
                (host.PeerCount, host.OutstandingPageCount)));
    }

    [Fact]
    public void PeerRequestFloodCreatesOnePageOnlyDuringDrainAndRetryReusesIt()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 65);
        int pageCreations = 0;
        var host = new NeonLetterColorPageHostCoordinator<int, ulong>(
            colors,
            (cursor, watermark) =>
            {
                pageCreations++;
                return colors.CreatePage(cursor, watermark);
            });
        var firstRequest = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 1,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);
        var scheduleResults =
            new List<NeonLetterColorPageScheduleResult>();
        for (ulong syncId = 1; syncId <= 1_000; syncId++)
        {
            scheduleResults.Add(
                host.TryScheduleRequest(
                    peer: 1,
                    canSchedule: true,
                    firstRequest with { SyncId = syncId }));
        }

        (int PageCreations, int PendingCount) beforeDrain =
            (pageCreations, host.PendingRequestCount);
        var responses = new List<NeonLetterColorPageResponse<ulong>>();
        int firstSent = host.DrainScheduledRequests(
            canSend: _ => true,
            send: delivery => responses.Add(delivery.Response),
            onFailure: (_, _) => { });
        host.TryScheduleRequest(
            peer: 1,
            canSchedule: true,
            firstRequest);
        int retrySent = host.DrainScheduledRequests(
            canSend: _ => true,
            send: delivery => responses.Add(delivery.Response),
            onFailure: (_, _) => { });

        Assert.Equal(
            (
                1,
                999,
                (0, 1),
                1,
                1,
                1,
                2,
                responses[0]),
            (
                scheduleResults.Count(
                    result =>
                        result ==
                        NeonLetterColorPageScheduleResult.Scheduled),
                scheduleResults.Count(
                    result =>
                        result ==
                        NeonLetterColorPageScheduleResult.Rejected),
                beforeDrain,
                firstSent,
                retrySent,
                pageCreations,
                responses.Count,
                responses[1]));
    }

    [Fact]
    public void ManyPeersCreateAtMostFourPagesPerUpdateInFifoOrder()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        int pageCreations = 0;
        var host = new NeonLetterColorPageHostCoordinator<int, ulong>(
            colors,
            (cursor, watermark) =>
            {
                pageCreations++;
                return colors.CreatePage(cursor, watermark);
            });
        for (int peer = 1; peer <= 10; peer++)
        {
            host.TryScheduleRequest(
                peer,
                canSchedule: true,
                new NeonLetterColorPageRequest(
                    NeonLetterColorPageProtocol.ProtocolVersion,
                    SyncId: (ulong)peer,
                    CursorChangeSerial: 0,
                    WatermarkChangeSerial: 0));
        }

        var sentPeers = new List<int>();
        int firstSent = host.DrainScheduledRequests(
            _ => true,
            delivery => sentPeers.Add(delivery.Peer),
            (_, _) => { });
        (int Creations, int Pending) afterFirst =
            (pageCreations, host.PendingRequestCount);
        int secondSent = host.DrainScheduledRequests(
            _ => true,
            delivery => sentPeers.Add(delivery.Peer),
            (_, _) => { });

        Assert.Equal(
            (4, (4, 6), 4, 8, "1,2,3,4,5,6,7,8"),
            (
                firstSent,
                afterFirst,
                secondSent,
                pageCreations,
                string.Join(",", sentPeers)));
    }

    [Fact]
    public void QuarantineDropsPendingPageAndSchedulesPeerDisconnect()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        host.TryScheduleRequest(
            peer: 1,
            canSchedule: true,
            new NeonLetterColorPageRequest(
                NeonLetterColorPageProtocol.ProtocolVersion,
                SyncId: 1,
                CursorChangeSerial: 0,
                WatermarkChangeSerial: 0));
        var quarantinedPeers = new List<int>();

        host.Quarantine(1, quarantinedPeers.Add);
        int sent = host.DrainScheduledRequests(
            _ => true,
            _ => throw new InvalidOperationException(
                "A quarantined page must not be sent."),
            (_, _) => { });

        Assert.Equal(
            ("1", 0, 0),
            (
                string.Join(",", quarantinedPeers),
                sent,
                host.PendingRequestCount));
    }

    [Fact]
    public void DisconnectAndSessionCleanupRemoveQueuedAndCachedPageState()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        var request = new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            SyncId: 1,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);
        host.TryScheduleRequest(peer: 1, canSchedule: true, request);
        host.DrainScheduledRequests(_ => true, _ => { }, (_, _) => { });
        host.TryScheduleRequest(peer: 1, canSchedule: true, request);
        host.TryScheduleRequest(
            peer: 2,
            canSchedule: true,
            request with { SyncId = 2 });

        host.Remove(peer: 1);
        (int PeerCount, int PendingCount) afterDisconnect =
            (host.PeerCount, host.PendingRequestCount);
        host.Clear();

        Assert.Equal(
            ((0, 1), (0, 0)),
            (
                afterDisconnect,
                (host.PeerCount, host.PendingRequestCount)));
    }

    [Fact]
    public void ThousandDuplicateRequestsCoalesceBeforeOneUpdateDrain()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        NeonLetterColorPageRequest request = CreateInitialRequest(syncId: 1);
        var scheduleResults =
            new List<NeonLetterColorPageScheduleResult>();
        int serialized = 0;
        for (int duplicate = 0; duplicate < 1_000; duplicate++)
        {
            scheduleResults.Add(
                host.TryScheduleRequest(
                    peer: 1,
                    canSchedule: true,
                    request));
        }

        int pendingBeforeDrain = host.PendingRequestCount;
        int serializedBeforeDrain = serialized;
        int sent = host.DrainScheduledRequests(
            canSend: _ => true,
            send: _ => serialized++,
            onFailure: (_, _) => { });

        Assert.Equal(
            (1, 999, 1, 0, 1, 1, 0),
            (
                scheduleResults.Count(
                    result =>
                        result ==
                        NeonLetterColorPageScheduleResult.Scheduled),
                scheduleResults.Count(
                    result =>
                        result ==
                        NeonLetterColorPageScheduleResult.Coalesced),
                pendingBeforeDrain,
                serializedBeforeDrain,
                sent,
                serialized,
                host.PendingRequestCount));
    }

    [Fact]
    public void PageDeliveryDrainIsFairAndGloballyBoundedPerUpdate()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        for (int peer = 1; peer <= 10; peer++)
        {
            host.TryScheduleRequest(
                peer,
                canSchedule: true,
                CreateInitialRequest((ulong)peer));
        }

        var sentPeers = new List<int>();
        int first = host.DrainScheduledRequests(
            _ => true,
            delivery => sentPeers.Add(delivery.Peer),
            (_, _) => { });
        int pendingAfterFirst = host.PendingRequestCount;
        int second = host.DrainScheduledRequests(
            _ => true,
            delivery => sentPeers.Add(delivery.Peer),
            (_, _) => { });
        int pendingAfterSecond = host.PendingRequestCount;
        int third = host.DrainScheduledRequests(
            _ => true,
            delivery => sentPeers.Add(delivery.Peer),
            (_, _) => { });

        Assert.Equal(
            (
                NeonLetterColorPageDeliveryProtocol.MaxPagesPerUpdate,
                6,
                NeonLetterColorPageDeliveryProtocol.MaxPagesPerUpdate,
                2,
                2,
                "1,2,3,4,5,6,7,8,9,10"),
            (
                first,
                pendingAfterFirst,
                second,
                pendingAfterSecond,
                third,
                string.Join(",", sentPeers)));
    }

    [Fact]
    public void ExactRetryScheduledDuringSendWaitsForNextUpdate()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        NeonLetterColorPageRequest request = CreateInitialRequest(syncId: 1);
        host.TryScheduleRequest(peer: 1, canSchedule: true, request);
        var responses = new List<NeonLetterColorPageResponse<ulong>>();

        int first = host.DrainScheduledRequests(
            _ => true,
            delivery =>
            {
                responses.Add(delivery.Response);
                host.TryScheduleRequest(
                    delivery.Peer,
                    canSchedule: true,
                    request);
            },
            (_, _) => { });
        int pendingAfterFirst = host.PendingRequestCount;
        int second = host.DrainScheduledRequests(
            _ => true,
            delivery => responses.Add(delivery.Response),
            (_, _) => { });

        Assert.Equal(
            (1, 1, 1, 2, 0, responses[0]),
            (
                first,
                pendingAfterFirst,
                second,
                responses.Count,
                host.PendingRequestCount,
                responses[1]));
    }

    [Fact]
    public void FailedPageSendDoesNotBlockLaterPeers()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        host.TryScheduleRequest(1, true, CreateInitialRequest(syncId: 1));
        host.TryScheduleRequest(2, true, CreateInitialRequest(syncId: 2));
        host.TryScheduleRequest(3, true, CreateInitialRequest(syncId: 3));
        var sentPeers = new List<int>();
        var failedPeers = new List<int>();
        var quarantinedPeers = new List<int>();

        int sent = host.DrainScheduledRequests(
            _ => true,
            delivery =>
            {
                if (delivery.Peer == 2)
                {
                    throw new InvalidOperationException("send failed");
                }

                sentPeers.Add(delivery.Peer);
            },
            (peer, _) =>
            {
                failedPeers.Add(peer);
                host.Quarantine(peer, quarantinedPeers.Add);
            });

        Assert.Equal(
            (2, "1,3", "2", "2", 0, 2),
            (
                sent,
                string.Join(",", sentPeers),
                string.Join(",", failedPeers),
                string.Join(",", quarantinedPeers),
                host.PendingRequestCount,
                host.PeerCount));
    }

    [Fact]
    public void QuarantineDisconnectAndSessionCleanupBoundPendingPages()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        host.TryScheduleRequest(1, true, CreateInitialRequest(syncId: 1));
        host.TryScheduleRequest(2, true, CreateInitialRequest(syncId: 2));
        host.Remove(peer: 1);
        var sentPeers = new List<int>();

        int sent = host.DrainScheduledRequests(
            canSend: peer => peer != 2,
            send: delivery => sentPeers.Add(delivery.Peer),
            onFailure: (_, _) => { });
        host.TryScheduleRequest(3, true, CreateInitialRequest(syncId: 3));
        host.Clear();

        Assert.Equal(
            (0, string.Empty, 0, 0),
            (
                sent,
                string.Join(",", sentPeers),
                host.PendingRequestCount,
                host.PeerCount));
    }

    [Fact]
    public void HostilePendingPeerCountFailsClosedAtNamedMaximum()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        bool allWithinLimit = true;
        for (int peer = 1;
             peer <= NeonLetterColorPageDeliveryProtocol.MaxPendingPeers;
             peer++)
        {
            allWithinLimit &=
                host.TryScheduleRequest(
                    peer,
                    canSchedule: true,
                    CreateInitialRequest((ulong)peer)) ==
                NeonLetterColorPageScheduleResult.Scheduled;
        }

        NeonLetterColorPageScheduleResult overflow =
            host.TryScheduleRequest(
                NeonLetterColorPageDeliveryProtocol.MaxPendingPeers + 1,
                canSchedule: true,
                CreateInitialRequest(
                    (ulong)NeonLetterColorPageDeliveryProtocol
                        .MaxPendingPeers + 1));
        NeonLetterColorPageScheduleResult existing =
            host.TryScheduleRequest(
                peer: 1,
                canSchedule: true,
                CreateInitialRequest(syncId: 1));

        Assert.Equal(
            (
                true,
                NeonLetterColorPageScheduleResult.CapacityExceeded,
                NeonLetterColorPageScheduleResult.Coalesced,
                NeonLetterColorPageDeliveryProtocol.MaxPendingPeers),
            (
                allWithinLimit,
                overflow,
                existing,
                host.PendingRequestCount));
    }

    [Fact]
    public void LostPageRetriesTheSameSyncAndCursorWithoutRestarting()
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        client.StartSession(canStart: true);
        bool firstDue = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 10d,
            out NeonLetterColorPageRequest first);
        client.RecordRequestAttempt(nowSeconds: 10d);

        bool earlyRetry = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds:
                10d + NeonLetterColorPageProtocol.RetryIntervalSeconds -
                0.001d,
            out _);
        bool retryDue = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds:
                10d + NeonLetterColorPageProtocol.RetryIntervalSeconds,
            out NeonLetterColorPageRequest retry);

        Assert.Equal(
            (true, false, true, first),
            (firstDue, earlyRetry, retryDue, retry));
    }

    [Fact]
    public void MidPagePublishFailureRetriesTheExactCachedPage()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 2);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(colors);
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest request);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            request,
            out NeonLetterTargetedColorPage<string, ulong> first);
        int attempts = 0;

        bool failed = client.TryAcceptResponse(
            canApply: true,
            first.Response,
            nowSeconds: 1d,
            _ => ++attempts < 2);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 1d,
            out NeonLetterColorPageRequest retryRequest);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            retryRequest,
            out NeonLetterTargetedColorPage<string, ulong> retry);
        bool recovered = client.TryAcceptResponse(
            canApply: true,
            retry.Response,
            nowSeconds: 1d,
            _ => true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 1d,
            out NeonLetterColorPageRequest catchUpRequest);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            catchUpRequest,
            out NeonLetterTargetedColorPage<string, ulong> catchUp);
        bool completed = client.TryAcceptResponse(
            canApply: true,
            catchUp.Response,
            nowSeconds: 1d,
            _ => true);

        Assert.Equal(
            (false, true, true, true, request, first.Response),
            (
                failed,
                recovered,
                completed,
                client.IsComplete,
                retryRequest,
                retry.Response));
    }

    [Fact]
    public void DuplicateAndOutOfOrderPagesDoNotRepublishEntries()
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest request);
        var first = Response(
            request,
            sequence: 1,
            watermark: 2,
            nextCursor: 1,
            complete: false,
            new NeonLetterColorPageEntry<ulong>(1, 1, Red));
        int publishCount = 0;

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            first,
            nowSeconds: 1d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool duplicateAccepted = client.TryAcceptResponse(
            canApply: true,
            first,
            nowSeconds: 1d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool outOfOrderAccepted = client.TryAcceptResponse(
            canApply: true,
            first with { Sequence = 3 },
            nowSeconds: 1d,
            _ =>
            {
                publishCount++;
                return true;
            });
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 1d,
            out NeonLetterColorPageRequest expectedRetry);

        Assert.Equal(
            (true, false, false, 1, 1ul, 2ul),
            (
                accepted,
                duplicateAccepted,
                outOfOrderAccepted,
                publishCount,
                expectedRetry.CursorChangeSerial,
                expectedRetry.WatermarkChangeSerial));
    }

    [Fact]
    public void StableEmptyCatchUpCompletesAfterAppliedPages()
    {
        NeonLetterAuthoritativeColors<ulong> colors = CreateColors(count: 1);
        var host =
            new NeonLetterColorPageHostCoordinator<string, ulong>(colors);
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest initialRequest);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            initialRequest,
            out NeonLetterTargetedColorPage<string, ulong> initial);
        int appliedCount = 0;
        client.TryAcceptResponse(
            canApply: true,
            initial.Response,
            nowSeconds: 0d,
            _ =>
            {
                appliedCount++;
                return true;
            });
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest catchUpRequest);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            catchUpRequest,
            out NeonLetterTargetedColorPage<string, ulong> catchUp);

        bool catchUpAccepted = client.TryAcceptResponse(
            canApply: true,
            catchUp.Response,
            nowSeconds: 0d,
            _ =>
            {
                appliedCount++;
                return true;
            });

        Assert.Equal(
            (true, true, 1, 1ul, 0ul, true),
            (
                catchUpAccepted,
                client.IsComplete,
                appliedCount,
                catchUpRequest.CursorChangeSerial,
                catchUpRequest.WatermarkChangeSerial,
                catchUp.Response.Complete));
    }

    [Fact]
    public void StaleSessionAndMalformedPageLeaveExpectedRequestArmed()
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest request);
        NeonLetterColorPageEntry<ulong>[] oversized = Enumerable
            .Range(1, NeonLetterColorPageProtocol.MaxPageEntries + 1)
            .Select(index => new NeonLetterColorPageEntry<ulong>(
                (ulong)index,
                EntityRevision: 1,
                Red))
            .ToArray();
        NeonLetterColorPageResponse<ulong> malformed = Response(
            request,
            sequence: 1,
            watermark: 65,
            nextCursor: 64,
            complete: false,
            oversized);
        int publishCount = 0;

        bool staleAccepted = client.TryAcceptResponse(
            canApply: true,
            malformed with { SyncId = request.SyncId - 1 },
            nowSeconds: 1d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool malformedAccepted = client.TryAcceptResponse(
            canApply: true,
            malformed,
            nowSeconds: 1d,
            _ =>
            {
                publishCount++;
                return true;
            });
        bool retryDue = client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 1d,
            out NeonLetterColorPageRequest retry);

        Assert.Equal(
            (false, false, true, 0, request),
            (
                staleAccepted,
                malformedAccepted,
                retryDue,
                publishCount,
                retry));
    }

    [Fact]
    public void LiveAndPagedColorsUseTheSameRevisionWinner()
    {
        var apply = new NeonLetterClientApplyCoordinator<ulong>(
            timeoutSeconds: 5d);
        NeonLetterClientApplyDecision<ulong> live =
            apply.AcceptLive(1, Green, revision: 3);
        NeonLetterClientApplyDecision<ulong> stalePage =
            apply.AcceptLive(1, Red, revision: 2);
        NeonLetterClientApplyDecision<ulong> newerPage =
            apply.AcceptLive(1, Red, revision: 4);
        NeonLetterClientApplyDecision<ulong> staleLive =
            apply.AcceptLive(1, Green, revision: 3);

        Assert.Equal(
            (
                NeonLetterClientApplyAction.ApplyAuthoritative,
                NeonLetterClientApplyAction.Ignored,
                NeonLetterClientApplyAction.ApplyAuthoritative,
                NeonLetterClientApplyAction.Ignored,
                Red,
                4ul),
            (
                live.Action,
                stalePage.Action,
                newerPage.Action,
                staleLive.Action,
                apply.ResolveAuthoritative(1).Color,
                apply.ResolveAuthoritative(1).Revision));
    }

    [Fact]
    public void MissingPagedColorSurvivesArbitraryDelayAndAppliesLater()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity: 2,
            pendingLifetimeSeconds: 15d);
        state.TryReceiveAuthoritative(
            identity: 1,
            Red,
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });
        NeonRgba applied = default;

        int appliedCount = state.DrainReady(
            nowSeconds: 10_000d,
            maxItems: 1,
            isReady: _ => true,
            apply: (_, color) => applied = color,
            onApplyError: (_, _) => { });

        Assert.Equal((1, Red, 0), (appliedCount, applied, state.PendingCount));
    }

    [Fact]
    public void PendingOverflowDoesNotAcknowledgeOrEvictPagedState()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity:
                NeonLetterColorPageProtocol.MaxPendingEntries,
            pendingLifetimeSeconds: 15d);
        bool allRetained = true;

        for (ulong identity = 1;
             identity <=
                (ulong)NeonLetterColorPageProtocol.MaxPendingEntries;
             identity++)
        {
            allRetained &= state.TryReceiveAuthoritative(
                identity,
                Red,
                nowSeconds: 0d,
                isReady: _ => false,
                apply: (_, _) => { });
        }

        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> overflow = Response(
            request,
            sequence: 1,
            watermark: 1,
            nextCursor: 1,
            complete: true,
            new NeonLetterColorPageEntry<ulong>(
                (ulong)NeonLetterColorPageProtocol.MaxPendingEntries + 1,
                EntityRevision: 1,
                Green));

        bool accepted = client.TryAcceptResponse(
            canApply: true,
            overflow,
            nowSeconds: 1d,
            entry => state.TryReceiveAuthoritative(
                entry.Identity,
                entry.Color,
                nowSeconds: 1d,
                isReady: _ => false,
                apply: (_, _) => { }));
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 1d,
            out NeonLetterColorPageRequest retry);
        ulong appliedIdentity = 0;
        int appliedCount = state.DrainReady(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity => identity == 1,
            apply: (identity, _) => appliedIdentity = identity,
            onApplyError: (_, _) => { });

        Assert.Equal(
            (
                true,
                false,
                false,
                1,
                1ul,
                NeonLetterColorPageProtocol.MaxPendingEntries - 1,
                request),
            (
                allRetained,
                accepted,
                client.IsComplete,
                appliedCount,
                appliedIdentity,
                state.PendingCount,
                retry));
    }

    [Fact]
    public void RetentionFailureDoesNotCommitRevisionBeforeExactPageRetry()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity: 1,
            pendingLifetimeSeconds: 15d);
        state.TryReceiveAuthoritative(
            identity: 1,
            Red,
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });
        var apply = new NeonLetterClientApplyCoordinator<ulong>(
            timeoutSeconds: 5d);
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest request);
        NeonLetterColorPageResponse<ulong> response = Response(
            request,
            sequence: 1,
            watermark: 1,
            nextCursor: 1,
            complete: true,
            new NeonLetterColorPageEntry<ulong>(2, 1, Green));

        bool failed = client.TryAcceptResponse(
            canApply: true,
            response,
            nowSeconds: 1d,
            entry => apply.TryAcceptLive(
                entry.Identity,
                entry.Color,
                entry.EntityRevision,
                decision => state.TryReceiveAuthoritative(
                    decision.Identity,
                    decision.Color,
                    nowSeconds: 1d,
                    isReady: _ => false,
                    apply: (_, _) => { })));
        ulong revisionAfterFailure =
            apply.ResolveAuthoritative(2).Revision;
        state.DrainReady(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity => identity == 1,
            apply: (_, _) => { },
            onApplyError: (_, _) => { });
        bool retried = client.TryAcceptResponse(
            canApply: true,
            response,
            nowSeconds: 1d,
            entry => apply.TryAcceptLive(
                entry.Identity,
                entry.Color,
                entry.EntityRevision,
                decision => state.TryReceiveAuthoritative(
                    decision.Identity,
                    decision.Color,
                    nowSeconds: 1d,
                    isReady: _ => false,
                    apply: (_, _) => { })));

        Assert.Equal(
            (false, 0ul, true, 1ul, 1),
            (
                failed,
                revisionAfterFailure,
                retried,
                apply.ResolveAuthoritative(2).Revision,
                state.PendingCount));
    }

    [Fact]
    public void LiveRetentionFailureDoesNotCommitRevision()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity: 1,
            pendingLifetimeSeconds: 15d);
        state.TryReceiveAuthoritative(
            identity: 1,
            Red,
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });
        var apply = new NeonLetterClientApplyCoordinator<ulong>(
            timeoutSeconds: 5d);

        bool retained = apply.TryAcceptLive(
            identity: 2,
            Green,
            revision: 1,
            decision => state.TryReceiveAuthoritative(
                decision.Identity,
                decision.Color,
                nowSeconds: 1d,
                isReady: _ => false,
                apply: (_, _) => { }));

        Assert.Equal(
            (false, 0ul, 1),
            (
                retained,
                apply.ResolveAuthoritative(2).Revision,
                state.PendingCount));
    }

    [Fact]
    public void LiveThenEqualPageRevisionSurvivesUntilDelayedSpawn()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity: 2,
            pendingLifetimeSeconds: 15d);
        var apply = new NeonLetterClientApplyCoordinator<ulong>(
            timeoutSeconds: 5d);
        bool liveRetained = apply.TryAcceptLive(
            identity: 1,
            Green,
            revision: 1,
            decision => state.TryReceiveAuthoritative(
                decision.Identity,
                decision.Color,
                nowSeconds: 0d,
                isReady: _ => false,
                apply: (_, _) => { }));
        bool equalPageRetained = apply.TryAcceptLive(
            identity: 1,
            Green,
            revision: 1,
            decision => state.TryReceiveAuthoritative(
                decision.Identity,
                decision.Color,
                nowSeconds: 1d,
                isReady: _ => false,
                apply: (_, _) => { }));
        NeonRgba applied = default;

        int appliedCount = state.DrainReady(
            nowSeconds: 10_000d,
            maxItems: 1,
            isReady: _ => true,
            apply: (_, color) => applied = color,
            onApplyError: (_, _) => { });

        Assert.Equal(
            (true, true, 1, Green, 0),
            (
                liveRetained,
                equalPageRetained,
                appliedCount,
                applied,
                state.PendingCount));
    }

    [Fact]
    public void LiveOnlyAuthoritativeColorSurvivesUntilDelayedSpawn()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity: 1,
            pendingLifetimeSeconds: 15d);
        var apply = new NeonLetterClientApplyCoordinator<ulong>(
            timeoutSeconds: 5d);
        bool retained = apply.TryAcceptLive(
            identity: 1,
            Green,
            revision: 1,
            decision => state.TryReceiveAuthoritative(
                decision.Identity,
                decision.Color,
                nowSeconds: 0d,
                isReady: _ => false,
                apply: (_, _) => { }));
        NeonRgba applied = default;

        int appliedCount = state.DrainReady(
            nowSeconds: 10_000d,
            maxItems: 1,
            isReady: _ => true,
            apply: (_, color) => applied = color,
            onApplyError: (_, _) => { });

        Assert.Equal(
            (true, 1, Green, 0),
            (retained, appliedCount, applied, state.PendingCount));
    }

    [Fact]
    public void FullLiveCapacityFailsClosedAndPreservesOldestState()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            NeonLetterColorPageProtocol.MaxPendingEntries,
            pendingLifetimeSeconds: 15d);
        var apply = new NeonLetterClientApplyCoordinator<ulong>(
            timeoutSeconds: 5d);
        bool allRetained = true;
        for (ulong identity = 1;
             identity <= NeonLetterColorPageProtocol.MaxPendingEntries;
             identity++)
        {
            allRetained &= apply.TryAcceptLive(
                identity,
                Red,
                revision: 1,
                decision => state.TryReceiveAuthoritative(
                    decision.Identity,
                    decision.Color,
                    nowSeconds: 0d,
                    isReady: _ => false,
                    apply: (_, _) => { }));
        }

        ulong overflowIdentity =
            (ulong)NeonLetterColorPageProtocol.MaxPendingEntries + 1;
        bool overflowRetained = apply.TryAcceptLive(
            overflowIdentity,
            Green,
            revision: 1,
            decision => state.TryReceiveAuthoritative(
                decision.Identity,
                decision.Color,
                nowSeconds: 0d,
                isReady: _ => false,
                apply: (_, _) => { }));
        NeonRgba oldestApplied = default;
        int oldestAppliedCount = state.DrainReady(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity => identity == 1,
            apply: (_, color) => oldestApplied = color,
            onApplyError: (_, _) => { });

        Assert.Equal(
            (
                true,
                false,
                0ul,
                1,
                Red,
                NeonLetterColorPageProtocol.MaxPendingEntries - 1),
            (
                allRetained,
                overflowRetained,
                apply.ResolveAuthoritative(overflowIdentity).Revision,
                oldestAppliedCount,
                oldestApplied,
                state.PendingCount));
    }

    [Fact]
    public void PendingDrainInspectsOnlyThePerUpdateBudget()
    {
        var state = new NeonLetterReplicatedColorState<ulong>(
            pendingCapacity: 100,
            pendingLifetimeSeconds: 1d);
        for (ulong identity = 1; identity <= 100; identity++)
        {
            state.Receive(
                identity,
                Red,
                nowSeconds: 0d,
                isReady: _ => false,
                apply: (_, _) => { });
        }

        state.DrainReady(
            nowSeconds: 2d,
            maxItems: 16,
            isReady: _ => false,
            apply: (_, _) => { },
            onApplyError: (_, _) => { });

        Assert.Equal(84, state.PendingCount);
    }

    [Fact]
    public void ClientPagingIsHandshakeGatedAndReconnectRejectsOldPages()
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>();
        bool startedUnknown = client.StartSession(canStart: false);
        bool dueUnknown = client.TryGetDueRequest(
            canRequest: false,
            nowSeconds: 0d,
            out _);
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest oldRequest);
        client.Clear();
        client.StartSession(canStart: true);
        int appliedCount = 0;

        bool oldPageAccepted = client.TryAcceptResponse(
            canApply: true,
            Response(
                oldRequest,
                sequence: 1,
                watermark: 1,
                nextCursor: 1,
                complete: true,
                new NeonLetterColorPageEntry<ulong>(1, 1, Red)),
            nowSeconds: 1d,
            _ =>
            {
                appliedCount++;
                return true;
            });
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 1d,
            out NeonLetterColorPageRequest newRequest);

        Assert.Equal(
            (false, false, false, 0, oldRequest.SyncId + 1),
            (
                startedUnknown,
                dueUnknown,
                oldPageAccepted,
                appliedCount,
                newRequest.SyncId));
    }

    [Fact]
    public void SyncIdentifierNeverWrapsToZeroOrReusesAfterCleanup()
    {
        var client = new NeonLetterColorPageClientCoordinator<ulong>(
            firstSyncId: ulong.MaxValue);
        client.StartSession(canStart: true);
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest finalRequest);
        client.Clear();

        InvalidOperationException exhausted =
            Assert.Throws<InvalidOperationException>(
                () => client.StartSession(canStart: true));

        Assert.Equal(
            (ulong.MaxValue, true),
            (
                finalRequest.SyncId,
                exhausted.Message.Contains(
                    "identifier space is exhausted",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void RuntimeUsesBoundedPageEventsWithoutTheAtomicSnapshotProtocol()
    {
        string runtime = File.ReadAllText(
            FindRepositoryFile("NeonLetterMultiplayerRuntime.cs"));
        string wire = File.ReadAllText(
            FindRepositoryFile("NeonLetterMultiplayerWireEvents.cs"));

        Assert.Equal(
            (true, true, true, true, true, true, false, false),
            (
                wire.Contains(
                    "ColorPageRequestEvent",
                    StringComparison.Ordinal),
                wire.Contains(
                    "ColorPageResponseEvent",
                    StringComparison.Ordinal),
                runtime.Contains(
                    "ColorPageHostCoordinator.TryScheduleRequest",
                    StringComparison.Ordinal),
                runtime.Contains(
                    "ColorPageClientCoordinator.TryAcceptResponse",
                    StringComparison.Ordinal),
                runtime.Contains(
                    "ScheduleFailedColorPageConnection",
                    StringComparison.Ordinal),
                runtime.Contains(
                    "NeonLetterColorPageWireParser.ReadResponse",
                    StringComparison.Ordinal),
                runtime.Contains(
                    "SnapshotBatchCoordinator",
                    StringComparison.Ordinal),
                wire.Contains(
                    "ColorSnapshotFrameEvent",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void RuntimeQuarantinesRetentionFailureAndCleansPageCoordinators()
    {
        string runtime = File.ReadAllText(
            FindRepositoryFile("NeonLetterMultiplayerRuntime.cs"));
        string handshake = File.ReadAllText(
            FindRepositoryFile("NeonLetterMultiplayerHandshakeRuntime.cs"));

        Assert.Equal(
            (true, true, true, true, false),
            (
                runtime.Contains(
                    "DeferredClientDisconnects.Schedule(" +
                    "ClientDisconnectKey)",
                    StringComparison.Ordinal),
                handshake.Contains(
                    "ColorPageHostCoordinator.Remove(connection)",
                    StringComparison.Ordinal),
                handshake.Contains(
                    "ColorPageHostCoordinator.Clear()",
                    StringComparison.Ordinal),
                handshake.Contains(
                    "ColorPageClientCoordinator.Clear()",
                    StringComparison.Ordinal),
                runtime.Contains(
                    "ColorPageResponseScheduler",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void RuntimeSerializesPageResponsesOnlyDuringBoundedDrain()
    {
        string runtime = File.ReadAllText(
            FindRepositoryFile("NeonLetterMultiplayerRuntime.cs"));
        string requestHandler = ExtractSourceSegment(
            runtime,
            "private static void HandleColorPageRequest",
            "private static void DrainColorPageResponses");
        string responseDrain = ExtractSourceSegment(
            runtime,
            "private static void DrainColorPageResponses",
            "private static void SendColorPageDelivery");
        string sendAdapter = ExtractSourceSegment(
            runtime,
            "private static void SendColorPageDelivery",
            "private static void ScheduleFailedColorPageConnection");

        Assert.Equal(
            (true, false, false, true, true),
            (
                requestHandler.Contains(
                    "ColorPageHostCoordinator.TryScheduleRequest",
                    StringComparison.Ordinal),
                requestHandler.Contains(
                    "ColorPageHostCoordinator.TryCreateResponse",
                    StringComparison.Ordinal),
                requestHandler.Contains(
                    "ColorPageResponse.SendResponse",
                    StringComparison.Ordinal),
                responseDrain.Contains(
                    "ColorPageHostCoordinator.DrainScheduledRequests",
                    StringComparison.Ordinal),
                sendAdapter.Contains(
                    "ColorPageResponse.SendResponse",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void PageAllocationStaysBoundedForLargeAuthoritativeStores()
    {
        NeonLetterAuthoritativeColors<ulong> small =
            CreateColors(NeonLetterColorPageProtocol.MaxPageEntries);
        NeonLetterAuthoritativeColors<ulong> large =
            CreateColors(count: 50_000);

        (long AllocatedBytes, int EntryCount) smallMeasurement =
            MeasurePageAllocation(small);
        (long AllocatedBytes, int EntryCount) largeMeasurement =
            MeasurePageAllocation(large);

        Assert.Equal(
            (
                NeonLetterColorPageProtocol.MaxPageEntries,
                NeonLetterColorPageProtocol.MaxPageEntries,
                true),
            (
                smallMeasurement.EntryCount,
                largeMeasurement.EntryCount,
                largeMeasurement.AllocatedBytes <=
                    smallMeasurement.AllocatedBytes + 4_096));
    }

    [Fact]
    public void ThirtyTwoJoiningPeersKeepIndependentBoundedPages()
    {
        NeonLetterAuthoritativeColors<ulong> colors =
            CreateColors(count: 1_000);
        var host =
            new NeonLetterColorPageHostCoordinator<int, ulong>(colors);
        var firstPages =
            new Dictionary<int, NeonLetterTargetedColorPage<int, ulong>>();
        bool allTargetedAndBounded = true;
        for (int peer = 1; peer <= 32; peer++)
        {
            var request = new NeonLetterColorPageRequest(
                NeonLetterColorPageProtocol.ProtocolVersion,
                SyncId: (ulong)peer,
                CursorChangeSerial: 0,
                WatermarkChangeSerial: 0);
            bool created = host.TryCreateResponse(
                peer,
                canSend: true,
                request,
                out NeonLetterTargetedColorPage<int, ulong> delivery);
            firstPages.Add(peer, delivery);
            allTargetedAndBounded &=
                created &&
                delivery.Peer == peer &&
                delivery.Response.Entries.Count ==
                    NeonLetterColorPageProtocol.MaxPageEntries;
        }

        bool cursorsIndependent = true;
        for (int peer = 1; peer <= 32; peer++)
        {
            NeonLetterTargetedColorPage<int, ulong> first =
                firstPages[peer];
            NeonLetterColorPageRequest request = peer % 2 == 0
                ? new NeonLetterColorPageRequest(
                    NeonLetterColorPageProtocol.ProtocolVersion,
                    (ulong)peer,
                    first.Response.NextCursorChangeSerial,
                    first.Response.WatermarkChangeSerial)
                : new NeonLetterColorPageRequest(
                    NeonLetterColorPageProtocol.ProtocolVersion,
                    (ulong)peer,
                    CursorChangeSerial: 0,
                    WatermarkChangeSerial: 0);
            host.TryCreateResponse(
                peer,
                canSend: true,
                request,
                out NeonLetterTargetedColorPage<int, ulong> delivery);
            cursorsIndependent &=
                delivery.Peer == peer &&
                delivery.Response.Sequence ==
                    (peer % 2 == 0 ? 2ul : 1ul) &&
                delivery.Response.Entries.Count ==
                    NeonLetterColorPageProtocol.MaxPageEntries;
        }

        for (int peer = 2; peer <= 32; peer += 2)
        {
            host.Remove(peer);
        }

        (int PeerCount, int OutstandingPageCount) afterCleanup =
            (host.PeerCount, host.OutstandingPageCount);
        host.Clear();

        Assert.Equal(
            (true, true, (16, 16), (0, 0)),
            (
                allTargetedAndBounded,
                cursorsIndependent,
                afterCleanup,
                (host.PeerCount, host.OutstandingPageCount)));
    }

    [Fact]
    public void WireParserRejectsNegativeEntryCountBeforeAllocation()
    {
        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(
                HeaderTokens(
                    count: -1,
                    complete: 0)));
    }

    [Fact]
    public void WireParserRejectsOversizedEntryCountBeforeAllocation()
    {
        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(
                HeaderTokens(
                    count:
                        NeonLetterColorPageProtocol.MaxPageEntries + 1,
                    complete: 0)));
    }

    [Fact]
    public void WireParserRejectsTruncatedEntry()
    {
        Assert.Throws<EndOfStreamException>(
            () => ReadWireResponse(
                HeaderTokens(
                    count: 1,
                    complete: 0)));
    }

    [Fact]
    public void WireParserRejectsInvalidCompleteByte()
    {
        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(
                HeaderTokens(
                    count: 0,
                    complete: 2)));
    }

    [Fact]
    public void WireParserRejectsUnknownProtocolVersion()
    {
        object[] tokens = HeaderTokens(count: 0, complete: 1);
        tokens[0] =
            (byte)(NeonLetterColorPageProtocol.ProtocolVersion + 1);

        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(tokens));
    }

    [Fact]
    public void WireParserRejectsZeroIdentity()
    {
        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(
                EntryTokens(identity: 0, revision: 1)));
    }

    [Fact]
    public void WireParserRejectsZeroEntityRevision()
    {
        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(
                EntryTokens(identity: 1, revision: 0)));
    }

    [Fact]
    public void WireParserRejectsTrailingFrameData()
    {
        object[] tokens = HeaderTokens(
            count: 0,
            complete: 1).Concat(
                new object[] { (byte)99 }).ToArray();

        Assert.Throws<InvalidDataException>(
            () => ReadWireResponse(tokens));
    }

    private static NeonLetterAuthoritativeColors<ulong> CreateColors(int count)
    {
        var colors = new NeonLetterAuthoritativeColors<ulong>();
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        for (ulong identity = 1; identity <= (ulong)count; identity++)
        {
            colors.TryAccept(
                isHost: true,
                identity,
                isLive: true,
                recipeId,
                new NeonRgba(identity / 255f, 0f, 0f, 1f));
        }

        return colors;
    }

    private static void ApplyNextPage(
        NeonLetterColorPageHostCoordinator<string, ulong> host,
        NeonLetterColorPageClientCoordinator<ulong> client,
        IDictionary<ulong, NeonLetterColorPageEntry<ulong>> applied)
    {
        client.TryGetDueRequest(
            canRequest: true,
            nowSeconds: 0d,
            out NeonLetterColorPageRequest request);
        host.TryCreateResponse(
            "peer",
            canSend: true,
            request,
            out NeonLetterTargetedColorPage<string, ulong> delivery);
        client.TryAcceptResponse(
            canApply: true,
            delivery.Response,
            nowSeconds: 0d,
            entry =>
            {
                if (!applied.TryGetValue(
                        entry.Identity,
                        out NeonLetterColorPageEntry<ulong> current) ||
                    entry.EntityRevision > current.EntityRevision)
                {
                    applied[entry.Identity] = entry;
                }

                return true;
            });
    }

    private static (long AllocatedBytes, int EntryCount)
        MeasurePageAllocation(
            NeonLetterAuthoritativeColors<ulong> colors)
    {
        const int Iterations = 64;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            colors.CreatePage(
                cursorChangeSerial: 0,
                watermarkChangeSerial: 0);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int entryCount = 0;
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            NeonLetterAuthoritativeColorPage<ulong> page =
                colors.CreatePage(
                    cursorChangeSerial: 0,
                    watermarkChangeSerial: 0);
            entryCount = page.Entries.Count;
        }

        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;
        return (allocated / Iterations, entryCount);
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

    private static NeonLetterColorPageRequest CreateInitialRequest(
        ulong syncId)
    {
        return new NeonLetterColorPageRequest(
            NeonLetterColorPageProtocol.ProtocolVersion,
            syncId,
            CursorChangeSerial: 0,
            WatermarkChangeSerial: 0);
    }

    private static NeonLetterColorPageResponse<ulong> ReadWireResponse(
        object[] tokens)
    {
        var reader = new ScriptedColorPageWireReader(tokens);
        return NeonLetterColorPageWireParser.ReadResponse<
            ScriptedColorPageWireReader,
            ulong>(ref reader);
    }

    private static object[] HeaderTokens(int count, byte complete)
    {
        return new object[]
        {
            NeonLetterColorPageProtocol.ProtocolVersion,
            1ul,
            1ul,
            1ul,
            0ul,
            count,
            complete
        };
    }

    private static object[] EntryTokens(ulong identity, ulong revision)
    {
        object[] header = HeaderTokens(count: 1, complete: 1);
        return header.Concat(new object[]
        {
            identity,
            revision,
            Red
        }).ToArray();
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{relativePath}'.");
    }

    private static string ExtractSourceSegment(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(
            endMarker,
            start + startMarker.Length,
            StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException(
                $"Could not find source segment '{startMarker}'.");
        }

        return source.Substring(start, end - start);
    }

    private struct ScriptedColorPageWireReader :
        INeonLetterColorPageWireReader<ulong>
    {
        private readonly object[] _tokens;
        private int _index;

        internal ScriptedColorPageWireReader(object[] tokens)
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
