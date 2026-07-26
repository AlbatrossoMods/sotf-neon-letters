using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SOTFNeonLetters;
using Xunit;

public sealed class SessionMutationBehaviorTests
{
    private static readonly NeonRgba Red = new(1f, 0f, 0f, 1f);
    private static readonly NeonRgba Green = new(0f, 1f, 0f, 1f);
    private static readonly NeonRgba Blue = new(0f, 0f, 1f, 1f);

    [Fact]
    public void SessionAcceptanceAndRejectionControlTrafficExecution()
    {
        var session = new NeonLetterClientSessionGate();
        int actionCount = 0;

        bool beforeAcceptance = session.TryRun(() => actionCount++);
        session.Accept();
        bool afterAcceptance = session.TryRun(() => actionCount++);
        session.Reject();
        bool afterRejection = session.TryRun(() => actionCount++);
        bool tryAccepted = session.TryAccept(canAccept: true);
        bool afterTryAcceptance = session.TryRun(() => actionCount++);
        bool tryRejected = session.TryAccept(canAccept: false);
        bool afterTryRejection = session.TryRun(() => actionCount++);

        Assert.Equal(
            (false, true, false, true, true, false, false, 2),
            (
                beforeAcceptance,
                afterAcceptance,
                afterRejection,
                tryAccepted,
                afterTryAcceptance,
                tryRejected,
                afterTryRejection,
                actionCount));
    }

    [Fact]
    public void AcceptedHandshakeCannotBeReplacedByALaterMismatch()
    {
        NeonLetterSessionIdentity expected = CreateIdentity();
        var registry = CreateRegistry(expected);
        registry.Observe("peer", nowSeconds: 10d);
        NeonLetterHandshakeStatus accepted = registry.AcceptHello(
            "peer",
            NeonLetterHandshakeHello.Create(helloId: 1, expected));
        NeonLetterSessionIdentity mismatch =
            expected with { BundleHash = Digest(seed: 90) };

        NeonLetterHandshakeStatus repeated = registry.AcceptHello(
            "peer",
            NeonLetterHandshakeHello.Create(helloId: 2, mismatch));

        Assert.Equal(
            (
                NeonLetterHandshakeStatus.Accepted,
                NeonLetterHandshakeStatus.Accepted,
                NeonLetterPeerState.Accepted,
                true),
            (
                accepted,
                repeated,
                registry.GetState("peer"),
                registry.IsAccepted("peer")));
    }

    [Fact]
    public void RejectedHandshakeCannotBeReplacedByALaterMatch()
    {
        NeonLetterSessionIdentity expected = CreateIdentity();
        var registry = CreateRegistry(expected);
        registry.Observe("peer", nowSeconds: 10d);
        NeonLetterSessionIdentity mismatch =
            expected with { CatalogHash = Digest(seed: 80) };
        NeonLetterHandshakeStatus rejected = registry.AcceptHello(
            "peer",
            NeonLetterHandshakeHello.Create(helloId: 1, mismatch));

        NeonLetterHandshakeStatus repeated = registry.AcceptHello(
            "peer",
            NeonLetterHandshakeHello.Create(helloId: 2, expected));

        Assert.Equal(
            (
                NeonLetterHandshakeStatus.CatalogMismatch,
                NeonLetterHandshakeStatus.CatalogMismatch,
                NeonLetterPeerState.Rejected,
                false),
            (
                rejected,
                repeated,
                registry.GetState("peer"),
                registry.IsAccepted("peer")));
    }

    [Fact]
    public void RemovedHandshakePeerReturnsToMissingUnknownState()
    {
        NeonLetterSessionIdentity identity = CreateIdentity();
        var registry = CreateRegistry(identity);
        registry.Observe("peer", nowSeconds: 0d);
        registry.AcceptHello(
            "peer",
            NeonLetterHandshakeHello.Create(helloId: 1, identity));

        registry.Remove("peer");

        Assert.Equal(
            (
                0,
                NeonLetterPeerState.Unknown,
                NeonLetterHandshakeStatus.MissingHello,
                false),
            (
                registry.Count,
                registry.GetState("peer"),
                registry.GetStatus("peer"),
                registry.IsAccepted("peer")));
    }

