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
