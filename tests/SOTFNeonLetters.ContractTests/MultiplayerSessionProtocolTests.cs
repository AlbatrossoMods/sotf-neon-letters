using System.Security.Cryptography;
using System.Text;
using SOTFNeonLetters;
using Xunit;

public sealed class MultiplayerSessionProtocolTests
{
    private static readonly NeonRgba Red = new(1f, 0f, 0f, 1f);
    private static readonly NeonRgba Green = new(0f, 1f, 0f, 1f);
    private static readonly NeonRgba Blue = new(0f, 0f, 1f, 1f);

    [Fact]
    public void CatalogHashIsDeterministicAndCoversEveryIdentityField()
    {
        var baseline = new[]
        {
            new NeonLetterCatalogIdentityEntry(0, 101, 100, "A", "Prefab_A"),
            new NeonLetterCatalogIdentityEntry(1, 103, 102, "B", "Prefab_B")
        };
        NeonLetterSha256Digest expected =
            NeonLetterSessionIdentityHasher.ComputeCatalogHash(baseline);
        var variations = new[]
        {
            baseline.Select((entry, index) => index == 0
                ? entry with { CatalogIndex = 9 }
                : entry).ToArray(),
            baseline.Select((entry, index) => index == 0
                ? entry with { RecipeId = 999 }
                : entry).ToArray(),
            baseline.Select((entry, index) => index == 0
                ? entry with { CraftingNodeId = 998 }
                : entry).ToArray(),
            baseline.Select((entry, index) => index == 0
                ? entry with { AssetKey = "Changed" }
                : entry).ToArray(),
            baseline.Select((entry, index) => index == 0
                ? entry with { PrefabAssetName = "Changed" }
                : entry).ToArray(),
            baseline.Reverse().ToArray()
        };

        bool allVariationsDiffer = variations.All(
            entries =>
                NeonLetterSessionIdentityHasher.ComputeCatalogHash(entries) !=
                expected);
        NeonLetterSha256Digest repeated =
            NeonLetterSessionIdentityHasher.ComputeCatalogHash(baseline);

        Assert.Equal((true, expected), (allVariationsDiffer, repeated));
    }

