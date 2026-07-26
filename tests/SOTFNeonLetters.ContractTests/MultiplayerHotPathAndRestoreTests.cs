using SOTFNeonLetters;
using Xunit;

public sealed class MultiplayerHotPathAndRestoreTests
{
    [Fact]
    public void SequentialSnapshotRentalsReuseOneBuffer()
    {
        var pool = new NeonLetterReentrantSnapshotPool<int>();

        List<int> first = pool.Rent();
        pool.Return(first);
        List<int> second = pool.Rent();
        try
        {
            Assert.Equal(
                (true, 1),
                (ReferenceEquals(first, second), pool.AllocatedBufferCount));
        }
        finally
        {
            pool.Return(second);
        }
    }

    [Fact]
    public void NestedSnapshotRentalsAllocateOnlyForActiveDepth()
    {
        var pool = new NeonLetterReentrantSnapshotPool<int>();

        List<int> outer = pool.Rent();
        List<int> inner = pool.Rent();
        try
        {
            Assert.Equal(
                (false, 2),
                (ReferenceEquals(outer, inner), pool.AllocatedBufferCount));
        }
        finally
        {
            pool.Return(inner);
            pool.Return(outer);
        }
    }

    [Fact]
    public void SnapshotRentalReturnedFromFinallyIsReused()
    {
        var pool = new NeonLetterReentrantSnapshotPool<int>();
        List<int>? interrupted = null;
        Action interruptedRental =
            () =>
            {
                List<int> snapshot = pool.Rent();
                interrupted = snapshot;
                try
                {
                    throw new InvalidOperationException("interrupted");
                }
                finally
                {
                    pool.Return(snapshot);
                }
            };

        Assert.Throws<InvalidOperationException>(interruptedRental);
        List<int> reused = pool.Rent();
        try
        {
            Assert.Equal(
                (true, 1),
                (
                    ReferenceEquals(interrupted, reused),
                    pool.AllocatedBufferCount));
        }
        finally
        {
            pool.Return(reused);
        }
    }

    [Fact]
    public void ReturnedSnapshotBufferDoesNotRetainItems()
    {
        var pool = new NeonLetterReentrantSnapshotPool<object>();
        var retainedCandidate = new object();
        List<object> first = pool.Rent();
        first.Add(retainedCandidate);

        pool.Return(first);
        List<object> reused = pool.Rent();
        try
        {
            Assert.Equal(
                (true, 0),
                (ReferenceEquals(first, reused), reused.Count));
        }
        finally
        {
            pool.Return(reused);
        }
    }

    [Fact]
    public void EmptyPendingColorDrainInvokesNoCallbacks()
    {
        var state = new NeonLetterReplicatedColorState<int>(
            pendingCapacity: 4,
            pendingLifetimeSeconds: 15d);
        int callbackCount = 0;

        int appliedCount = state.DrainReady(
            nowSeconds: 1d,
            maxItems: 2,
            isReady: _ =>
            {
                callbackCount++;
                return true;
            },
            apply: (_, _) => callbackCount++,
            onApplyError: (_, _) => callbackCount++);

        Assert.Equal((0, 0), (appliedCount, callbackCount));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void EmptyApplyReadyRejectsNonFiniteTime(double nowSeconds)
    {
        NeonLetterPendingColors<int> pending = CreatePendingColors();
        Action applyReady = () =>
        {
            pending.ApplyReady(
                nowSeconds,
                isReady: _ => true,
                apply: (_, _) => { });
        };

        Assert.Throws<ArgumentOutOfRangeException>(applyReady);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void EmptyApplyReadyContinuingRejectsNonFiniteTime(
        double nowSeconds)
    {
        NeonLetterPendingColors<int> pending = CreatePendingColors();
        Action applyReady = () =>
        {
            pending.ApplyReadyContinuing(
                nowSeconds,
                maxItems: 1,
                isReady: _ => true,
                apply: (_, _) => { },
                onApplyError: (_, _) => { });
        };

        Assert.Throws<ArgumentOutOfRangeException>(applyReady);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void EmptyPruneRejectsNonFiniteTime(double nowSeconds)
    {
        NeonLetterPendingColors<int> pending = CreatePendingColors();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => pending.Prune(nowSeconds));
    }

    [Fact]
    public void BoundedPendingDrainContinuesFromTheNextIdentity()
    {
        var pending = CreatePendingColors(1, 2, 3);
        var observed = new List<int>();
        var applied = new List<int>();

        pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity =>
            {
                observed.Add(identity);
                return false;
            },
            apply: (_, _) => { },
            onApplyError: (_, exception) => throw exception);
        pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity =>
            {
                observed.Add(identity);
                return true;
            },
            apply: (identity, _) => applied.Add(identity),
            onApplyError: (_, exception) => throw exception);

        Assert.Equal(
            ("1,2", "2", 2),
            (
                string.Join(",", observed),
                string.Join(",", applied),
                pending.Count));
    }

