using SOTFNeonLetters;
using Xunit;

public sealed class SinglePlayerRestoreRetryTests
{
    [Fact]
    public void OneWorldTickAttemptsAtMostSixteenEntriesInStableOrder()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateEnvelope(Enumerable.Range(1, 18).ToArray()),
            nowSeconds: 0d);
        var attemptedSaveIds = new List<int>();

        coordinator.Advance(
            epoch,
            nowSeconds: 1d,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.Equal(
            (
                NeonLetterSinglePlayerRestoreCoordinator.MaxAttemptsPerTick,
                string.Join(
                    ",",
                    Enumerable.Range(
                        1,
                        NeonLetterSinglePlayerRestoreCoordinator
                            .MaxAttemptsPerTick)),
                18),
            (
                attemptedSaveIds.Count,
                string.Join(",", attemptedSaveIds),
                coordinator.PendingCount));
    }

    [Fact]
    public void EntriesBeyondTheFirstTickBudgetAreAttemptedOnTheNextTick()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateEnvelope(Enumerable.Range(1, 18).ToArray()),
            nowSeconds: 0d);
        coordinator.Advance(
            epoch,
            nowSeconds: 1d,
            _ => NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable);
        var nextTickSaveIds = new List<int>();

        coordinator.Advance(
            epoch,
            nowSeconds: 2d,
            entry =>
            {
                nextTickSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.Equal(
            new[] { 17, 18 },
            nextTickSaveIds.Take(2));
    }

    [Fact]
    public void TemporarilyUnavailableTargetRestoresWhenItBecomesReady()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(CreateEnvelope(1), nowSeconds: 10d);
        int applyCount = 0;

        int firstAppliedCount = coordinator.Advance(
            epoch,
            nowSeconds: 11d,
            _ => NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable);
        int secondAppliedCount = coordinator.Advance(
            epoch,
            nowSeconds: 12d,
            _ =>
            {
                applyCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            (0, 1, 1, 0),
            (
                firstAppliedCount,
                secondAppliedCount,
                applyCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void PendingRestoreExpiresAtExactlyFifteenSeconds()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(CreateEnvelope(1), nowSeconds: 20d);
        int attemptCount = 0;
        coordinator.Advance(
            epoch,
            nowSeconds: 34.999d,
            _ =>
            {
                attemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        coordinator.Advance(
            epoch,
            nowSeconds: 35d,
            _ =>
            {
                attemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal((1, 0), (attemptCount, coordinator.PendingCount));
    }

    [Fact]
    public void SuccessfullyRestoredEntryIsNeverAppliedTwice()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(CreateEnvelope(1), nowSeconds: 0d);
        int applyCount = 0;
        Func<
            NeonLetterColorSaveEntry,
            NeonLetterSinglePlayerRestoreAttemptResult> apply = _ =>
            {
                applyCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            };

        coordinator.Advance(epoch, nowSeconds: 1d, apply);
        coordinator.Advance(epoch, nowSeconds: 2d, apply);

        Assert.Equal((1, 0), (applyCount, coordinator.PendingCount));
    }

    [Fact]
    public void PartialBatchRetainsOnlyUnavailableEntries()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(CreateEnvelope(1, 2, 3), nowSeconds: 0d);
        var attemptedSaveIds = new List<int>();

        coordinator.Advance(
            epoch,
            nowSeconds: 1d,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return entry.SaveId switch
                {
                    1 => NeonLetterSinglePlayerRestoreAttemptResult.Applied,
                    2 => NeonLetterSinglePlayerRestoreAttemptResult
                        .TargetUnavailable,
                    _ => NeonLetterSinglePlayerRestoreAttemptResult.Terminal
                };
            });
        coordinator.Advance(
            epoch,
            nowSeconds: 2d,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            ("1,2,3,2", 0),
            (string.Join(",", attemptedSaveIds), coordinator.PendingCount));
    }

    [Fact]
    public void MalformedEntriesAndTerminalMismatchAreNotRetried()
    {
        int knownRecipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            new NeonLetterColorSaveEnvelope
            {
                Entries = new List<NeonLetterColorSaveEntry>
                {
                    null!,
                    new(1, int.MinValue, NeonRgba.ProjectCyan),
                    new(
                        2,
                        knownRecipeId,
                        new NeonRgba(float.NaN, 0f, 0f, 1f)),
                    new(3, knownRecipeId, NeonRgba.ProjectCyan)
                }
            },
            nowSeconds: 0d);
        int mismatchAttemptCount = 0;

        coordinator.Advance(
            epoch,
            nowSeconds: 1d,
            _ =>
            {
                mismatchAttemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Terminal;
            });
        coordinator.Advance(
            epoch,
            nowSeconds: 2d,
            _ =>
            {
                mismatchAttemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal((1, 0), (mismatchAttemptCount, coordinator.PendingCount));
    }

    [Fact]
    public void RestagingReplacesThePreviousEpochWithoutDuplicatingEntries()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long staleEpoch = coordinator.Stage(CreateEnvelope(1, 2), nowSeconds: 0d);
        long currentEpoch = coordinator.Stage(CreateEnvelope(3), nowSeconds: 1d);
        var appliedSaveIds = new List<int>();

        coordinator.Advance(
            staleEpoch,
            nowSeconds: 2d,
            entry =>
            {
                appliedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });
        coordinator.Advance(
            currentEpoch,
            nowSeconds: 2d,
            entry =>
            {
                appliedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            ("3", 0),
            (string.Join(",", appliedSaveIds), coordinator.PendingCount));
    }

    [Fact]
    public void CancelPreventsAStaleWorldUpdateFromApplying()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long staleEpoch = coordinator.Stage(CreateEnvelope(1), nowSeconds: 0d);
        int applyCount = 0;

        coordinator.Cancel();
        coordinator.Advance(
            staleEpoch,
            nowSeconds: 1d,
            _ =>
            {
                applyCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal((0, 0), (applyCount, coordinator.PendingCount));
    }

    [Fact]
    public void RestageDuringAnAttemptStopsTheStaleTick()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long firstEpoch = coordinator.Stage(
            CreateEnvelope(1, 2),
            nowSeconds: 0d);
        var attemptedSaveIds = new List<int>();

        coordinator.Advance(
            firstEpoch,
            nowSeconds: 1d,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                coordinator.Stage(CreateEnvelope(3), nowSeconds: 1d);
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            ("1", 1),
            (string.Join(",", attemptedSaveIds), coordinator.PendingCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnavailableManagerOrSaveIdTargetRemainsRetryable(
        int observationKindValue)
    {
        NeonLetterColorSaveEntry entry = CreateEnvelope(1).Entries.Single();
        var target = new SinglePlayerRestoreTarget(entry.RecipeId);
        var observationKind =
            (NeonLetterSinglePlayerRestoreTargetObservationKind)
                observationKindValue;

        NeonLetterSinglePlayerRestoreAttemptResult result =
            NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                entry,
                new NeonLetterSinglePlayerRestoreTargetObservation(
                    observationKind,
                    target,
                    entry.RecipeId),
                _ => { });

        Assert.Equal(
            (NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable, 0),
            (result, target.ApplyCount));
    }

    [Fact]
    public void ResolvedStructureThatCannotBecomeARestoreTargetIsTerminal()
    {
        NeonLetterColorSaveEntry entry = CreateEnvelope(1).Entries.Single();

        NeonLetterSinglePlayerRestoreAttemptResult result =
            NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                entry,
                new NeonLetterSinglePlayerRestoreTargetObservation(
                    NeonLetterSinglePlayerRestoreTargetObservationKind.Resolved,
                    Target: null,
                    entry.RecipeId),
                _ => { });

        Assert.Equal(
            NeonLetterSinglePlayerRestoreAttemptResult.Terminal,
            result);
    }

    [Fact]
    public void ResolvedStructureWithoutARecipeIsTerminal()
    {
        NeonLetterColorSaveEntry entry = CreateEnvelope(1).Entries.Single();
        var target = new SinglePlayerRestoreTarget(entry.RecipeId);

        NeonLetterSinglePlayerRestoreAttemptResult result =
            NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                entry,
                new NeonLetterSinglePlayerRestoreTargetObservation(
                    NeonLetterSinglePlayerRestoreTargetObservationKind.Resolved,
                    target,
                    ResolvedRecipeId: null),
                _ => { });

        Assert.Equal(
            (NeonLetterSinglePlayerRestoreAttemptResult.Terminal, 0),
            (result, target.ApplyCount));
    }

    [Fact]
    public void ResolvedRecipeMismatchIsTerminal()
    {
        NeonLetterColorSaveEntry entry = CreateEnvelope(1).Entries.Single();
        var target = new SinglePlayerRestoreTarget(entry.RecipeId);
        int differentRecipeId = NeonLetterSmallCatalog.Get('B').RecipeId;

        NeonLetterSinglePlayerRestoreAttemptResult result =
            NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                entry,
                new NeonLetterSinglePlayerRestoreTargetObservation(
                    NeonLetterSinglePlayerRestoreTargetObservationKind.Resolved,
                    target,
                    differentRecipeId),
                _ => { });

        Assert.Equal(
            (NeonLetterSinglePlayerRestoreAttemptResult.Terminal, 0),
            (result, target.ApplyCount));
    }

    [Fact]
    public void ApplyFailureIsTerminalAndAttemptedOnlyOnce()
    {
        var lifecycle = new NeonLetterSinglePlayerRestoreLifecycle();
        lifecycle.SetSinglePlayerRole(isSinglePlayer: true);
        lifecycle.Stage(CreateEnvelope(1), nowSeconds: 0d);
        NeonLetterColorSaveEntry entry = CreateEnvelope(1).Entries.Single();
        var target = new SinglePlayerRestoreTarget(
            entry.RecipeId,
            throwOnApply: true);
        int errorCount = 0;
        Func<
            NeonLetterColorSaveEntry,
            NeonLetterSinglePlayerRestoreAttemptResult> attempt = savedEntry =>
                NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                    savedEntry,
                    new NeonLetterSinglePlayerRestoreTargetObservation(
                        NeonLetterSinglePlayerRestoreTargetObservationKind
                            .Resolved,
                        target,
                        savedEntry.RecipeId),
                    _ => errorCount++);

        lifecycle.Advance(nowSeconds: 1d, attempt);
        lifecycle.Advance(nowSeconds: 2d, attempt);

        Assert.Equal(
            (1, 1, 0),
            (target.ApplyCount, errorCount, lifecycle.PendingCount));
    }

    [Fact]
    public void ApplyFailureRemainsTerminalWhenErrorReporterThrows()
    {
        var lifecycle = CreateStagedLifecycle();
        NeonLetterColorSaveEntry entry = CreateEnvelope(1).Entries.Single();
        var target = new SinglePlayerRestoreTarget(
            entry.RecipeId,
            throwOnApply: true);
        int errorReportCount = 0;
        Func<
            NeonLetterColorSaveEntry,
            NeonLetterSinglePlayerRestoreAttemptResult> attempt = savedEntry =>
                NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                    savedEntry,
                    new NeonLetterSinglePlayerRestoreTargetObservation(
                        NeonLetterSinglePlayerRestoreTargetObservationKind
                            .Resolved,
                        target,
                        savedEntry.RecipeId),
                    _ =>
                    {
                        errorReportCount++;
                        throw new InvalidOperationException("report failed");
                    });

        Exception? firstError = Record.Exception(
            () => lifecycle.Advance(nowSeconds: 1d, attempt));
        Exception? staleTickError = Record.Exception(
            () => lifecycle.Advance(nowSeconds: 2d, attempt));

        Assert.Equal(
            (null, null, 1, 1, 0),
            (
                firstError?.GetType(),
                staleTickError?.GetType(),
                target.ApplyCount,
                errorReportCount,
                lifecycle.PendingCount));
    }

    [Fact]
    public void ClassificationFailureRemainsTerminalWhenErrorReporterThrows()
    {
        var lifecycle = CreateStagedLifecycle();
        NeonLetterColorSaveEntry entry = CreateEnvelope(1).Entries.Single();
        var target = new SinglePlayerRestoreTarget(
            entry.RecipeId,
            throwOnRecipeRead: true);
        int errorReportCount = 0;
        Func<
            NeonLetterColorSaveEntry,
            NeonLetterSinglePlayerRestoreAttemptResult> attempt = savedEntry =>
                NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                    savedEntry,
                    new NeonLetterSinglePlayerRestoreTargetObservation(
                        NeonLetterSinglePlayerRestoreTargetObservationKind
                            .Resolved,
                        target,
                        savedEntry.RecipeId),
                    _ =>
                    {
                        errorReportCount++;
                        throw new InvalidOperationException("report failed");
                    });

        Exception? firstError = Record.Exception(
            () => lifecycle.Advance(nowSeconds: 1d, attempt));
        Exception? staleTickError = Record.Exception(
            () => lifecycle.Advance(nowSeconds: 2d, attempt));

        Assert.Equal(
            (null, null, 0, 1, 0),
            (
                firstError?.GetType(),
                staleTickError?.GetType(),
                target.ApplyCount,
                errorReportCount,
                lifecycle.PendingCount));
    }

    [Fact]
    public void WorldExitCancelsPendingRestoreBeforeAStaleTick()
    {
        var lifecycle = CreateStagedLifecycle();
        int applyCount = 0;

        lifecycle.OnWorldExited();
        lifecycle.Advance(
            nowSeconds: 1d,
            _ =>
            {
                applyCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal((0, 0), (applyCount, lifecycle.PendingCount));
    }

    [Fact]
    public void DeinitializeCancelsPendingRestoreBeforeAStaleTick()
    {
        var lifecycle = CreateStagedLifecycle();
        int applyCount = 0;

        lifecycle.Deinitialize();
        lifecycle.Advance(
            nowSeconds: 1d,
            _ =>
            {
                applyCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal((0, 0), (applyCount, lifecycle.PendingCount));
    }

    [Fact]
    public void LeavingSinglePlayerCancelsPendingRestoreBeforeAStaleTick()
    {
        var lifecycle = CreateStagedLifecycle();
        int applyCount = 0;

        lifecycle.SetSinglePlayerRole(isSinglePlayer: false);
        lifecycle.Advance(
            nowSeconds: 1d,
            _ =>
            {
                applyCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal((0, 0), (applyCount, lifecycle.PendingCount));
    }

    [Fact]
    public void RepeatedRestoreTriggerReplacesEntriesWithoutDuplicateApply()
    {
        var lifecycle = new NeonLetterSinglePlayerRestoreLifecycle();
        lifecycle.SetSinglePlayerRole(isSinglePlayer: true);
        NeonLetterColorSaveEnvelope envelope = CreateEnvelope(1, 2);
        var targets = envelope.Entries.ToDictionary(
            entry => entry.SaveId,
            entry => new SinglePlayerRestoreTarget(entry.RecipeId));

        lifecycle.Stage(envelope, nowSeconds: 0d);
        lifecycle.Stage(envelope, nowSeconds: 0.1d);
        lifecycle.Advance(
            nowSeconds: 1d,
            entry => NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                entry,
                new NeonLetterSinglePlayerRestoreTargetObservation(
                    NeonLetterSinglePlayerRestoreTargetObservationKind.Resolved,
                    targets[entry.SaveId],
                    entry.RecipeId),
                _ => { }));

        Assert.Equal(
            (1, 1, 0),
            (
                targets[1].ApplyCount,
                targets[2].ApplyCount,
                lifecycle.PendingCount));
    }

    private static NeonLetterSinglePlayerRestoreLifecycle
        CreateStagedLifecycle()
    {
        var lifecycle = new NeonLetterSinglePlayerRestoreLifecycle();
        lifecycle.SetSinglePlayerRole(isSinglePlayer: true);
        lifecycle.Stage(CreateEnvelope(1), nowSeconds: 0d);
        return lifecycle;
    }

    private static NeonLetterColorSaveEnvelope CreateEnvelope(
        params int[] saveIds)
    {
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        return new NeonLetterColorSaveEnvelope
        {
            Entries = saveIds
                .Select(
                    saveId => new NeonLetterColorSaveEntry(
                        saveId,
                        recipeId,
                        NeonRgba.ProjectCyan))
                .ToList()
        };
    }

    private sealed class SinglePlayerRestoreTarget
        : INeonLetterColorRestoreTarget
    {
        private readonly bool _throwOnApply;
        private readonly bool _throwOnRecipeRead;
        private readonly int _recipeId;

        internal SinglePlayerRestoreTarget(
            int recipeId,
            bool throwOnApply = false,
            bool throwOnRecipeRead = false)
        {
            _recipeId = recipeId;
            _throwOnApply = throwOnApply;
            _throwOnRecipeRead = throwOnRecipeRead;
        }

        public int RecipeId
        {
            get
            {
                if (_throwOnRecipeRead)
                {
                    throw new InvalidOperationException(
                        "recipe classification failed");
                }

                return _recipeId;
            }
        }

        internal int ApplyCount { get; private set; }

        public void Apply(NeonRgba color)
        {
            ApplyCount++;
            if (_throwOnApply)
            {
                throw new InvalidOperationException("apply failed");
            }
        }
    }
}