    [Fact]
    public void BundleHashUsesTheActualStreamContents()
    {
        byte[] contents = Encoding.UTF8.GetBytes("installed bundle bytes");
        using var stream = new MemoryStream(contents);

        NeonLetterSha256Digest actual =
            NeonLetterSessionIdentityHasher.ComputeBundleHash(stream);
        NeonLetterSha256Digest expected =
            NeonLetterSha256Digest.FromBytes(SHA256.HashData(contents));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CatalogHashUsesLengthPrefixesForTextFields()
    {
        var first = new[]
        {
            new NeonLetterCatalogIdentityEntry(0, 101, 100, "ab", "c")
        };
        var second = new[]
        {
            new NeonLetterCatalogIdentityEntry(0, 101, 100, "a", "bc")
        };

        Assert.NotEqual(
            NeonLetterSessionIdentityHasher.ComputeCatalogHash(first),
            NeonLetterSessionIdentityHasher.ComputeCatalogHash(second));
    }

    [Theory]
    [InlineData(HandshakeDifference.ReleaseVersion)]
    [InlineData(HandshakeDifference.ColorProtocol)]
    [InlineData(HandshakeDifference.Catalog)]
    [InlineData(HandshakeDifference.Bundle)]
    public void HandshakeRejectsEveryIdentityMismatch(
        HandshakeDifference difference)
    {
        NeonLetterSessionIdentity expected = CreateIdentity();
        NeonLetterSessionIdentity actual = difference switch
        {
            HandshakeDifference.ReleaseVersion =>
                expected with { ReleaseVersion = "0.3.2" },
            HandshakeDifference.ColorProtocol =>
                expected with { ColorProtocolVersion = 2 },
            HandshakeDifference.Catalog =>
                expected with { CatalogHash = Digest(33) },
            HandshakeDifference.Bundle =>
                expected with { BundleHash = Digest(44) },
            _ => expected
        };
        var registry = new NeonLetterHandshakeRegistry<string>(
            expected,
            NeonLetterSessionProtocol.NegotiationTimeoutSeconds);
        registry.Observe("peer", nowSeconds: 0d);

        NeonLetterHandshakeStatus status = registry.AcceptHello(
            "peer",
            NeonLetterHandshakeHello.Create(helloId: 1, actual));

        Assert.Equal(
            (NeonLetterPeerState.Rejected, ExpectedStatus(difference)),
            (registry.GetState("peer"), status));
    }

    [Fact]
    public void TrafficIsGatedUntilTheHandshakeIsAcceptedAndAfterCleanup()
    {
        NeonLetterSessionIdentity identity = CreateIdentity();
        var registry = new NeonLetterHandshakeRegistry<string>(
            identity,
            NeonLetterSessionProtocol.NegotiationTimeoutSeconds);
        registry.Observe("peer", nowSeconds: 0d);
        bool beforeHello = registry.IsAccepted("peer");

        NeonLetterHandshakeStatus result = registry.AcceptHello(
            "peer",
            NeonLetterHandshakeHello.Create(helloId: 1, identity));
        bool afterHello = registry.IsAccepted("peer");
        registry.Clear();

        Assert.Equal(
            (false, NeonLetterHandshakeStatus.Accepted, true, false),
            (beforeHello, result, afterHello, registry.IsAccepted("peer")));
    }

    [Fact]
    public void ReconnectClearsOldClientStateAndUnknownTicksCannotApply()
    {
        var session = new NeonLetterClientSessionGate();
        var replicated = new NeonLetterReplicatedColorState<int>(
            pendingCapacity: 4,
            pendingLifetimeSeconds: 5d);
        var clientApply = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        int appliedAfterReconnect = 0;
        session.BeginSession(replicated.Clear, clientApply.Clear);
        session.Accept();
        session.TryRun(() => replicated.Receive(
            identity: 7,
            Red,
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { }));
        clientApply.AcceptLive(8, Blue, revision: 4);

        session.BeginSession(replicated.Clear, clientApply.Clear);
        bool receiveRan = session.TryRun(() => replicated.Receive(
            identity: 7,
            Green,
            nowSeconds: 1d,
            isReady: _ => true,
            apply: (_, _) => appliedAfterReconnect++));
        bool drainRan = session.TryRun(() => replicated.DrainReady(
            nowSeconds: 1d,
            isReady: _ => true,
            apply: (_, _) => appliedAfterReconnect++));
        session.Clear(replicated.Clear, clientApply.Clear);
        session.Clear(replicated.Clear, clientApply.Clear);

        Assert.Equal(
            (false, 2ul, false, false, 0, 0, NeonRgba.ProjectCyan),
            (
                session.IsAccepted,
                session.Epoch,
                receiveRan,
                drainRan,
                replicated.PendingCount,
                appliedAfterReconnect,
                clientApply.ResolveAuthoritative(8).Color));
    }

    [Fact]
    public void MalformedTrafficForcesAnAcceptedPeerToRejected()
    {
        NeonLetterSessionIdentity identity = CreateIdentity();
        var registry = new NeonLetterHandshakeRegistry<string>(
            identity,
            NeonLetterSessionProtocol.NegotiationTimeoutSeconds);
        registry.Observe("peer", nowSeconds: 0d);
        registry.AcceptHello(
            "peer",
            NeonLetterHandshakeHello.Create(helloId: 1, identity));

        registry.Reject(
            "peer",
            NeonLetterHandshakeStatus.MalformedHello);

        Assert.Equal(
            (NeonLetterPeerState.Rejected, false),
            (registry.GetState("peer"), registry.IsAccepted("peer")));
    }

    [Fact]
    public void MissingHelloExpiresAtTheNegotiationDeadlineNotBeforeIt()
    {
        var registry = new NeonLetterHandshakeRegistry<string>(
            CreateIdentity(),
            NeonLetterSessionProtocol.NegotiationTimeoutSeconds);
        registry.Observe("peer", nowSeconds: 10d);

        IReadOnlyList<string> before = registry.RejectExpiredUnknown(
            10d + NeonLetterSessionProtocol.NegotiationTimeoutSeconds -
            0.000_001d);
        IReadOnlyList<string> at = registry.RejectExpiredUnknown(
            10d + NeonLetterSessionProtocol.NegotiationTimeoutSeconds);

        Assert.Equal(
            (0, 1, NeonLetterPeerState.Rejected),
            (before.Count, at.Count, registry.GetState("peer")));
    }

    [Fact]
    public void HelloSchedulerSendsImmediatelyResendsAtOneSecondAndTimesOutAtFive()
    {
        var scheduler = new NeonLetterHelloScheduler(
            NeonLetterSessionProtocol.HelloResendIntervalSeconds,
            NeonLetterSessionProtocol.NegotiationTimeoutSeconds);
        scheduler.Start(nowSeconds: 10d);

        bool immediate = scheduler.ShouldSend(10d);
        scheduler.MarkSent(10d);
        bool beforeResend = scheduler.ShouldSend(
            10d + NeonLetterSessionProtocol.HelloResendIntervalSeconds -
            0.000_001d);
        bool atResend = scheduler.ShouldSend(
            10d + NeonLetterSessionProtocol.HelloResendIntervalSeconds);
        bool beforeTimeout = scheduler.HasTimedOut(
            10d + NeonLetterSessionProtocol.NegotiationTimeoutSeconds -
            0.000_001d);
        bool atTimeout = scheduler.HasTimedOut(
            10d + NeonLetterSessionProtocol.NegotiationTimeoutSeconds);

        Assert.Equal(
            (true, false, true, false, true),
            (immediate, beforeResend, atResend, beforeTimeout, atTimeout));
    }

    [Fact]
    public void PeerSendFailureDoesNotBlockLaterAcceptedPeers()
    {
        string[] peers = { "first", "middle", "third" };
        var attempted = new List<string>();
        var received = new List<string>();
        var disconnects = new NeonLetterDeferredDisconnects<string>();

        NeonLetterPeerDelivery.Deliver(
            peers,
            isAccepted: _ => true,
            send: peer =>
            {
                attempted.Add(peer);
                if (peer == "middle")
                {
                    throw new InvalidOperationException("send failed");
                }

                received.Add(peer);
            },
            onFailure: (peer, _) => disconnects.Schedule(peer));

        Assert.Equal(
            ("first,middle,third", "first,third", 1, true),
            (
                string.Join(",", attempted),
                string.Join(",", received),
                disconnects.Count,
                disconnects.IsQuarantined("middle")));
    }

    [Fact]
    public void DeferredDisconnectUsesCappedExponentialUpdateBackoff()
    {
        var deferred = new NeonLetterDeferredDisconnects<string>();
        deferred.Schedule("peer");
        var attemptUpdates = new List<int>();
        int failureLogs = 0;

        for (int update = 1; update <= 256; update++)
        {
            int currentUpdate = update;
            deferred.Drain(
                exists: _ => true,
                execute: _ =>
                {
                    attemptUpdates.Add(currentUpdate);
                    throw new InvalidOperationException("persistent failure");
                },
                onFirstFailure: (_, _) => failureLogs++);
        }

        Assert.Equal(
            ("1,2,4,8,16,32,64,128,192,256", 1, 1),
            (
                string.Join(",", attemptUpdates),
                failureLogs,
                deferred.Count));
    }

    [Fact]
    public void DeferredDisconnectSucceedsAfterTransientFailures()
    {
        var deferred = new NeonLetterDeferredDisconnects<string>();
        deferred.Schedule("peer");
        var attemptUpdates = new List<int>();
        int failureLogs = 0;

        for (int update = 1; update <= 4; update++)
        {
            int currentUpdate = update;
            deferred.Drain(
                exists: _ => true,
                execute: _ =>
                {
                    attemptUpdates.Add(currentUpdate);
                    if (attemptUpdates.Count < 3)
                    {
                        throw new InvalidOperationException(
                            "transient failure");
                    }
                },
                onFirstFailure: (_, _) => failureLogs++);
        }

        Assert.Equal(
            ("1,2,4", 1, 0),
            (string.Join(",", attemptUpdates), failureLogs, deferred.Count));
    }

    [Fact]
    public void PermanentDisconnectFailureHasBoundedAttemptsOverManyUpdates()
    {
        var deferred = new NeonLetterDeferredDisconnects<string>();
        deferred.Schedule("peer");
        int attempts = 0;
        int failureLogs = 0;

        for (int update = 1; update <= 10_000; update++)
        {
            deferred.Drain(
                exists: _ => true,
                execute: _ =>
                {
                    attempts++;
                    throw new InvalidOperationException("persistent failure");
                },
                onFirstFailure: (_, _) => failureLogs++);
        }

        Assert.Equal((162, 1, 1), (attempts, failureLogs, deferred.Count));
    }

    [Fact]
    public void DeferredDisconnectClearsWhenPeerDisappears()
    {
        var deferred = new NeonLetterDeferredDisconnects<string>();
        deferred.Schedule("peer");
        int attempts = 0;
        int failureLogs = 0;

        deferred.Drain(
            exists: _ => false,
            execute: _ => attempts++,
            onFirstFailure: (_, _) => failureLogs++);

        Assert.Equal((0, 0, 0), (deferred.Count, attempts, failureLogs));
    }

    [Fact]
    public void ScheduledDisconnectQuarantinesAcceptedPeerTraffic()
    {
        string[] peers = { "first", "middle", "third" };
        var deferred = new NeonLetterDeferredDisconnects<string>();
        var received = new List<string>();
        deferred.Schedule("middle");

        NeonLetterPeerDelivery.Deliver(
            peers,
            isAccepted: peer => deferred.AllowsAcceptedTraffic(
                peer,
                _ => true),
            send: received.Add,
            onFailure: (_, _) => { });
        bool receiveAllowed = deferred.AllowsAcceptedTraffic(
            "middle",
            _ => true);

        Assert.Equal(
            ("first,third", false, true),
            (
                string.Join(",", received),
                receiveAllowed,
                deferred.IsQuarantined("middle")));
    }

    [Fact]
    public void SessionCleanupClearsDisconnectQuarantineAndRetryState()
    {
        var deferred = new NeonLetterDeferredDisconnects<string>();
        deferred.Schedule("peer");
        int attempts = 0;
        int failureLogs = 0;
        deferred.Drain(
            exists: _ => true,
            execute: _ =>
            {
                attempts++;
                throw new InvalidOperationException("transient failure");
            },
            onFirstFailure: (_, _) => failureLogs++);

        deferred.Clear();
        bool quarantinedAfterCleanup = deferred.IsQuarantined("peer");
        deferred.Schedule("peer");
        deferred.Drain(
            exists: _ => true,
            execute: _ => attempts++,
            onFirstFailure: (_, _) => failureLogs++);

        Assert.Equal(
            (false, 2, 1, 0),
            (
                quarantinedAfterCleanup,
                attempts,
                failureLogs,
                deferred.Count));
    }

    [Fact]
    public void FatalRoleSetupFailureBlocksAdvanceAndSpawnUntilLifecycleReset()
    {
        var gate = new NeonLetterRoleSetupGate();
        int setupAttempts = 0;

        Exception? firstFailure = gate.TryRun(FailSetup);
        Exception? advanceFailure = gate.TryRun(FailSetup);
        Exception? spawnFailure = gate.TryRun(FailSetup);
        bool failedBeforeReset = gate.IsFailed;
        gate.Reset();
        Exception? afterResetFailure = gate.TryRun(() => setupAttempts++);

        Assert.Equal(
            (
                2,
                typeof(IOException),
                (Type?)null,
                (Type?)null,
                true,
                false,
                (Type?)null),
            (
                setupAttempts,
                firstFailure?.GetType(),
                advanceFailure?.GetType(),
                spawnFailure?.GetType(),
                failedBeforeReset,
                gate.IsFailed,
                afterResetFailure?.GetType()));

        void FailSetup()
        {
            setupAttempts++;
            throw new IOException("bundle unavailable");
        }
    }

    [Fact]
    public void RequestIdsAreNonzeroMonotonicAndReapplySupersedesPerEntity()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);

