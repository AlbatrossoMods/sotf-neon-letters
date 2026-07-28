using SOTFNeonLetters;
using Xunit;

public sealed class ColorInteractionMutationBehaviorTests
{
    [Fact]
    public void ProxyRadiusUsesTheLargestVerticalSizeAndPositivePadding()
    {
        NeonLetterColorInteractionGeometry geometry =
            NeonLetterColorInteractionGeometryPolicy.Resolve(
                new NeonLetterColorInteractionBounds(
                    CenterX: 0f,
                    CenterY: 0f,
                    CenterZ: 0f,
                    SizeX: 0.2f,
                    SizeY: 1.2f,
                    SizeZ: 0.6f));

        Assert.Equal(0.75f, geometry.Radius);
    }

    [Fact]
    public void MissingLeaseLookupsReturnFalseWithoutChangingRegistry()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();

        bool found = registry.TryGet(41, out string? foundLease);
        bool removed = registry.TryRemove(41, out string? removedLease);

        Assert.Equal(
            (false, (string?)null, false, (string?)null, 0),
            (found, foundLease, removed, removedLease, registry.Count));
    }

    [Fact]
    public void BoundedLeaseSweepInspectsExactlyOneLiveEntry()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(1, "first");
        registry.TryAdd(2, "second");
        var aliveResults = new Queue<bool>(new[] { true });
        int callbackCount = 0;

        bool removed = registry.TryTakeNextDead(
            maxEntries: 1,
            _ =>
            {
                callbackCount++;
                return aliveResults.Dequeue();
            },
            out string? deadLease,
            out int inspectedEntries);

        Assert.Equal(
            (false, (string?)null, 1, 1, 2),
            (
                removed,
                deadLease,
                inspectedEntries,
                callbackCount,
                registry.Count));
    }

    [Fact]
    public void RemovingAMiddleLeasePreservesTheRemainingSweepOrder()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(1, "first");
        registry.TryAdd(2, "second");
        registry.TryAdd(3, "third");

        bool removedMiddle =
            registry.TryRemove(2, out string? middleLease);
        bool removedDead = registry.TryTakeNextDead(
            maxEntries: 3,
            _ => false,
            out string? deadLease,
            out int inspectedEntries);
        bool removedRemaining =
            registry.TryTakeFirst(out string? remainingLease);

        Assert.Equal(
            (true, "second", true, "first", 1, true, "third", 0),
            (
                removedMiddle,
                middleLease,
                removedDead,
                deadLease,
                inspectedEntries,
                removedRemaining,
                remainingLease,
                registry.Count));
    }

    [Fact]
    public void TransientCreationFailureRetriesAtTheExactBoundary()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();

        bool changed = failures.RecordTransientFailure(
            structureInstanceId: 7,
            updateTick: 0,
            fingerprint: "temporary");
        bool beforeBoundary =
            failures.AllowsAttempt(7, updateTick: 119);
        bool atBoundary =
            failures.AllowsAttempt(7, updateTick: 120);

        Assert.Equal(
            (true, false, true, 1),
            (changed, beforeBoundary, atBoundary, failures.Count));
    }

    [Fact]
    public void TerminalFailureReportsOnlyNewFingerprintsAndBlocksEveryRetry()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();

        bool first = failures.RecordTerminalFailure(7, "missing root");
        bool repeated = failures.RecordTerminalFailure(7, "missing root");
        bool changed = failures.RecordTerminalFailure(7, "missing collider");
        bool allowedAtMaximumTick =
            failures.AllowsAttempt(7, long.MaxValue);

        Assert.Equal(
            (true, false, true, false, 1),
            (
                first,
                repeated,
                changed,
                allowedAtMaximumTick,
                failures.Count));
    }

    [Fact]
    public void BackfillScheduleUsesInclusiveRetryBoundaryWithoutOverflowAndCanReset()
    {
        var schedule = new NeonLetterColorInteractionBackfillSchedule();
        bool atZero = schedule.TryBeginAttempt(updateTick: 0);
        bool beforeBoundary = schedule.TryBeginAttempt(updateTick: 119);
        bool atBoundary = schedule.TryBeginAttempt(updateTick: 120);
        var overflowSchedule =
            new NeonLetterColorInteractionBackfillSchedule();
        bool nearMaximum = overflowSchedule.TryBeginAttempt(
            long.MaxValue -
            NeonLetterColorInteractionBackfillSchedule.RetryUpdateDelay +
            1);
        bool beforeMaximum =
            overflowSchedule.TryBeginAttempt(long.MaxValue - 1);

        overflowSchedule.Reset();
        bool afterReset = overflowSchedule.TryBeginAttempt(updateTick: 0);

        Assert.Equal(
            (true, false, true, true, false, true),
            (
                atZero,
                beforeBoundary,
                atBoundary,
                nearMaximum,
                beforeMaximum,
                afterReset));
    }

    [Fact]
    public void BackfillCursorClearsInactivePositiveActiveZeroAndResetWindows()
    {
        var cursor = new NeonLetterColorInteractionBackfillCursor();
        NeonLetterColorInteractionBackfillWindow inactive =
            cursor.TakeWindow(itemCount: 5, maximumItems: 2);
        cursor.StartCycle();
        NeonLetterColorInteractionBackfillWindow activeZero =
            cursor.TakeWindow(itemCount: 0, maximumItems: 2);
        cursor.StartCycle();
        NeonLetterColorInteractionBackfillWindow active =
            cursor.TakeWindow(itemCount: 5, maximumItems: 2);

        cursor.Reset();
        NeonLetterColorInteractionBackfillWindow afterReset =
            cursor.TakeWindow(itemCount: 5, maximumItems: 2);

        Assert.Equal(
            (0, 0, 0, 0, 0, 2, 0, 0, false),
            (
                inactive.StartOffset,
                inactive.Count,
                activeZero.StartOffset,
                activeZero.Count,
                active.StartOffset,
                active.Count,
                afterReset.StartOffset,
                afterReset.Count,
                cursor.IsActive));
    }

    [Fact]
    public void UnknownTargetModeCannotCommitThroughAnyRoute()
    {
        int singlePlayerCommits = 0;
        int multiplayerRequests = 0;

        NeonLetterColorRoutedCommit result =
            NeonLetterColorCommitRoutingCoordinator.TryCommit(
                (NeonLetterColorTargetMode)int.MaxValue,
                isServer: true,
                isClient: true,
                NeonRgba.ProjectCyan,
                _ => singlePlayerCommits++,
                _ =>
                {
                    multiplayerRequests++;
                    return true;
                });

        Assert.Equal(
            (false, NeonLetterColorCommitRoute.Unavailable, 0, 0),
            (
                result.Succeeded,
                result.Route,
                singlePlayerCommits,
                multiplayerRequests));
    }

    [Fact]
    public void FreshUiDestroyCoordinatorIgnoresDestroyBeforeBegin()
    {
        var target = new object();
        var session = new NeonLetterColorEditorSession<object>();
        session.Open(target, NeonRgba.ProjectCyan);
        var destroy = new NeonLetterUiDestroyCoordinator<object>();
        int callbackCount = 0;

        destroy.Destroy(
            session,
            (_, _) => callbackCount++,
            () => callbackCount++,
            () => callbackCount++,
            () => callbackCount++,
            _ => callbackCount++);

        Assert.Equal((0, target), (callbackCount, session.Target));
    }

}
