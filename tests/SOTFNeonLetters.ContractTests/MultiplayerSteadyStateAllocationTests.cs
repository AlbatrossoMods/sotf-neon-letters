using SOTFNeonLetters;
using Xunit;

public sealed class MultiplayerSteadyStateAllocationTests
{
    private const int MeasurementIterations = 256;

    [Fact]
    public void WarmHandshakeExpiryChecksAllocateNoBytes()
    {
        var registry = new NeonLetterHandshakeRegistry<int>(
            CreateSessionIdentity(),
            timeoutSeconds: 5d);
        registry.Observe(peer: 7, nowSeconds: 0d);
        Action<int> ignoreExpiredPeer = IgnoreInt32;

        for (int iteration = 0; iteration < 8; iteration++)
        {
            _ = registry.RejectExpiredUnknown(nowSeconds: 1d);
            registry.DrainExpiredUnknown(
                nowSeconds: 1d,
                ignoreExpiredPeer);
        }

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0;
             iteration < MeasurementIterations;
             iteration++)
        {
            _ = registry.RejectExpiredUnknown(nowSeconds: 1d);
            registry.DrainExpiredUnknown(
                nowSeconds: 1d,
                ignoreExpiredPeer);
        }

        long allocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.Equal(0, allocatedBytes);
        GC.KeepAlive(registry);
        GC.KeepAlive(ignoreExpiredPeer);
    }

    [Fact]
    public void WarmClientTimeoutChecksAllocateNoBytes()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.Start(7, NeonRgba.ProjectCyan, nowSeconds: 0d);
        Action<NeonLetterClientApplyDecision<int>> ignoreTimedOutDecision =
            IgnoreClientDecision;

        for (int iteration = 0; iteration < 8; iteration++)
        {
            _ = coordinator.RejectTimedOut(nowSeconds: 1d);
            coordinator.DrainTimedOut(
                nowSeconds: 1d,
                ignoreTimedOutDecision);
        }

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0;
             iteration < MeasurementIterations;
             iteration++)
        {
            _ = coordinator.RejectTimedOut(nowSeconds: 1d);
            coordinator.DrainTimedOut(
                nowSeconds: 1d,
                ignoreTimedOutDecision);
        }

        long allocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.Equal(0, allocatedBytes);
        GC.KeepAlive(coordinator);
        GC.KeepAlive(ignoreTimedOutDecision);
    }

    [Fact]
    public void HandshakeExpirySnapshotRemainsStableAfterRegistryChanges()
    {
        var registry = new NeonLetterHandshakeRegistry<int>(
            CreateSessionIdentity(),
            timeoutSeconds: 5d);
        registry.Observe(peer: 7, nowSeconds: 0d);
        registry.Observe(peer: 8, nowSeconds: 0d);

        int[] expiredPeers =
            registry.RejectExpiredUnknown(nowSeconds: 5d);
        registry.Clear();
        registry.Observe(peer: 9, nowSeconds: 5d);

        Assert.Equal(new[] { 7, 8 }, expiredPeers);
    }

    [Fact]
    public void HandshakeExpiryDrainUsesTheExactDeadlineAndObservationOrder()
    {
        var registry = new NeonLetterHandshakeRegistry<int>(
            CreateSessionIdentity(),
            timeoutSeconds: 5d);
        registry.Observe(peer: 7, nowSeconds: 10d);
        registry.Observe(peer: 8, nowSeconds: 10d);
        var expiredPeers = new List<int>();

        int beforeDeadline = registry.DrainExpiredUnknown(
            nowSeconds: 14.999_999d,
            expiredPeers.Add);
        int atDeadline = registry.DrainExpiredUnknown(
            nowSeconds: 15d,
            expiredPeers.Add);

        Assert.Equal(
            new
            {
                BeforeDeadline = 0,
                AtDeadline = 2,
                ExpiredPeers = "7,8",
                FirstState = NeonLetterPeerState.Rejected,
                SecondState = NeonLetterPeerState.Rejected
            },
            new
            {
                BeforeDeadline = beforeDeadline,
                AtDeadline = atDeadline,
                ExpiredPeers = string.Join(",", expiredPeers),
                FirstState = registry.GetState(7),
                SecondState = registry.GetState(8)
            });
    }

    [Fact]
    public void HandshakeExpiryDrainMarksTheBatchBeforeReentrantReset()
    {
        var registry = new NeonLetterHandshakeRegistry<int>(
            CreateSessionIdentity(),
            timeoutSeconds: 5d);
        registry.Observe(peer: 7, nowSeconds: 0d);
        registry.Observe(peer: 8, nowSeconds: 0d);
        var expiredPeers = new List<int>();
        NeonLetterPeerState secondStateAtFirstCallback =
            NeonLetterPeerState.Unknown;
        int nestedDrainCount = -1;

        int drainedCount = registry.DrainExpiredUnknown(
            nowSeconds: 5d,
            peer =>
            {
                expiredPeers.Add(peer);
                if (peer != 7)
                {
                    return;
                }

                secondStateAtFirstCallback = registry.GetState(8);
                registry.Clear();
                registry.Observe(peer: 8, nowSeconds: 0d);
                nestedDrainCount = registry.DrainExpiredUnknown(
                    nowSeconds: 5d,
                    expiredPeers.Add);
            });

        Assert.Equal(
            new
            {
                DrainedCount = 2,
                ExpiredPeers = "7,8",
                SecondStateAtFirstCallback =
                    NeonLetterPeerState.Rejected,
                NestedDrainCount = 0,
                RemainingCount = 1,
                ReplacementState = NeonLetterPeerState.Unknown
            },
            new
            {
                DrainedCount = drainedCount,
                ExpiredPeers = string.Join(",", expiredPeers),
                SecondStateAtFirstCallback =
                    secondStateAtFirstCallback,
                NestedDrainCount = nestedDrainCount,
                RemainingCount = registry.Count,
                ReplacementState = registry.GetState(8)
            });
    }

    [Fact]
    public void ClientTimeoutDrainUsesTheExactDeadlineAndRequestOrder()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        coordinator.Start(7, NeonRgba.ProjectCyan, nowSeconds: 10d);
        coordinator.Start(8, NeonRgba.ProjectCyan, nowSeconds: 10d);
        var timedOutIdentities = new List<int>();

        int beforeDeadline = coordinator.DrainTimedOut(
            nowSeconds: 14.999_999d,
            decision => timedOutIdentities.Add(decision.Identity));
        int atDeadline = coordinator.DrainTimedOut(
            nowSeconds: 15d,
            decision => timedOutIdentities.Add(decision.Identity));

        Assert.Equal(
            new
            {
                BeforeDeadline = 0,
                AtDeadline = 2,
                TimedOutIdentities = "7,8",
                PendingCount = 0
            },
            new
            {
                BeforeDeadline = beforeDeadline,
                AtDeadline = atDeadline,
                TimedOutIdentities =
                    string.Join(",", timedOutIdentities),
                coordinator.PendingCount
            });
    }

    [Fact]
    public void ClientTimeoutDrainRemovesTheBatchBeforeReentrantReset()
    {
        var coordinator = new NeonLetterClientApplyCoordinator<int>(
            timeoutSeconds: 5d);
        NeonLetterApplyRequest<int> first =
            coordinator.Start(7, NeonRgba.ProjectCyan, nowSeconds: 0d);
        coordinator.Start(8, NeonRgba.ProjectCyan, nowSeconds: 0d);
        var timedOutIdentities = new List<int>();
        int pendingAtFirstCallback = -1;
        int nestedDrainCount = -1;
        ulong replacementRequestId = 0;

        int drainedCount = coordinator.DrainTimedOut(
            nowSeconds: 5d,
            decision =>
            {
                timedOutIdentities.Add(decision.Identity);
                if (decision.Identity != 7)
                {
                    return;
                }

                pendingAtFirstCallback = coordinator.PendingCount;
                coordinator.Clear();
                replacementRequestId = coordinator.Start(
                    7,
                    NeonRgba.ProjectCyan,
                    nowSeconds: 5d).RequestId;
                nestedDrainCount = coordinator.DrainTimedOut(
                    nowSeconds: 5d,
                    nested => timedOutIdentities.Add(nested.Identity));
            });

        Assert.Equal(
            new
            {
                DrainedCount = 2,
                TimedOutIdentities = "7,8",
                PendingAtFirstCallback = 0,
                NestedDrainCount = 0,
                PendingCount = 1,
                ReplacementSurvived = true
            },
            new
            {
                DrainedCount = drainedCount,
                TimedOutIdentities =
                    string.Join(",", timedOutIdentities),
                PendingAtFirstCallback = pendingAtFirstCallback,
                NestedDrainCount = nestedDrainCount,
                coordinator.PendingCount,
                ReplacementSurvived =
                    replacementRequestId != first.RequestId &&
                    coordinator.ResolvePendingRequestId(7) ==
                    replacementRequestId
            });
    }

    private static void IgnoreClientDecision(
        NeonLetterClientApplyDecision<int> _)
    {
    }

    private static void IgnoreInt32(int _)
    {
    }

    private static NeonLetterSessionIdentity CreateSessionIdentity()
    {
        return new NeonLetterSessionIdentity(
            "1.0.0",
            NeonLetterNetworkProtocol.CurrentVersion,
            default,
            default);
    }
}