        NeonLetterApplyRequest<int> first =
            coordinator.Start(7, Red, nowSeconds: 0d);
        NeonLetterApplyRequest<int> second =
            coordinator.Start(7, Blue, nowSeconds: 1d);

        Assert.Equal(
            (1ul, 2ul, 1, 2ul),
            (
                first.RequestId,
                second.RequestId,
                coordinator.PendingCount,
                coordinator.ResolvePendingRequestId(7)));
    }

    [Theory]
    [InlineData((int)NeonLetterApplyStatus.Accepted)]
    [InlineData((int)NeonLetterApplyStatus.Rejected)]
    public void DelayedPreviousSessionResultCannotResolveNewRequest(
        int statusValue)
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        NeonLetterApplyRequest<int> previous =
            coordinator.Start(7, Red, nowSeconds: 0d);
        coordinator.Clear();
        NeonLetterApplyRequest<int> current =
            coordinator.Start(7, Blue, nowSeconds: 1d);

        NeonLetterClientApplyDecision<int> delayed =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                previous.RequestId,
                7,
                (NeonLetterApplyStatus)statusValue,
                Red,
                Revision: 1));

        Assert.Equal(
            (1ul, 2ul, NeonLetterClientApplyAction.Ignored, 1, 2ul),
            (
                previous.RequestId,
                current.RequestId,
                delayed.Action,
                coordinator.PendingCount,
                coordinator.ResolvePendingRequestId(7)));
    }

    [Fact]
    public void RepeatedCleanupPreservesRequestIdentityAndEmptySessionState()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.SeedAuthoritative(7, Red);
        coordinator.Start(7, Blue, nowSeconds: 0d);

        coordinator.Clear();
        coordinator.Clear();
        NeonLetterApplyRequest<int> current =
            coordinator.Start(7, Green, nowSeconds: 1d);
        NeonLetterAuthoritativeColor authoritative =
            coordinator.ResolveAuthoritative(7);

        Assert.Equal(
            (2ul, 1, 2ul, NeonRgba.ProjectCyan, 0ul),
            (
                current.RequestId,
                coordinator.PendingCount,
                coordinator.ResolvePendingRequestId(7),
                authoritative.Color,
                authoritative.Revision));
    }

    [Fact]
    public void RequestIdExhaustionFailsClosedAcrossCleanup()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d,
            firstRequestId: ulong.MaxValue);
        NeonLetterApplyRequest<int> last =
            coordinator.Start(7, Red, nowSeconds: 0d);

        Exception? beforeCleanup = Record.Exception(
            () => coordinator.Start(8, Blue, nowSeconds: 1d));
        coordinator.Clear();
        Exception? afterCleanup = Record.Exception(
            () => coordinator.Start(9, Green, nowSeconds: 2d));

        Assert.Equal(
            (
                ulong.MaxValue,
                typeof(InvalidOperationException),
                typeof(InvalidOperationException)),
            (
                last.RequestId,
                beforeCleanup?.GetType(),
                afterCleanup?.GetType()));
    }

    [Fact]
    public void MatchingAcceptConfirmsAndStaleSupersededResultIsIgnored()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        NeonLetterApplyRequest<int> first =
            coordinator.Start(7, Red, nowSeconds: 0d);
        NeonLetterApplyRequest<int> second =
            coordinator.Start(7, Blue, nowSeconds: 1d);

        NeonLetterClientApplyDecision<int> stale =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                first.RequestId,
                7,
                NeonLetterApplyStatus.Accepted,
                Red,
                Revision: 1));
        NeonLetterClientApplyDecision<int> accepted =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                second.RequestId,
                7,
                NeonLetterApplyStatus.Accepted,
                Blue,
                Revision: 2));

        Assert.Equal(
            (NeonLetterClientApplyAction.Ignored,
                NeonLetterClientApplyAction.Confirm,
                Blue,
                0),
            (stale.Action, accepted.Action, accepted.Color, coordinator.PendingCount));
    }

    [Fact]
    public void RejectionRollsBackTheLatestLiveAuthoritativeColor()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.AcceptLive(7, Red, revision: 1);
        NeonLetterApplyRequest<int> request =
            coordinator.Start(7, Blue, nowSeconds: 0d);
        coordinator.AcceptLive(7, Green, revision: 2);

        NeonLetterClientApplyDecision<int> decision =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                request.RequestId,
                7,
                NeonLetterApplyStatus.Rejected,
                Red,
                Revision: 1));

        Assert.Equal(
            (NeonLetterClientApplyAction.Rollback, Green, 2ul),
            (decision.Action, decision.Color, decision.Revision));
    }

    [Fact]
    public void TimeoutRollsBackTheLatestAuthoritativeColor()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.AcceptLive(7, Red, revision: 1);
        coordinator.Start(7, Blue, nowSeconds: 0d);
        coordinator.AcceptLive(7, Green, revision: 2);

        IReadOnlyList<NeonLetterClientApplyDecision<int>> before =
            coordinator.RejectTimedOut(5d - 0.000_001d);
        IReadOnlyList<NeonLetterClientApplyDecision<int>> at =
            coordinator.RejectTimedOut(5d);

        Assert.Equal(
            (0, 1, NeonLetterClientApplyAction.Rollback, Green),
            (before.Count, at.Count, at.Single().Action, at.Single().Color));
    }

    [Fact]
    public void LiveStateUsesRevisionOrderingAndUpdatesRollbackTruth()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);

        NeonLetterClientApplyDecision<int> current =
            coordinator.AcceptLive(7, Green, revision: 2);
        NeonLetterClientApplyDecision<int> stale =
            coordinator.AcceptLive(7, Red, revision: 1);
        NeonLetterAuthoritativeColor resolved =
            coordinator.ResolveAuthoritative(7);

        Assert.Equal(
            (NeonLetterClientApplyAction.ApplyAuthoritative,
                NeonLetterClientApplyAction.Ignored,
                Green,
                2ul),
            (current.Action, stale.Action, resolved.Color, resolved.Revision));
    }

    [Fact]
    public void HostDeduplicatesARequestAndReturnsTheExactCachedResult()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);

        NeonLetterHostApplyOutcome<int> first = coordinator.Process(
            "peer",
            requestId: 11,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Red);
        NeonLetterHostApplyOutcome<int> repeated = coordinator.Process(
            "peer",
            requestId: 11,
            identity: 8,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[1].RecipeId,
            Blue);

        Assert.Equal(
            (first.Result, false, 1ul, NeonRgba.ProjectCyan),
            (
                repeated.Result,
                repeated.ShouldBroadcast,
                authoritative.ResolveState(7).Revision,
                authoritative.Resolve(8)));
    }

    [Fact]
    public void HostRejectsAnEvictedReplayWithoutMutatingAuthoritativeState()
    {
        const int capacity =
            NeonLetterHostApplyProtocol.MaxCachedRequestsPerPeer;
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);
        for (ulong requestId = 1;
             requestId <= (ulong)capacity + 1;
             requestId++)
        {
            coordinator.Process(
                "peer",
                requestId,
                identity: 7,
                isHost: true,
                isLive: true,
                recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
                Red);
        }

        bool replayDetected = coordinator.TryResolveReplay(
            "peer",
            requestId: 1,
            identity: 7,
            out NeonLetterApplyResult<int> staleResult);
        NeonLetterHostApplyOutcome<int> replayed = coordinator.Process(
            "peer",
            requestId: 1,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Blue);

        Assert.Equal(
            (
                true,
                NeonLetterApplyStatus.Rejected,
                NeonLetterApplyStatus.Rejected,
                false,
                (ulong)capacity + 1,
                Red),
            (
                replayDetected,
                staleResult.Status,
                replayed.Result.Status,
                replayed.ShouldBroadcast,
                replayed.Result.Revision,
                authoritative.Resolve(7)));
    }

    [Fact]
    public void HostRejectsNonMonotonicRequestAndAcceptsNextHigherId()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);
        coordinator.Process(
            "peer",
            requestId: 10,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Red);

        NeonLetterHostApplyOutcome<int> stale = coordinator.Process(
            "peer",
            requestId: 9,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Blue);
        NeonLetterHostApplyOutcome<int> next = coordinator.Process(
            "peer",
            requestId: 11,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Green);

        Assert.Equal(
            (
                NeonLetterApplyStatus.Rejected,
                false,
                1ul,
                NeonLetterApplyStatus.Accepted,
                true,
                2ul,
                Green),
            (
                stale.Result.Status,
                stale.ShouldBroadcast,
                stale.Result.Revision,
                next.Result.Status,
                next.ShouldBroadcast,
                next.Result.Revision,
                authoritative.Resolve(7)));
    }

    [Fact]
    public void HostRequestWatermarkHandlesUlongMaximumWithoutWrapping()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);
        coordinator.Process(
            "peer",
            requestId: ulong.MaxValue - 1,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Red);
        NeonLetterHostApplyOutcome<int> maximum = coordinator.Process(
            "peer",
            requestId: ulong.MaxValue,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Blue);

        NeonLetterHostApplyOutcome<int> lower = coordinator.Process(
            "peer",
            requestId: ulong.MaxValue - 2,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Green);
        NeonLetterHostApplyOutcome<int> duplicateMaximum = coordinator.Process(
            "peer",
            requestId: ulong.MaxValue,
            identity: 8,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[1].RecipeId,
            Green);

        Assert.Equal(
            (
                NeonLetterApplyStatus.Accepted,
                2ul,
                NeonLetterApplyStatus.Rejected,
                false,
                maximum.Result,
                false,
                Blue),
            (
                maximum.Result.Status,
                maximum.Result.Revision,
                lower.Result.Status,
                lower.ShouldBroadcast,
                duplicateMaximum.Result,
                duplicateMaximum.ShouldBroadcast,
                authoritative.Resolve(7)));
    }

    [Fact]
    public void HostDedupeKeepsRecentDuplicatesAndClearsOnePeer()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);
        NeonLetterHostApplyOutcome<int> first = coordinator.Process(
            "peer",
            requestId: 1,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Red);
        NeonLetterHostApplyOutcome<int> duplicate = coordinator.Process(
            "peer",
            requestId: 1,
            identity: 8,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[1].RecipeId,
            Blue);
        coordinator.Remove("peer");
        NeonLetterHostApplyOutcome<int> afterCleanup = coordinator.Process(
            "peer",
            requestId: 1,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Green);

        Assert.Equal(
            (first.Result, false, true, 2ul),
            (
                duplicate.Result,
                duplicate.ShouldBroadcast,
                afterCleanup.ShouldBroadcast,
                afterCleanup.Result.Revision));
    }

    [Fact]
    public void AuthoritativeRevisionsAdvanceIndependentlyPerEntity()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();

        NeonLetterColorAcceptance firstA = authoritative.TryAccept(
            isHost: true,
            identity: 7,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Red);
        NeonLetterColorAcceptance firstB = authoritative.TryAccept(
            isHost: true,
            identity: 8,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[1].RecipeId,
            Blue);
        NeonLetterColorAcceptance secondA = authoritative.TryAccept(
            isHost: true,
            identity: 7,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Green);
        NeonLetterColorAcceptance rejectedB = authoritative.TryAccept(
            isHost: true,
            identity: 8,
            isLive: false,
            recipeId: NeonLetterSmallCatalog.All[1].RecipeId,
            Red);

        Assert.Equal(
            (1ul, 1ul, 2ul, 1ul),
            (
                firstA.Revision,
                firstB.Revision,
                secondA.Revision,
                rejectedB.Revision));
    }

    [Fact]
    public void ColorAcceptanceSupportsTwoValueDeconstruction()
    {
        var acceptance = new NeonLetterColorAcceptance(true, Green, Revision: 7);

        (bool accepted, NeonRgba authoritativeColor) = acceptance;

        Assert.Equal((true, Green), (accepted, authoritativeColor));
    }

    [Fact]
    public void RejectedHostMutationDoesNotIncrementRevisionOrBroadcast()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);
        coordinator.Process(
            "peer",
            requestId: 10,
            identity: 7,
            isHost: true,
            isLive: true,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Red);

        NeonLetterHostApplyOutcome<int> outcome = coordinator.Process(
            "peer",
            requestId: 11,
            identity: 7,
            isHost: true,
            isLive: false,
            recipeId: NeonLetterSmallCatalog.All[0].RecipeId,
            Red);

        Assert.Equal(
            (NeonLetterApplyStatus.Rejected, 1ul, Red, false),
            (
                outcome.Result.Status,
                outcome.Result.Revision,
                outcome.Result.AuthoritativeColor,
                outcome.ShouldBroadcast));
    }

    private static NeonLetterSessionIdentity CreateIdentity()
    {
        return new NeonLetterSessionIdentity(
            "0.3.1",
            NeonLetterNetworkProtocol.CurrentVersion,
            Digest(11),
            Digest(22));
    }

    private static NeonLetterSha256Digest Digest(byte value)
    {
        return NeonLetterSha256Digest.FromBytes(
            Enumerable.Repeat(value, NeonLetterSha256Digest.ByteCount).ToArray());
    }

    private static NeonLetterHandshakeStatus ExpectedStatus(
        HandshakeDifference difference)
    {
        return difference switch
        {
            HandshakeDifference.ReleaseVersion =>
                NeonLetterHandshakeStatus.ReleaseVersionMismatch,
            HandshakeDifference.ColorProtocol =>
                NeonLetterHandshakeStatus.ColorProtocolMismatch,
            HandshakeDifference.Catalog =>
                NeonLetterHandshakeStatus.CatalogMismatch,
            HandshakeDifference.Bundle =>
                NeonLetterHandshakeStatus.BundleMismatch,
            _ => NeonLetterHandshakeStatus.Accepted
        };
    }

    public enum HandshakeDifference
    {
        ReleaseVersion,
        ColorProtocol,
        Catalog,
        Bundle
    }
}