    [Fact]
    public void BoundedPendingDrainWrapsInOriginalOrder()
    {
        var pending = CreatePendingColors(1, 2, 3);
        var observed = new List<int>();

        for (int slice = 0; slice < 2; slice++)
        {
            pending.ApplyReadyContinuing(
                nowSeconds: 1d,
                maxItems: 2,
                isReady: identity =>
                {
                    observed.Add(identity);
                    return false;
                },
                apply: (_, _) => { },
                onApplyError: (_, exception) => throw exception);
        }

        Assert.Equal(new[] { 1, 2, 3, 1 }, observed);
    }

    [Fact]
    public void RemovingTheNextPendingIdentityAdvancesTheCursor()
    {
        var pending = CreatePendingColors(1, 2, 3);
        var observed = new List<int>();
        pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity =>
            {
                observed.Add(identity);
                return false;
            },
            apply: (_, _) => { },
            onApplyError: (_, exception) => throw exception);

        pending.Remove(2);
        pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity =>
            {
                observed.Add(identity);
                return false;
            },
            apply: (_, _) => { },
            onApplyError: (_, exception) => throw exception);

        Assert.Equal(new[] { 1, 3 }, observed);
    }

    [Fact]
    public void ReentrantPendingChangesDoNotEnterTheCurrentEpoch()
    {
        var pending = CreatePendingColors(1, 2, 3);
        var applied = new List<int>();
        pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: _ => false,
            apply: (_, _) => { },
            onApplyError: (_, exception) => throw exception);

        int firstAppliedCount = pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 3,
            isReady: _ => true,
            apply: (identity, _) =>
            {
                applied.Add(identity);
                if (identity == 2)
                {
                    pending.Remove(3);
                    pending.Enqueue(
                        4,
                        NeonRgba.ProjectCyan,
                        nowSeconds: 1d);
                }
            },
            onApplyError: (_, exception) => throw exception);
        int secondAppliedCount = pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 3,
            isReady: _ => true,
            apply: (identity, _) => applied.Add(identity),
            onApplyError: (_, exception) => throw exception);

        Assert.Equal(
            (2, 1, "2,1,4", 0),
            (
                firstAppliedCount,
                secondAppliedCount,
                string.Join(",", applied),
                pending.Count));
    }

    [Fact]
    public void ReentrantReplacementOfSnapshottedIdentityUsesReplacement()
    {
        var pending = CreatePendingColors(1, 2);
        NeonRgba replacement = new(1f, 0f, 0f, 1f);
        var applied = new List<string>();

        int appliedCount = pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 2,
            isReady: _ => true,
            apply: (identity, color) =>
            {
                applied.Add($"{identity}:{color.Red}");
                if (identity == 1)
                {
                    pending.Remove(2);
                    pending.Enqueue(2, replacement, nowSeconds: 1d);
                }
            },
            onApplyError: (_, exception) => throw exception);

        Assert.Equal(
            (2, "1:0,2:1", 0),
            (appliedCount, string.Join(",", applied), pending.Count));
    }

    [Fact]
    public void NestedPendingDrainConsumesTheReservedNextSlice()
    {
        var pending = CreatePendingColors(1, 2, 3);
        var applied = new List<int>();
        int nestedAppliedCount = 0;
        bool nested = false;

        int outerAppliedCount = pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 2,
            isReady: _ => true,
            apply: (identity, _) =>
            {
                applied.Add(identity);
                if (!nested)
                {
                    nested = true;
                    nestedAppliedCount = pending.ApplyReadyContinuing(
                        nowSeconds: 1d,
                        maxItems: 1,
                        isReady: _ => true,
                        apply: (nestedIdentity, _) =>
                            applied.Add(nestedIdentity),
                        onApplyError: (_, exception) => throw exception);
                }
            },
            onApplyError: (_, exception) => throw exception);

        Assert.Equal(
            (2, 1, "1,3,2", 0),
            (
                outerAppliedCount,
                nestedAppliedCount,
                string.Join(",", applied),
                pending.Count));
    }

    [Fact]
    public void NestedPendingDrainDoesNotWrapIntoOuterSnapshot()
    {
        var pending = CreatePendingColors(1, 2, 3);
        var applied = new List<int>();
        int nestedAppliedCount = 0;
        bool nested = false;

        int outerAppliedCount = pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 2,
            isReady: _ => true,
            apply: (identity, _) =>
            {
                applied.Add(identity);
                if (!nested)
                {
                    nested = true;
                    nestedAppliedCount = pending.ApplyReadyContinuing(
                        nowSeconds: 1d,
                        maxItems: 2,
                        isReady: _ => true,
                        apply: (nestedIdentity, _) =>
                            applied.Add(nestedIdentity),
                        onApplyError: (_, exception) => throw exception);
                }
            },
            onApplyError: (_, exception) => throw exception);

        Assert.Equal(
            (2, 1, "1,3,2", 0),
            (
                outerAppliedCount,
                nestedAppliedCount,
                string.Join(",", applied),
                pending.Count));
    }

    [Fact]
    public void ReadinessProbeFailurePropagatesAndPreservesPendingState()
    {
        var pending = CreatePendingColors(1);
        int routedErrorCount = 0;

        Exception? readinessError = Record.Exception(
            () => pending.ApplyReadyContinuing(
                nowSeconds: 1d,
                maxItems: 1,
                isReady: _ => throw new InvalidOperationException(),
                apply: (_, _) => { },
                onApplyError: (_, _) => routedErrorCount++));
        int appliedCount = pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: _ => true,
            apply: (_, _) => { },
            onApplyError: (_, exception) => throw exception);

        Assert.Equal(
            (typeof(InvalidOperationException), 0, 1, 0),
            (
                readinessError?.GetType(),
                routedErrorCount,
                appliedCount,
                pending.Count));
    }

    [Fact]
    public void NegativePendingColorBudgetIsRejected()
    {
        var pending = CreatePendingColors(1);

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => pending.ApplyReadyContinuing(
                    nowSeconds: 1d,
                    maxItems: -1,
                    isReady: _ => true,
                    apply: (_, _) => { },
                    onApplyError: (_, _) => { }));

        Assert.Equal("maxItems", exception.ParamName);
    }

    [Fact]
    public void ZeroBudgetPendingDrainPreservesEntriesAndCursor()
    {
        NeonLetterPendingColors<int> pending = CreatePendingColors(1, 2, 3);
        var observed = new List<int>();
        pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity =>
            {
                observed.Add(identity);
                return false;
            },
            apply: (_, _) => { },
            onApplyError: (_, exception) => throw exception);
        int zeroBudgetCallbackCount = 0;

        int zeroBudgetAppliedCount = pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 0,
            isReady: _ =>
            {
                zeroBudgetCallbackCount++;
                return true;
            },
            apply: (_, _) => zeroBudgetCallbackCount++,
            onApplyError: (_, _) => zeroBudgetCallbackCount++);
        pending.ApplyReadyContinuing(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: identity =>
            {
                observed.Add(identity);
                return false;
            },
            apply: (_, _) => { },
            onApplyError: (_, exception) => throw exception);

        Assert.Equal(
            (0, 0, "1,2", 3),
            (
                zeroBudgetAppliedCount,
                zeroBudgetCallbackCount,
                string.Join(",", observed),
                pending.Count));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ZeroBudgetPendingDrainRejectsNonFiniteTime(
        double nowSeconds)
    {
        NeonLetterPendingColors<int> pending = CreatePendingColors(1);
        Action drain = () =>
        {
            pending.ApplyReadyContinuing(
                nowSeconds,
                maxItems: 0,
                isReady: _ => true,
                apply: (_, _) => { },
                onApplyError: (_, _) => { });
        };

        Assert.Throws<ArgumentOutOfRangeException>(drain);
    }

    [Fact]
    public void BoundedPendingDrainRejectsInvalidTimeWithPendingEntry()
    {
        var pending = CreatePendingColors(1);

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => pending.ApplyReadyContinuing(
                    nowSeconds: double.NaN,
                    maxItems: 1,
                    isReady: _ => true,
                    apply: (_, _) => { },
                    onApplyError: (_, _) => { }));

        Assert.Equal("nowSeconds", exception.ParamName);
    }

    [Fact]
    public void BoundedPendingDrainPrunesExpiredEntriesBeforeCallbacks()
    {
        var pending = CreatePendingColors(1);
        int callbackCount = 0;

        int appliedCount = pending.ApplyReadyContinuing(
            nowSeconds: 15d,
            maxItems: 1,
            isReady: _ =>
            {
                callbackCount++;
                return true;
            },
            apply: (_, _) => callbackCount++,
            onApplyError: (_, _) => callbackCount++);

        Assert.Equal((0, 0, 0), (appliedCount, callbackCount, pending.Count));
    }

    [Fact]
    public void BoundedPendingDrainRejectsNullReadinessCallback()
    {
        var pending = CreatePendingColors(1);

        Assert.Throws<ArgumentNullException>(
            () => pending.ApplyReadyContinuing(
                nowSeconds: 1d,
                maxItems: 1,
                isReady: null!,
                apply: (_, _) => { },
                onApplyError: (_, _) => { }));
    }

    [Fact]
    public void BoundedPendingDrainRejectsNullApplyCallback()
    {
        var pending = CreatePendingColors(1);

        Assert.Throws<ArgumentNullException>(
            () => pending.ApplyReadyContinuing(
                nowSeconds: 1d,
                maxItems: 1,
                isReady: _ => false,
                apply: null!,
                onApplyError: (_, _) => { }));
    }

    [Fact]
    public void BoundedPendingDrainRejectsNullErrorCallback()
    {
        var pending = CreatePendingColors(1);

        Assert.Throws<ArgumentNullException>(
            () => pending.ApplyReadyContinuing(
                nowSeconds: 1d,
                maxItems: 1,
                isReady: _ => false,
                apply: (_, _) => { },
                onApplyError: null!));
    }

    [Fact]
    public void BoundedReplicatedDrainRejectsNullApply()
    {
        var state = new NeonLetterReplicatedColorState<int>(
            pendingCapacity: 4,
            pendingLifetimeSeconds: 15d);
        Action<int, NeonRgba> apply = null!;

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => state.DrainReady(
                    nowSeconds: 1d,
                    maxItems: 1,
                    isReady: _ => true,
                    apply,
                    onApplyError: (_, _) => { }));

        Assert.Equal("apply", exception.ParamName);
    }

    [Fact]
    public void EmptyRestoreAdvanceInvokesNoEntryCallbacks()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int callbackCount = 0;

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 2,
            maxFallbackSpawns: 1,
            observe: (_, _, _) =>
            {
                callbackCount++;
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable);
            },
            startFallback: _ =>
            {
                callbackCount++;
                return "fallback";
            },
            applyRestored: (_, _) =>
            {
                callbackCount++;
                return true;
            },
            onEntryError: (_, _) => callbackCount++);

        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public void ZeroRestoreItemBudgetDefersWithoutCallbacks()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1);
        int callbackCount = 0;

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 0,
            maxFallbackSpawns: 1,
            observe: (_, _, _) =>
            {
                callbackCount++;
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable);
            },
            startFallback: _ =>
            {
                callbackCount++;
                return "fallback";
            },
            applyRestored: (_, _) =>
            {
                callbackCount++;
                return true;
            },
            onEntryError: (_, _) => callbackCount++);

        Assert.Equal((1, 0), (coordinator.PendingCount, callbackCount));
    }

    [Theory]
    [InlineData(0, "observe")]
    [InlineData(1, "startFallback")]
    [InlineData(2, "applyRestored")]
    [InlineData(3, "onEntryError")]
    public void RestoreAdvanceRejectsNullCallbacks(
        int callback,
        string expectedParameterName)
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(0);
        Func<NeonLetterMultiplayerSaveEntry, bool, string?,
            NeonLetterMultiplayerRestoreObservation<string>> observe =
            (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    callback == 1
                        ? NeonLetterMultiplayerRestoreObservationKind
                            .ReadyToSpawnFallback
                        : NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetReady,
                    Target: "native",
                    ResolvedRecipeId: NeonLetterSmallCatalog.Get('A').RecipeId);
        Func<NeonLetterMultiplayerSaveEntry, string> startFallback =
            _ => "fallback";
        Func<NeonLetterMultiplayerSaveEntry, string, bool> applyRestored =
            (_, _) => true;
        Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError =
            (_, _) => { };
        switch (callback)
        {
            case 0:
                observe = null!;
                break;
            case 1:
                startFallback = null!;
                break;
            case 2:
                applyRestored = null!;
                break;
            default:
                onEntryError = null!;
                observe = (_, _, _) =>
                    throw new InvalidOperationException("observe failed");
                break;
        }

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => coordinator.Advance(
                    nowSeconds: 1d,
                    maxItems: 1,
                    maxFallbackSpawns: 1,
                    observe,
                    startFallback,
                    applyRestored,
                    onEntryError));

        Assert.Equal(expectedParameterName, exception.ParamName);
    }

    [Fact]
    public void BoundedRestoreSlicesMatchUnlimitedOrderAndErrors()
    {
        RestoreRun unlimited = RunRestore(maxItems: null);
        RestoreRun bounded = RunRestore(maxItems: 2);

        Assert.Equal(
            (unlimited.Events, unlimited.PendingCount, 2),
            (bounded.Events, bounded.PendingCount, bounded.MaximumSliceSize));
    }

    [Fact]
    public void BoundedRestoreContinuesFromTheNextPendingEntry()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1, 2, 3);
        var observed = new List<int>();
        var restored = new List<int>();

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 1,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                observed.Add(entry.NativeSaveId);
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 1,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                observed.Add(entry.NativeSaveId);
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: $"native-{entry.NativeSaveId}",
                    ResolvedRecipeId: entry.RecipeId);
            },
            startFallback: _ => "fallback",
            applyRestored: (entry, _) =>
            {
                restored.Add(entry.NativeSaveId);
                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            ("1,2", "2", 2),
            (
                string.Join(",", observed),
                string.Join(",", restored),
                coordinator.PendingCount));
    }

    [Fact]
    public void NextRestoreSliceStartsAfterTheUnavailableEntry()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1, 2, 3);
        var observed = new List<int>();

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 2,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                observed.Add(entry.NativeSaveId);
                return entry.NativeSaveId == 1
                    ? new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetReady,
                        Target: "native-1",
                        ResolvedRecipeId: entry.RecipeId)
                    : new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .ProcessedRecipeUnavailable);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 1,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                observed.Add(entry.NativeSaveId);
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            ("1,2,3", 2),
            (string.Join(",", observed), coordinator.PendingCount));
    }

    [Fact]
    public void BoundedRestoreWrapsPendingEntriesInOriginalOrder()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1, 2, 3);
        var observed = new List<int>();

        for (int slice = 0; slice < 2; slice++)
        {
            coordinator.Advance(
                nowSeconds: 1d,
                maxItems: 2,
                maxFallbackSpawns: 1,
                observe: (entry, _, _) =>
                {
                    observed.Add(entry.NativeSaveId);
                    return new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .ProcessedRecipeUnavailable);
                },
                startFallback: _ => "fallback",
                applyRestored: (_, _) => true,
                onEntryError: (_, exception) => throw exception);
        }

        Assert.Equal(new[] { 1, 2, 3, 1 }, observed);
    }

    [Fact]
    public void ReentrantRestoreStageDoesNotEnterTheCurrentEpoch()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1, 2, 3);
        var firstObserved = new List<int>();
        var secondObserved = new List<int>();
        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 1,
            maxFallbackSpawns: 1,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable),
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 3,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                firstObserved.Add(entry.NativeSaveId);
                if (entry.NativeSaveId == 2)
                {
                    coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
                    {
                        Entries = new List<NeonLetterMultiplayerSaveEntry>
                        {
                            CreateEntry(4)
                        }
                    });
                }

                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: $"native-{entry.NativeSaveId}",
                    ResolvedRecipeId: entry.RecipeId);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);
        int pendingAfterFirstAdvance = coordinator.PendingCount;
        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 3,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                secondObserved.Add(entry.NativeSaveId);
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: $"native-{entry.NativeSaveId}",
                    ResolvedRecipeId: entry.RecipeId);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            ("2,3,1", 1, "4", 0),
            (
                string.Join(",", firstObserved),
                pendingAfterFirstAdvance,
                string.Join(",", secondObserved),
                coordinator.PendingCount));
    }

    [Fact]
    public void CurrentRestoreSliceFinishesBeforeStagedReplacementIsProcessed()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1, 2);
        var restored = new List<int>();

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 2,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                if (entry.NativeSaveId == 1)
                {
                    coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
                    {
                        Entries = new List<NeonLetterMultiplayerSaveEntry>
                        {
                            CreateEntry(3)
                        }
                    });
                }

                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: $"native-{entry.NativeSaveId}",
                    ResolvedRecipeId: entry.RecipeId);
            },
            startFallback: _ => "fallback",
            applyRestored: (entry, _) =>
            {
                restored.Add(entry.NativeSaveId);
                return true;
            },
            onEntryError: (_, exception) => throw exception);
        int pendingAfterFirstAdvance = coordinator.PendingCount;
        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 1,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: $"native-{entry.NativeSaveId}",
                    ResolvedRecipeId: entry.RecipeId),
            startFallback: _ => "fallback",
            applyRestored: (entry, _) =>
            {
                restored.Add(entry.NativeSaveId);
                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            ("1,2,3", 1, 0),
            (
                string.Join(",", restored),
                pendingAfterFirstAdvance,
                coordinator.PendingCount));
    }

    [Fact]
    public void NestedRestoreAdvanceConsumesTheReservedNextSlice()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1, 2, 3);
        var observed = new List<int>();
        bool nested = false;

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 2,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                observed.Add(entry.NativeSaveId);
                if (!nested)
                {
                    nested = true;
                    coordinator.Advance(
                        nowSeconds: 1d,
                        maxItems: 1,
                        maxFallbackSpawns: 1,
                        observe: (nestedEntry, _, _) =>
                        {
                            observed.Add(nestedEntry.NativeSaveId);
                            return new
                                NeonLetterMultiplayerRestoreObservation<string>(
                                    NeonLetterMultiplayerRestoreObservationKind
                                        .NativeTargetReady,
                                    Target:
                                        $"native-{nestedEntry.NativeSaveId}",
                                    ResolvedRecipeId: nestedEntry.RecipeId);
                        },
                        startFallback: _ => "fallback",
                        applyRestored: (_, _) => true,
                        onEntryError: (_, exception) => throw exception);
                }

                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: $"native-{entry.NativeSaveId}",
                    ResolvedRecipeId: entry.RecipeId);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            ("1,3,2", 0),
            (string.Join(",", observed), coordinator.PendingCount));
    }

    [Fact]
    public void NestedRestoreAdvanceDoesNotWrapIntoOuterSnapshot()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1, 2, 3);
        var observed = new List<int>();
        bool nested = false;
        int nestedObservedCount = 0;

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 2,
            maxFallbackSpawns: 1,
            observe: (entry, _, _) =>
            {
                observed.Add(entry.NativeSaveId);
                if (!nested)
                {
                    nested = true;
                    coordinator.Advance(
                        nowSeconds: 1d,
                        maxItems: 2,
                        maxFallbackSpawns: 1,
                        observe: (nestedEntry, _, _) =>
                        {
                            nestedObservedCount++;
                            observed.Add(nestedEntry.NativeSaveId);
                            return new
                                NeonLetterMultiplayerRestoreObservation<string>(
                                    NeonLetterMultiplayerRestoreObservationKind
                                        .NativeTargetReady,
                                    Target:
                                        $"native-{nestedEntry.NativeSaveId}",
                                    ResolvedRecipeId: nestedEntry.RecipeId);
                        },
                        startFallback: _ => "fallback",
                        applyRestored: (_, _) => true,
                        onEntryError: (_, exception) => throw exception);
                }

                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: $"native-{entry.NativeSaveId}",
                    ResolvedRecipeId: entry.RecipeId);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (1, "1,3,2", 0),
            (
                nestedObservedCount,
                string.Join(",", observed),
                coordinator.PendingCount));
    }

    [Fact]
    public void PendingReadinessDoesNotExpireAfterArbitraryDelay()
    {
        NeonLetterMultiplayerSaveEntry entry = CreateEntry(0);
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry> { entry }
        });
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var errors = new List<Exception>();

        void Advance(
            double nowSeconds,
            NeonLetterMultiplayerRestoreObservationKind kind)
        {
            coordinator.Advance(
                nowSeconds,
                maxItems: 1,
                maxFallbackSpawns: 0,
                observe: (_, _, _) =>
                    new NeonLetterMultiplayerRestoreObservation<string>(kind),
                startFallback: _ => "fallback",
                applyRestored: (_, _) => true,
                onEntryError: (_, exception) => errors.Add(exception));
        }

        Advance(
            nowSeconds: 0d,
            NeonLetterMultiplayerRestoreObservationKind
                .ProcessedRecipeUnavailable);
        Advance(
            nowSeconds: 1_000_000d,
            NeonLetterMultiplayerRestoreObservationKind
                .ProcessedRecipeUnavailable);

        Assert.Equal((0, 1), (errors.Count, coordinator.PendingCount));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void NegativeRestoreBudgetsAreRejected(
        int maxItems,
        int maxFallbackSpawns)
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1);

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => coordinator.Advance(
                    nowSeconds: 1d,
                    maxItems,
                    maxFallbackSpawns,
                    observe: (_, _, _) =>
                        new NeonLetterMultiplayerRestoreObservation<string>(
                            NeonLetterMultiplayerRestoreObservationKind
                                .ProcessedRecipeUnavailable),
                    startFallback: _ => "fallback",
                    applyRestored: (_, _) => true,
                    onEntryError: (_, _) => { }));

        Assert.Equal(
            maxItems < 0 ? "maxItems" : "maxFallbackSpawns",
            exception.ParamName);
    }

    [Fact]
    public void ReadyObservationWithoutTargetReportsItsEntryError()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1);
        var errors = new List<Exception>();

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady),
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => errors.Add(exception));

        Assert.Equal(
            (typeof(InvalidOperationException), 0),
            (errors.Single().GetType(), coordinator.PendingCount));
    }

    [Fact]
    public void NativeMismatchWithoutRecipeReportsADefiniteMismatchError()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1);
        var errors = new List<Exception>();

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeRecipeMismatch),
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => errors.Add(exception));

        Assert.Equal(
            (typeof(InvalidOperationException), 0),
            (errors.Single().GetType(), coordinator.PendingCount));
    }

    [Fact]
    public void NativeMismatchWithSameRecipeUsesTheEntryErrorPath()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1);
        int errorCount = 0;

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (entry, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeRecipeMismatch,
                    ResolvedRecipeId: entry.RecipeId),
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, _) => errorCount++);

        Assert.Equal((1, 0), (errorCount, coordinator.PendingCount));
    }

    [Fact]
    public void UnsupportedRestoreObservationReportsItsEntryError()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1);
        var errors = new List<Exception>();

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    (NeonLetterMultiplayerRestoreObservationKind)999),
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => errors.Add(exception));

        Assert.Equal(
            (typeof(InvalidOperationException), 0),
            (errors.Single().GetType(), coordinator.PendingCount));
    }

    [Fact]
    public void FailedReadyApplyRemainsPendingAcrossArbitraryDelay()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1);
        var errors = new List<Exception>();

        void Advance(double nowSeconds)
        {
            coordinator.Advance(
                nowSeconds,
                observe: (_, _, _) =>
                    new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetReady,
                        Target: "native",
                        ResolvedRecipeId:
                            NeonLetterSmallCatalog.Get('A').RecipeId),
                startFallback: _ => "fallback",
                applyRestored: (_, _) => false,
                onEntryError: (_, exception) => errors.Add(exception));
        }

        Advance(nowSeconds: 0d);
        Advance(nowSeconds: 1_000_000d);

        Assert.Equal(
            (0, 1),
            (errors.Count, coordinator.PendingCount));
    }

    [Fact]
    public void KnownHostConsumesAStagedEnvelopeImmediately()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);

        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry>
            {
                CreateEntry(1)
            }
        });

        Assert.Equal(
            (false, 1),
            (coordinator.HasStagedEnvelope, coordinator.PendingCount));
    }

    [Fact]
    public void StagingANewEnvelopeReplacesPendingEntries()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateCoordinator(1, 2);

        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry>
            {
                CreateEntry(3)
            }
        });

        Assert.Equal(1, coordinator.PendingCount);
    }

    [Fact]
    public void ThrowingFallbackAttemptConsumesTheSliceSpawnBudget()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry>
            {
                CreateEntry(1),
                CreateEntry(2)
            }
        });
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var fallbackAttempts = new List<int>();
        var errors = new List<int>();

        coordinator.Advance(
            nowSeconds: 1d,
            maxItems: 2,
            maxFallbackSpawns: 1,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: entry =>
            {
                fallbackAttempts.Add(entry.NativeSaveId);
                if (entry.NativeSaveId == 1)
                {
                    throw new InvalidOperationException("spawn failed");
                }

                return $"fallback-{entry.NativeSaveId}";
            },
            applyRestored: (_, _) => true,
            onEntryError: (entry, _) =>
                errors.Add(entry.NativeSaveId));

        Assert.Equal(
            ("1", "1", 1, 0),
            (
                string.Join(",", fallbackAttempts),
                string.Join(",", errors),
                coordinator.PendingCount,
                coordinator.StartedFallbackCount));
    }

    [Fact]
    public void RemovingPendingIdentityPreservesRemainingOrder()
    {
        var pending = new NeonLetterPendingColors<int>(
            capacity: 4,
            lifetimeSeconds: 15d);
        NeonRgba color = NeonRgba.ProjectCyan;
        pending.Enqueue(1, color, nowSeconds: 0d);
        pending.Enqueue(2, color, nowSeconds: 0d);
        pending.Enqueue(3, color, nowSeconds: 0d);
        pending.Remove(2);
        var applied = new List<int>();

        pending.ApplyReady(
            nowSeconds: 1d,
            isReady: _ => true,
            apply: (identity, _) => applied.Add(identity));

        Assert.Equal(new[] { 1, 3 }, applied);
    }

    [Fact]
    public void NativeIdentityRecheckPreventsFallbackDuplication()
    {
        NeonLetterMultiplayerSaveEntry entry = CreateEntry(42);
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry> { entry }
        });
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        int observationCount = 0;
        int fallbackCount = 0;
        var restoredTargets = new List<string>();

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, _) => ++observationCount == 1
                ? new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback)
                : new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetReady,
                    Target: "native",
                    ResolvedRecipeId: entry.RecipeId),
            startFallback: _ =>
            {
                fallbackCount++;
                return "fallback";
            },
            applyRestored: (_, target) =>
            {
                restoredTargets.Add(target);
                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (2, 0, "native", 0),
            (
                observationCount,
                fallbackCount,
                string.Join(",", restoredTargets),
                coordinator.PendingCount));
    }

    [Fact]
    public void FailedPendingColorRetriesOnTheNextDrain()
    {
        var state = new NeonLetterReplicatedColorState<int>(
            pendingCapacity: 4,
            pendingLifetimeSeconds: 15d);
        state.Receive(
            identity: 7,
            NeonRgba.ProjectCyan,
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });
        int applyCount = 0;
        int errorCount = 0;

        state.DrainReady(
            nowSeconds: 1d,
            maxItems: 1,
            isReady: _ => true,
            apply: (_, _) =>
            {
                applyCount++;
                throw new InvalidOperationException("not ready");
            },
            onApplyError: (_, _) => errorCount++);
        int finalAppliedCount = state.DrainReady(
            nowSeconds: 1.001d,
            maxItems: 1,
            isReady: _ => true,
            apply: (_, _) => applyCount++,
            onApplyError: (_, _) => errorCount++);

        Assert.Equal(
            (2, 1, 1, 0),
            (applyCount, errorCount, finalAppliedCount, state.PendingCount));
    }

    [Fact]
    public void ReplicatedDrainPropagatesTheFirstApplyFailure()
    {
        var state = new NeonLetterReplicatedColorState<int>(
            pendingCapacity: 4,
            pendingLifetimeSeconds: 15d);
        state.Receive(
            identity: 1,
            NeonRgba.ProjectCyan,
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });
        state.Receive(
            identity: 2,
            NeonRgba.ProjectCyan,
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });
        var firstFailure = new InvalidOperationException("first");
        var secondFailure = new InvalidOperationException("second");
        int applyCount = 0;

        Exception? propagated = Record.Exception(
            () => state.DrainReady(
                nowSeconds: 1d,
                isReady: _ => true,
                apply: (identity, _) =>
                {
                    applyCount++;
                    throw identity == 1
                        ? firstFailure
                        : secondFailure;
                }));

        Assert.Equal(
            (true, 2, 2),
            (
                ReferenceEquals(firstFailure, propagated),
                applyCount,
                state.PendingCount));
    }

    private static RestoreRun RunRestore(int? maxItems)
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = Enumerable.Range(1, 5)
                .Select(CreateEntry)
                .ToList()
        });
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        var events = new List<string>();
        int maximumSliceSize = 0;

        do
        {
            int sliceSize = 0;
            void Advance()
            {
                NeonLetterMultiplayerRestoreObservation<string> Observe(
                    NeonLetterMultiplayerSaveEntry entry,
                    bool fallbackStarted,
                    string? spawnedTarget)
                {
                    _ = fallbackStarted;
                    _ = spawnedTarget;
                    sliceSize++;
                    if (entry.NativeSaveId is 2 or 4)
                    {
                        throw new InvalidOperationException(
                            $"error-{entry.NativeSaveId}");
                    }

                    return new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .NativeTargetReady,
                        Target: $"native-{entry.NativeSaveId}",
                        ResolvedRecipeId: entry.RecipeId);
                }

                bool Apply(
                    NeonLetterMultiplayerSaveEntry entry,
                    string _)
                {
                    events.Add($"restored-{entry.NativeSaveId}");
                    return true;
                }

                void OnError(
                    NeonLetterMultiplayerSaveEntry entry,
                    Exception exception)
                {
                    events.Add(
                        $"error-{entry.NativeSaveId}:{exception.Message}");
                }

                if (maxItems.HasValue)
                {
                    coordinator.Advance(
                        nowSeconds: 1d,
                        maxItems.Value,
                        maxFallbackSpawns: 1,
                        observe: Observe,
                        startFallback: _ => "fallback",
                        applyRestored: Apply,
                        onEntryError: OnError);
                }
                else
                {
                    coordinator.Advance(
                        nowSeconds: 1d,
                        Observe,
                        startFallback: _ => "fallback",
                        Apply,
                        OnError);
                }
            }

            Advance();
            maximumSliceSize = Math.Max(maximumSliceSize, sliceSize);
        }
        while (maxItems.HasValue && coordinator.PendingCount != 0);

        return new RestoreRun(
            string.Join(",", events),
            coordinator.PendingCount,
            maximumSliceSize);
    }

    private static NeonLetterMultiplayerSaveEntry CreateEntry(int nativeSaveId)
    {
        return new NeonLetterMultiplayerSaveEntry
        {
            RecipeId = NeonLetterSmallCatalog.Get('A').RecipeId,
            NativeSaveId = nativeSaveId,
            Position = new NeonVector3(nativeSaveId, 0f, 0f),
            Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
            PackedColor = NeonLetterNetworkProtocol.Pack(NeonRgba.ProjectCyan)
        };
    }

    private static NeonLetterMultiplayerRestoreCoordinator<string>
        CreateCoordinator(params int[] nativeSaveIds)
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = nativeSaveIds
                .Select(CreateEntry)
                .ToList()
        });
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        return coordinator;
    }

    private static NeonLetterPendingColors<int> CreatePendingColors(
        params int[] identities)
    {
        var pending = new NeonLetterPendingColors<int>(
            capacity: Math.Max(1, identities.Length),
            lifetimeSeconds: 15d);
        foreach (int identity in identities)
        {
            pending.Enqueue(identity, NeonRgba.ProjectCyan, nowSeconds: 0d);
        }

        return pending;
    }

    private sealed record RestoreRun(
        string Events,
        int PendingCount,
        int MaximumSliceSize);
}
