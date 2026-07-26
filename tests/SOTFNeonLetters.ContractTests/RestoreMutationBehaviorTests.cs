using SOTFNeonLetters;
using Xunit;

public sealed class RestoreMutationBehaviorTests
{
    [Theory]
    [InlineData(0, "entry")]
    [InlineData(1, "onApplyError")]
    public void RestoreAttemptRequiresItsEntryAndErrorReporter(
        int invalidInput,
        string expectedParameterName)
    {
        NeonLetterColorSaveEntry entry =
            invalidInput == 0 ? null! : CreateEntry(saveId: 1);
        Action<Exception> onApplyError =
            invalidInput == 1 ? null! : _ => { };

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                    entry,
                    new NeonLetterSinglePlayerRestoreTargetObservation(
                        NeonLetterSinglePlayerRestoreTargetObservationKind
                            .TargetUnavailable),
                    onApplyError));

        Assert.Equal(expectedParameterName, exception.ParamName);
    }

    [Fact]
    public void ResolvedObservationRequiresAResolvedRecipeIdentity()
    {
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        var target = new RestoreTarget(recipeId);
        var entry = CreateEntry(saveId: 1, recipeId);
        int errorCount = 0;

        NeonLetterSinglePlayerRestoreAttemptResult result =
            NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                entry,
                new NeonLetterSinglePlayerRestoreTargetObservation(
                    NeonLetterSinglePlayerRestoreTargetObservationKind.Resolved,
                    target,
                    ResolvedRecipeId: null),
                _ => errorCount++);

        Assert.Equal(
            (
                NeonLetterSinglePlayerRestoreAttemptResult.Terminal,
                0,
                0),
            (result, target.ApplyCount, errorCount));
    }

    [Fact]
    public void InvalidEnvelopeRestageClearsPreviouslyPendingEntries()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long staleEpoch = coordinator.Stage(
            CreateEnvelope(CreateEntry(saveId: 1)),
            nowSeconds: 0d);
        var invalidEnvelope = CreateEnvelope(CreateEntry(saveId: 2));
        invalidEnvelope.Version++;

        long currentEpoch = coordinator.Stage(
            invalidEnvelope,
            nowSeconds: 1d);
        int callbackCount = 0;
        int staleAppliedCount = coordinator.Advance(
            staleEpoch,
            nowSeconds: 2d,
            _ =>
            {
                callbackCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            (true, 0, 0, 0),
            (
                currentEpoch != staleEpoch,
                staleAppliedCount,
                callbackCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void DuplicateSaveIdentityUsesTheLastStagedColorOnce()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        NeonRgba firstColor = new(0.1f, 0.2f, 0.3f, 1f);
        NeonRgba lastColor = new(0.7f, 0.8f, 0.9f, 1f);
        long epoch = coordinator.Stage(
            CreateEnvelope(
                new NeonLetterColorSaveEntry(
                    saveId: 1,
                    recipeId,
                    firstColor),
                new NeonLetterColorSaveEntry(
                    saveId: 1,
                    recipeId,
                    lastColor)),
            nowSeconds: 0d);
        var attemptedColors = new List<NeonRgba>();

        int appliedCount = coordinator.Advance(
            epoch,
            nowSeconds: 1d,
            entry =>
            {
                attemptedColors.Add(entry.Color);
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            (1, lastColor, 0),
            (
                appliedCount,
                attemptedColors.Single(),
                coordinator.PendingCount));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RestoreStageRejectsInvalidTime(double nowSeconds)
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => coordinator.Stage(
                    CreateEnvelope(CreateEntry(saveId: 1)),
                    nowSeconds));

        Assert.Equal("nowSeconds", exception.ParamName);
    }

    [Fact]
    public void RestoreAdvanceRequiresAnAttemptCallback()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateEnvelope(CreateEntry(saveId: 1)),
            nowSeconds: 0d);

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => coordinator.Advance(
                    epoch,
                    nowSeconds: 1d,
                    attempt: null!));

        Assert.Equal("attempt", exception.ParamName);
    }

    [Fact]
    public void RestoreCanAdvanceAtItsStagedStartTime()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateEnvelope(CreateEntry(saveId: 1)),
            nowSeconds: 10d);
        int attemptCount = 0;

        int appliedCount = coordinator.Advance(
            epoch,
            nowSeconds: 10d,
            _ =>
            {
                attemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.Equal(
            (0, 1, 1),
            (appliedCount, attemptCount, coordinator.PendingCount));
    }

    [Fact]
    public void NestedAdvanceRemovingCurrentEntryStopsTheOuterAttempt()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateEnvelope(CreateEntry(saveId: 1)),
            nowSeconds: 0d);
        int nestedAppliedCount = -1;

        Exception? outerError = Record.Exception(
            () => coordinator.Advance(
                epoch,
                nowSeconds: 1d,
                _ =>
                {
                    nestedAppliedCount = coordinator.Advance(
                        epoch,
                        nowSeconds: 1d,
                        _ => NeonLetterSinglePlayerRestoreAttemptResult.Applied);
                    return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
                }));

        Assert.Equal(
            (null, 1, 0),
            (
                outerError?.GetType(),
                nestedAppliedCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void RemovingAMiddleTerminalEntryContinuesWithItsSuccessor()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateEnvelope(
                CreateEntry(saveId: 1),
                CreateEntry(saveId: 2),
                CreateEntry(saveId: 3)),
            nowSeconds: 0d);
        var attemptedSaveIds = new List<int>();

        coordinator.Advance(
            epoch,
            nowSeconds: 1d,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return entry.SaveId == 2
                    ? NeonLetterSinglePlayerRestoreAttemptResult.Terminal
                    : NeonLetterSinglePlayerRestoreAttemptResult
                        .TargetUnavailable;
            });

        Assert.Equal("1,2,3", string.Join(",", attemptedSaveIds));
    }

    [Fact]
    public void WorldExitPreventsADeferredRestoreFromBeingRestaged()
    {
        var lifecycle = new NeonLetterSinglePlayerRestoreLifecycle();
        lifecycle.SetSinglePlayerRole(isSinglePlayer: true);
        lifecycle.OnWorldExited();
        lifecycle.Stage(
            CreateEnvelope(CreateEntry(saveId: 1)),
            nowSeconds: 0d);
        int callbackCount = 0;

        int appliedCount = lifecycle.Advance(
            nowSeconds: 1d,
            _ =>
            {
                callbackCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            (0, 0, 0),
            (appliedCount, callbackCount, lifecycle.PendingCount));
    }

    private static NeonLetterColorSaveEnvelope CreateEnvelope(
        params NeonLetterColorSaveEntry[] entries)
    {
        return new NeonLetterColorSaveEnvelope
        {
            Entries = entries.ToList()
        };
    }

    private static NeonLetterColorSaveEntry CreateEntry(
        int saveId,
        int? recipeId = null)
    {
        return new NeonLetterColorSaveEntry(
            saveId,
            recipeId ?? NeonLetterSmallCatalog.Get('A').RecipeId,
            NeonRgba.ProjectCyan);
    }

    private sealed class RestoreTarget : INeonLetterColorRestoreTarget
    {
        internal RestoreTarget(int recipeId)
        {
            RecipeId = recipeId;
        }

        public int RecipeId { get; }
        internal int ApplyCount { get; private set; }

        public void Apply(NeonRgba color)
        {
            ApplyCount++;
        }
    }
}
