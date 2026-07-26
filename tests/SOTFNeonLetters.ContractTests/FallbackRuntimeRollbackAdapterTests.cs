using SOTFNeonLetters;
using Xunit;

public sealed class FallbackRuntimeRollbackAdapterTests
{
    [Fact]
    public void AttachedTargetWithNetworkIdentityUsesNetworkDestroy()
    {
        var target = new RollbackTargetState
        {
            IsAttached = true,
            HasNetworkIdentity = true,
        };
        var adapter = CreateAdapter(target);

        adapter.Dispose();

        Assert.Equal(
            (NetworkDestroyCount: 1, LocalDestroyCount: 0),
            (target.NetworkDestroyCount, target.LocalDestroyCount));
    }

    [Fact]
    public void LiveUnattachedTargetUsesLocalDestroy()
    {
        var target = new RollbackTargetState();
        var adapter = CreateAdapter(target);

        adapter.Dispose();

        Assert.Equal(
            (NetworkDestroyCount: 0, LocalDestroyCount: 1),
            (target.NetworkDestroyCount, target.LocalDestroyCount));
    }

    [Fact]
    public void LiveAttachedTargetWithZeroNetworkIdentityUsesLocalDestroy()
    {
        var target = new RollbackTargetState
        {
            IsAttached = true,
        };
        var adapter = CreateAdapter(target);

        adapter.Dispose();

        Assert.Equal(
            (NetworkDestroyCount: 0, LocalDestroyCount: 1),
            (target.NetworkDestroyCount, target.LocalDestroyCount));
    }

    [Fact]
    public void NullTargetIsNotDestroyed()
    {
        RollbackTargetState? target = null;
        var adapter = CreateAdapter(target);

        adapter.Dispose();

        Assert.Null(target);
    }

    [Fact]
    public void DeadTargetIsNotDestroyed()
    {
        var target = new RollbackTargetState
        {
            IsAlive = false,
            IsAttached = true,
            HasNetworkIdentity = true,
        };
        var adapter = CreateAdapter(target);

        adapter.Dispose();

        Assert.Equal(
            (NetworkDestroyCount: 0, LocalDestroyCount: 0),
            (target.NetworkDestroyCount, target.LocalDestroyCount));
    }

    [Fact]
    public void RepeatedDisposeDoesNotDestroyTargetAgain()
    {
        var target = new RollbackTargetState
        {
            IsAttached = true,
            HasNetworkIdentity = true,
        };
        var adapter = CreateAdapter(target);

        adapter.Dispose();
        adapter.Dispose();

        Assert.Equal(
            (NetworkDestroyCount: 1, LocalDestroyCount: 0),
            (target.NetworkDestroyCount, target.LocalDestroyCount));
    }

    [Fact]
    public async Task ConcurrentDisposeInvokesEachRollbackPathOnceAndDisarms()
    {
        const int concurrentCallerCount = 16;
        var networkTarget = new RollbackTargetState
        {
            IsAttached = true,
            HasNetworkIdentity = true,
        };
        var localTarget = new RollbackTargetState();
        int networkTargetNetworkCount = 0;
        int networkTargetLocalCount = 0;
        int localTargetNetworkCount = 0;
        int localTargetLocalCount = 0;
        var networkAdapter = CreateAdapter(
            networkTarget,
            () => Interlocked.Increment(
                ref networkTargetNetworkCount),
            () => Interlocked.Increment(
                ref networkTargetLocalCount));
        var localAdapter = CreateAdapter(
            localTarget,
            () => Interlocked.Increment(
                ref localTargetNetworkCount),
            () => Interlocked.Increment(
                ref localTargetLocalCount));
        using var startBarrier = new Barrier(
            concurrentCallerCount + 1);
        Task[] concurrentCalls = Enumerable.Range(
                0,
                concurrentCallerCount)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    startBarrier.SignalAndWait();
                    networkAdapter.Dispose();
                    localAdapter.Dispose();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        startBarrier.SignalAndWait();
        await Task.WhenAll(concurrentCalls);
        networkAdapter.Dispose();
        localAdapter.Dispose();

        Assert.Equal(
            (
                NetworkTargetNetworkCount: 1,
                NetworkTargetLocalCount: 0,
                LocalTargetNetworkCount: 0,
                LocalTargetLocalCount: 1),
            (
                NetworkTargetNetworkCount:
                    Volatile.Read(ref networkTargetNetworkCount),
                NetworkTargetLocalCount:
                    Volatile.Read(ref networkTargetLocalCount),
                LocalTargetNetworkCount:
                    Volatile.Read(ref localTargetNetworkCount),
                LocalTargetLocalCount:
                    Volatile.Read(ref localTargetLocalCount)));
    }