    [Fact]
    public void HelloSchedulerOnlySendsWithinAStartedSession()
    {
        var scheduler = new NeonLetterHelloScheduler(
            resendIntervalSeconds: 1d,
            timeoutSeconds: 5d);
        bool beforeStart = scheduler.ShouldSend(nowSeconds: 0d);
        Exception? markBeforeStart = Record.Exception(
            () => scheduler.MarkSent(nowSeconds: 0d));
        scheduler.Start(nowSeconds: 10d);
        bool immediate = scheduler.ShouldSend(nowSeconds: 10d);
        scheduler.MarkSent(nowSeconds: 10d);
        bool resend = scheduler.ShouldSend(nowSeconds: 11d);
        bool sendAtTimeout = scheduler.ShouldSend(nowSeconds: 15d);
        bool timedOut = scheduler.HasTimedOut(nowSeconds: 15d);

        scheduler.Clear();

        Assert.Equal(
            (
                false,
                typeof(InvalidOperationException),
                true,
                true,
                false,
                true,
                false,
                false),
            (
                beforeStart,
                markBeforeStart?.GetType(),
                immediate,
                resend,
                sendAtTimeout,
                timedOut,
                scheduler.ShouldSend(nowSeconds: 0d),
                scheduler.HasTimedOut(nowSeconds: 5d)));
    }

    [Fact]
    public void HelloSchedulerRequiresResendBeforeTimeout()
    {
        Exception? error = Record.Exception(
            () => new NeonLetterHelloScheduler(
                resendIntervalSeconds: 5d,
                timeoutSeconds: 5d));

        Assert.Equal(typeof(ArgumentOutOfRangeException), error?.GetType());
    }

    [Fact]
    public void DuplicateDisconnectSchedulingKeepsOneQuarantineEntry()
    {
        var disconnects = new NeonLetterDeferredDisconnects<string>();

        disconnects.Schedule("peer");
        disconnects.Schedule("peer");

        Assert.Equal(
            (1, true, false),
            (
                disconnects.Count,
                disconnects.IsQuarantined("peer"),
                disconnects.AllowsAcceptedTraffic("peer", _ => true)));
    }

    [Fact]
    public void RemovingDisconnectReleasesQuarantineAndAllowsRescheduling()
    {
        var disconnects = new NeonLetterDeferredDisconnects<string>();
        disconnects.Schedule("peer");

        disconnects.Remove("peer");
        bool quarantinedAfterRemoval =
            disconnects.IsQuarantined("peer");
        bool trafficAllowed = disconnects.AllowsAcceptedTraffic(
            "peer",
            _ => true);
        disconnects.Schedule("peer");

        Assert.Equal(
            (false, true, 1, true),
            (
                quarantinedAfterRemoval,
                trafficAllowed,
                disconnects.Count,
                disconnects.IsQuarantined("peer")));
    }

    [Fact]
    public void DigestRoundTripsEveryWordUsingLittleEndianEncoding()
    {
        byte[] bytes = Enumerable.Range(0, NeonLetterSha256Digest.ByteCount)
            .Select(index => (byte)(index * 7 + 3))
            .ToArray();
        NeonLetterSha256Digest digest =
            NeonLetterSha256Digest.FromBytes(bytes);
        string expectedWords = string.Join(
            ",",
            Enumerable.Range(0, NeonLetterSha256Digest.WordCount)
                .Select(index => BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(
                        index * sizeof(uint),
                        sizeof(uint)))));