    [Fact]
    public void NetworkDestroyExceptionStillDisarmsRollback()
    {
        var target = new RollbackTargetState
        {
            IsAttached = true,
            HasNetworkIdentity = true,
            NetworkDestroyException = new InvalidOperationException("network destroy failed"),
        };
        var adapter = CreateAdapter(target);

        Exception? firstFailure = Record.Exception(adapter.Dispose);
        Exception? secondFailure = Record.Exception(adapter.Dispose);

        Assert.Equal(
            (FirstFailure: "network destroy failed", SecondFailure: null, DestroyCount: 1),
            (
                FirstFailure: firstFailure?.Message,
                SecondFailure: secondFailure?.Message,
                DestroyCount: target.NetworkDestroyCount));
    }

    [Fact]
    public void LocalDestroyExceptionStillDisarmsRollback()
    {
        var target = new RollbackTargetState
        {
            LocalDestroyException = new InvalidOperationException("local destroy failed"),
        };
        var adapter = CreateAdapter(target);

        Exception? firstFailure = Record.Exception(adapter.Dispose);
        Exception? secondFailure = Record.Exception(adapter.Dispose);

        Assert.Equal(
            (FirstFailure: "local destroy failed", SecondFailure: null, DestroyCount: 1),
            (
                FirstFailure: firstFailure?.Message,
                SecondFailure: secondFailure?.Message,
                DestroyCount: target.LocalDestroyCount));
    }

    [Fact]
    public void AttachmentAfterLocalCleanupCannotTriggerNetworkDestroy()
    {
        var target = new RollbackTargetState();
        var adapter = CreateAdapter(target);

        adapter.Dispose();
        target.IsAttached = true;
        target.HasNetworkIdentity = true;
        adapter.Dispose();

        Assert.Equal(
            (NetworkDestroyCount: 0, LocalDestroyCount: 1),
            (target.NetworkDestroyCount, target.LocalDestroyCount));
    }

    private static NeonLetterFallbackRollbackAdapter<RollbackTargetState?>
        CreateAdapter(
            RollbackTargetState? target)
    {
        return CreateAdapter(
            target,
            () => target!.DestroyOverNetwork(),
            () => target!.DestroyLocally());
    }

    private static NeonLetterFallbackRollbackAdapter<RollbackTargetState?>
        CreateAdapter(
            RollbackTargetState? target,
            Action destroyOverNetwork,
            Action destroyLocally)
    {
        return new NeonLetterFallbackRollbackAdapter<RollbackTargetState?>(
            target,
            isTargetAlive: value => value is { IsAlive: true },
            isTargetAttached: value => value is { IsAttached: true },
            hasNetworkIdentity: value =>
                value is { HasNetworkIdentity: true },
            destroyOverNetwork: _ => destroyOverNetwork(),
            destroyLocally: _ => destroyLocally());
    }

    private sealed class RollbackTargetState
    {
        public bool IsAlive { get; set; } = true;

        public bool IsAttached { get; set; }

        public bool HasNetworkIdentity { get; set; }

        public int NetworkDestroyCount { get; private set; }

        public int LocalDestroyCount { get; private set; }

        public Exception? NetworkDestroyException { get; init; }

        public Exception? LocalDestroyException { get; init; }

        public void DestroyOverNetwork()
        {
            NetworkDestroyCount++;

            if (NetworkDestroyException != null)
            {
                throw NetworkDestroyException;
            }
        }

        public void DestroyLocally()
        {
            LocalDestroyCount++;

            if (LocalDestroyException != null)
            {
                throw LocalDestroyException;
            }
        }
    }
}