        Assert.Equal(
            (Convert.ToHexString(bytes), expectedWords),
            (
                Convert.ToHexString(digest.ToByteArray()),
                string.Join(
                    ",",
                    Enumerable.Range(
                            0,
                            NeonLetterSha256Digest.WordCount)
                        .Select(digest.GetWord))));
    }

    [Fact]
    public void CatalogHashMatchesTheCanonicalBinaryEncoding()
    {
        var entries = new[]
        {
            new NeonLetterCatalogIdentityEntry(
                CatalogIndex: 3,
                RecipeId: 101,
                CraftingNodeId: 100,
                AssetKey: "Neon-A",
                PrefabAssetName: "Prefab_Å"),
            new NeonLetterCatalogIdentityEntry(
                CatalogIndex: 7,
                RecipeId: 103,
                CraftingNodeId: 102,
                AssetKey: "Neon-B",
                PrefabAssetName: "Prefab_ß")
        };

        NeonLetterSha256Digest actual =
            NeonLetterSessionIdentityHasher.ComputeCatalogHash(entries);

        Assert.Equal(ComputeCanonicalCatalogHash(entries), actual);
    }

    [Fact]
    public void HostRejectsZeroRequestWithoutCachingOrChangingState()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);

        NeonLetterHostApplyOutcome<int> outcome = coordinator.Process(
            "peer",
            requestId: 0,
            identity: 7,
            isHost: true,
            isLive: true,
            KnownRecipeId,
            Red);
        bool replayed = coordinator.TryResolveReplay(
            "peer",
            requestId: 0,
            identity: 7,
            out _);
        NeonLetterAuthoritativeColor state =
            authoritative.ResolveState(7);

        Assert.Equal(
            (
                0ul,
                7,
                NeonLetterApplyStatus.Rejected,
                NeonRgba.ProjectCyan,
                0ul,
                false,
                false,
                NeonRgba.ProjectCyan,
                0ul),
            (
                outcome.Result.RequestId,
                outcome.Result.Identity,
                outcome.Result.Status,
                outcome.Result.AuthoritativeColor,
                outcome.Result.Revision,
                outcome.ShouldBroadcast,
                replayed,
                state.Color,
                state.Revision));
    }

    [Fact]
    public void HostCacheRetainsExactCapacityAndOnlyThenEvictsOldest()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);
        FillHostCacheToCapacity(coordinator);

        bool retainedAtCapacity = coordinator.TryResolveReplay(
            "peer",
            requestId: 1,
            identity: 7,
            out NeonLetterApplyResult<int> retained);
        coordinator.Process(
            "peer",
            requestId:
                (ulong)NeonLetterHostApplyProtocol.MaxCachedRequestsPerPeer +
                1,
            identity: 7,
            isHost: true,
            isLive: true,
            KnownRecipeId,
            Blue);
        bool resolvedAfterOverflow = coordinator.TryResolveReplay(
            "peer",
            requestId: 1,
            identity: 7,
            out NeonLetterApplyResult<int> evicted);
        bool secondRetained = coordinator.TryResolveReplay(
            "peer",
            requestId: 2,
            identity: 7,
            out NeonLetterApplyResult<int> second);

        Assert.Equal(
            (
                true,
                NeonLetterApplyStatus.Accepted,
                true,
                NeonLetterApplyStatus.Rejected,
                true,
                NeonLetterApplyStatus.Accepted),
            (
                retainedAtCapacity,
                retained.Status,
                resolvedAfterOverflow,
                evicted.Status,
                secondRetained,
                second.Status));
    }

    [Fact]
    public void ClearingHostCacheAllowsSameRequestToBeProcessedAgain()
    {
        var authoritative = new NeonLetterAuthoritativeColors<int>();
        var coordinator =
            new NeonLetterHostApplyCoordinator<string, int>(authoritative);
        coordinator.Process(
            "peer",
            requestId: 1,
            identity: 7,
            isHost: true,
            isLive: true,
            KnownRecipeId,
            Red);

        coordinator.Clear();
        NeonLetterHostApplyOutcome<int> repeated = coordinator.Process(
            "peer",
            requestId: 1,
            identity: 7,
            isHost: true,
            isLive: true,
            KnownRecipeId,
            Green);
        NeonLetterAuthoritativeColor state =
            authoritative.ResolveState(7);

        Assert.Equal(
            (
                NeonLetterApplyStatus.Accepted,
                true,
                Green,
                2ul),
            (
                repeated.Result.Status,
                repeated.ShouldBroadcast,
                state.Color,
                state.Revision));
    }

    [Fact]
    public void EqualLiveRevisionIsIgnoredAndPreservesAuthoritativeState()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.AcceptLive(7, Green, revision: 5);

        NeonLetterClientApplyDecision<int> decision =
            coordinator.AcceptLive(7, Red, revision: 5);
        NeonLetterAuthoritativeColor state =
            coordinator.ResolveAuthoritative(7);

        Assert.Equal(
            (
                NeonLetterClientApplyAction.Ignored,
                7,
                Green,
                5ul),
            (
                decision.Action,
                decision.Identity,
                state.Color,
                state.Revision));
    }

    [Fact]
    public void NewerMatchingResultReplacesRollbackTruth()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.AcceptLive(7, Green, revision: 2);
        NeonLetterApplyRequest<int> request =
            coordinator.Start(7, Blue, nowSeconds: 0d);

        NeonLetterClientApplyDecision<int> decision =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                request.RequestId,
                7,
                NeonLetterApplyStatus.Rejected,
                Red,
                Revision: 3));
        NeonLetterAuthoritativeColor state =
            coordinator.ResolveAuthoritative(7);

        Assert.Equal(
            (
                NeonLetterClientApplyAction.Rollback,
                Red,
                3ul,
                Red,
                3ul),
            (
                decision.Action,
                decision.Color,
                decision.Revision,
                state.Color,
                state.Revision));
    }

    [Fact]
    public void EqualResultRevisionCannotReplaceRollbackTruth()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.AcceptLive(7, Green, revision: 2);
        NeonLetterApplyRequest<int> request =
            coordinator.Start(7, Blue, nowSeconds: 0d);

        NeonLetterClientApplyDecision<int> decision =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                request.RequestId,
                7,
                NeonLetterApplyStatus.Rejected,
                Red,
                Revision: 2));
        NeonLetterAuthoritativeColor state =
            coordinator.ResolveAuthoritative(7);

        Assert.Equal(
            (
                NeonLetterClientApplyAction.Rollback,
                Green,
                2ul,
                Green,
                2ul),
            (
                decision.Action,
                decision.Color,
                decision.Revision,
                state.Color,
                state.Revision));
    }

    [Fact]
    public void IgnoredSupersededResultPreservesWireIdentity()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        NeonLetterApplyRequest<int> superseded =
            coordinator.Start(7, Red, nowSeconds: 0d);
        NeonLetterApplyRequest<int> current =
            coordinator.Start(7, Blue, nowSeconds: 1d);

        NeonLetterClientApplyDecision<int> decision =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                superseded.RequestId,
                superseded.Identity,
                NeonLetterApplyStatus.Accepted,
                Red,
                Revision: 1));

        Assert.Equal(
            (
                NeonLetterClientApplyAction.Ignored,
                superseded.RequestId,
                superseded.Identity,
                1,
                current.RequestId),
            (
                decision.Action,
                decision.RequestId,
                decision.Identity,
                coordinator.PendingCount,
                coordinator.ResolvePendingRequestId(7)));
    }

    [Fact]
    public void TimedOutRequestRollsBackExactlyOnce()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.AcceptLive(7, Green, revision: 2);
        NeonLetterApplyRequest<int> request =
            coordinator.Start(7, Blue, nowSeconds: 0d);

        IReadOnlyList<NeonLetterClientApplyDecision<int>> first =
            coordinator.RejectTimedOut(nowSeconds: 5d);
        IReadOnlyList<NeonLetterClientApplyDecision<int>> repeated =
            coordinator.RejectTimedOut(nowSeconds: 5d);
        NeonLetterClientApplyDecision<int> rollback = first.Single();

        Assert.Equal(
            (
                1,
                0,
                0,
                NeonLetterClientApplyAction.Rollback,
                request.RequestId,
                7,
                Green,
                2ul),
            (
                first.Count,
                repeated.Count,
                coordinator.PendingCount,
                rollback.Action,
                rollback.RequestId,
                rollback.Identity,
                rollback.Color,
                rollback.Revision));
    }

    [Fact]
    public void ClientClearDiscardsStateWithoutReusingRequestIdentity()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.AcceptLive(7, Green, revision: 2);
        NeonLetterApplyRequest<int> previous =
            coordinator.Start(7, Blue, nowSeconds: 0d);

        coordinator.Clear();
        NeonLetterClientApplyDecision<int> delayed =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                previous.RequestId,
                7,
                NeonLetterApplyStatus.Accepted,
                Red,
                Revision: 3));
        NeonLetterApplyRequest<int> current =
            coordinator.Start(7, Blue, nowSeconds: 1d);
        NeonLetterAuthoritativeColor state =
            coordinator.ResolveAuthoritative(7);

        Assert.Equal(
            (
                NeonLetterClientApplyAction.Ignored,
                previous.RequestId + 1,
                NeonRgba.ProjectCyan,
                0ul,
                1,
                current.RequestId),
            (
                delayed.Action,
                current.RequestId,
                state.Color,
                state.Revision,
                coordinator.PendingCount,
                coordinator.ResolvePendingRequestId(7)));
    }

    [Fact]
    public void ClientRemoveDiscardsOnlyTheSelectedIdentityState()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.AcceptLive(7, Red, revision: 1);
        coordinator.AcceptLive(8, Green, revision: 2);
        NeonLetterApplyRequest<int> removed =
            coordinator.Start(7, Blue, nowSeconds: 0d);
        NeonLetterApplyRequest<int> retained =
            coordinator.Start(8, Blue, nowSeconds: 0d);

        coordinator.Remove(7);
        NeonLetterClientApplyDecision<int> delayed =
            coordinator.AcceptResult(new NeonLetterApplyResult<int>(
                removed.RequestId,
                7,
                NeonLetterApplyStatus.Accepted,
                Blue,
                Revision: 3));
        NeonLetterAuthoritativeColor removedState =
            coordinator.ResolveAuthoritative(7);
        NeonLetterAuthoritativeColor retainedState =
            coordinator.ResolveAuthoritative(8);

        Assert.Equal(
            (
                NeonLetterClientApplyAction.Ignored,
                NeonRgba.ProjectCyan,
                0ul,
                retained.RequestId,
                Green,
                2ul,
                1),
            (
                delayed.Action,
                removedState.Color,
                removedState.Revision,
                coordinator.ResolvePendingRequestId(8),
                retainedState.Color,
                retainedState.Revision,
                coordinator.PendingCount));
    }

    private static int KnownRecipeId =>
        NeonLetterSmallCatalog.All[0].RecipeId;

    private static NeonLetterHandshakeRegistry<string> CreateRegistry(
        NeonLetterSessionIdentity expected)
    {
        return new NeonLetterHandshakeRegistry<string>(
            expected,
            NeonLetterSessionProtocol.NegotiationTimeoutSeconds);
    }

    private static NeonLetterSessionIdentity CreateIdentity()
    {
        return new NeonLetterSessionIdentity(
            "0.3.3",
            NeonLetterNetworkProtocol.CurrentVersion,
            Digest(seed: 1),
            Digest(seed: 40));
    }

    private static NeonLetterSha256Digest Digest(byte seed)
    {
        byte[] bytes = Enumerable.Range(0, NeonLetterSha256Digest.ByteCount)
            .Select(index => unchecked((byte)(seed + index)))
            .ToArray();
        return NeonLetterSha256Digest.FromBytes(bytes);
    }

    private static NeonLetterSha256Digest ComputeCanonicalCatalogHash(
        IReadOnlyList<NeonLetterCatalogIdentityEntry> entries)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        AppendInt32(hash, entries.Count);
        foreach (NeonLetterCatalogIdentityEntry entry in entries)
        {
            AppendInt32(hash, entry.CatalogIndex);
            AppendInt32(hash, entry.RecipeId);
            AppendInt32(hash, entry.CraftingNodeId);
            AppendString(hash, entry.AssetKey);
            AppendString(hash, entry.PrefabAssetName);
        }

        return NeonLetterSha256Digest.FromBytes(hash.GetHashAndReset());
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void FillHostCacheToCapacity(
        NeonLetterHostApplyCoordinator<string, int> coordinator)
    {
        for (ulong requestId = 1;
             requestId <=
                (ulong)NeonLetterHostApplyProtocol
                    .MaxCachedRequestsPerPeer;
             requestId++)
        {
            coordinator.Process(
                "peer",
                requestId,
                identity: 7,
                isHost: true,
                isLive: true,
                KnownRecipeId,
                Red);
        }
    }
}
