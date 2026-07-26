using SOTFNeonLetters;
using Xunit;

public sealed class FallbackRollbackRegressionTests
{
    [Fact]
    public void ApplyFailureAfterFallbackSpawnRollsBackTheOwnedTarget()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        var failure = new InvalidOperationException("apply failed");
        var errors = new List<Exception>();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () => rollbackCount++,
            (_, exception) => errors.Add(exception));

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, target) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .FallbackTargetReady,
                    target),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => throw failure,
            onEntryError: (_, exception) => errors.Add(exception));

        Assert.Equal(
            (1, (Exception)failure, 0),
            (rollbackCount, errors.Single(), coordinator.PendingCount));
    }

    [Fact]
    public void TerminalCallbacksAfterAFailedFallbackDoNotRollBackAgain()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () => rollbackCount++,
            (_, _) => { });
        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, _) => throw new InvalidOperationException("failed"),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, _) => { });

        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Client);
        coordinator.Clear();
        coordinator.Stage(CreateEnvelope());

        Assert.Equal(1, rollbackCount);
    }

    [Fact]
    public void FailureBeforeFallbackSpawnDoesNotRollBackAnUnownedTarget()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        int fallbackStartCount = 0;
        int rollbackCount = 0;

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                throw new InvalidOperationException("observe failed"),
            startFallback: _ =>
            {
                fallbackStartCount++;
                return new RestoreTarget(() => rollbackCount++);
            },
            applyRestored: (_, _) => true,
            onEntryError: (_, _) => { });
        coordinator.Clear();

        Assert.Equal(
            (0, 0, 0),
            (fallbackStartCount, rollbackCount, coordinator.PendingCount));
    }

    [Fact]
    public void NativeRestoreDoesNotInvokeFallbackRollback()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        int rollbackCount = 0;

        coordinator.Advance(
            nowSeconds: 0d,
            observe: (entry, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: new RestoreTarget(() => rollbackCount++),
                    ResolvedRecipeId: entry.RecipeId),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, _) => { });
        coordinator.Clear();

        Assert.Equal((0, 0), (rollbackCount, coordinator.PendingCount));
    }

    [Fact]
    public void SuccessfulFallbackRestoreReleasesWithoutDestroyingTheTarget()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () => rollbackCount++,
            (_, _) => { });

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, target) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .FallbackTargetReady,
                    target),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, _) => { });
        coordinator.Clear();

        Assert.Equal((0, 0), (rollbackCount, coordinator.PendingCount));
    }

    [Fact]
    public void RollbackFailureIsReportedAfterThePrimaryRestoreFailure()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        var primaryFailure = new InvalidOperationException("apply failed");
        var rollbackFailure = new InvalidOperationException("rollback failed");
        var errors = new List<Exception>();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () =>
            {
                rollbackCount++;
                throw rollbackFailure;
            },
            (_, exception) => errors.Add(exception));

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, target) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .FallbackTargetReady,
                    target),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => throw primaryFailure,
            onEntryError: (_, exception) => errors.Add(exception));
        coordinator.Clear();

        Assert.Equal(
            ("apply failed,rollback failed", 1, 0),
            (
                string.Join(",", errors.Select(exception => exception.Message)),
                rollbackCount,
                coordinator.PendingCount));
    }

    [Fact]
    public void FallbackReservationSurvivesArbitraryAttachmentDelay()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        var errors = new List<Exception>();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () => rollbackCount++,
            (_, exception) => errors.Add(exception));

        AdvanceUnavailableFallback(coordinator, nowSeconds: 1d, errors);
        AdvanceUnavailableFallback(
            coordinator,
            nowSeconds: 1_000_000d,
            errors);
        int applyCount = 0;
        uint appliedPackedColor = 0;

        coordinator.Advance(
            nowSeconds: 1_000_001d,
            observe: (_, _, target) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .FallbackTargetReady,
                    target),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (entry, _) =>
            {
                applyCount++;
                appliedPackedColor = entry.PackedColor;
                return true;
            },
            onEntryError: (_, exception) => errors.Add(exception));

        Assert.Equal(
            (
                0,
                0,
                1,
                NeonLetterNetworkProtocol.Pack(NeonRgba.ProjectCyan),
                0),
            (
                rollbackCount,
                errors.Count,
                applyCount,
                appliedPackedColor,
                coordinator.PendingCount));
    }

    [Fact]
    public void FallbackRecipeMismatchRollsBackTheOwnedTarget()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        var mismatch = new InvalidOperationException("recipe mismatch");
        var errors = new List<Exception>();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () => rollbackCount++,
            (_, exception) => errors.Add(exception));

        coordinator.Advance(
            nowSeconds: 1d,
            observe: (_, _, _) => throw mismatch,
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => errors.Add(exception));

        Assert.Equal(
            (1, (Exception)mismatch, 0),
            (rollbackCount, errors.Single(), coordinator.PendingCount));
    }

    [Fact]
    public void ReplacingTheStagedRestoreRollsBackTheOwnedFallback()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () => rollbackCount++,
            (_, _) => { });

        coordinator.Stage(CreateEnvelope());

        Assert.Equal((1, 1), (rollbackCount, coordinator.PendingCount));
    }

    [Fact]
    public void ClearingTheRestoreRollsBackTheOwnedFallback()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () => rollbackCount++,
            (_, _) => { });

        coordinator.Clear();

        Assert.Equal((1, 0), (rollbackCount, coordinator.PendingCount));
    }

    [Fact]
    public void LeavingTheHostRoleRollsBackTheOwnedFallback()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        int rollbackCount = 0;
        ActivateFallback(
            coordinator,
            () => rollbackCount++,
            (_, _) => { });

        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Client);

        Assert.Equal((1, 0), (rollbackCount, coordinator.PendingCount));
    }

    [Fact]
    public void ReentrantStageFromRollbackReplacesTheOuterStage()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        ActivateFallback(
            coordinator,
            () => coordinator.Stage(CreateEnvelope(nativeSaveId: 2)),
            (_, _) => { });

        coordinator.Stage(CreateEnvelope(nativeSaveId: 1));
        var restoredSaveIds = new List<int>();
        coordinator.Advance(
            nowSeconds: 1d,
            observe: (entry, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                    Target: new RestoreTarget(() => { }),
                    ResolvedRecipeId: entry.RecipeId),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (entry, _) =>
            {
                restoredSaveIds.Add(entry.NativeSaveId);
                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            ("2", 0),
            (string.Join(",", restoredSaveIds), coordinator.PendingCount));
    }

    [Fact]
    public void ReentrantClearFromRollbackWinsOverTheOuterStage()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        ActivateFallback(
            coordinator,
            coordinator.Clear,
            (_, _) => { });

        coordinator.Stage(CreateEnvelope(nativeSaveId: 1));

        Assert.Equal(
            (NeonLetterMultiplayerRestoreRole.Unknown, false, 0),
            (
                coordinator.Role,
                coordinator.HasStagedEnvelope,
                coordinator.PendingCount));
    }

    [Fact]
    public void ReentrantRoleChangeFromRollbackWinsOverTheOuterClear()
    {
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator =
            CreateCoordinator();
        ActivateFallback(
            coordinator,
            () => coordinator.SetRole(
                NeonLetterMultiplayerRestoreRole.Client),
            (_, _) => { });

        coordinator.Clear();

        Assert.Equal(
            (NeonLetterMultiplayerRestoreRole.Client, false, 0),
            (
                coordinator.Role,
                coordinator.HasStagedEnvelope,
                coordinator.PendingCount));
    }

    private static void ActivateFallback(
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator,
        Action rollbackFallback,
        Action<NeonLetterMultiplayerSaveEntry, Exception> onEntryError)
    {
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback),
            startFallback: _ => new RestoreTarget(rollbackFallback),
            applyRestored: (_, _) => true,
            onEntryError);
    }

    private static void AdvanceUnavailableFallback(
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget> coordinator,
        double nowSeconds,
        List<Exception> errors)
    {
        coordinator.Advance(
            nowSeconds,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .FallbackTargetUnavailable),
            startFallback: _ => throw new InvalidOperationException(),
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => errors.Add(exception));
    }

    private static NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>
        CreateCoordinator()
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>();
        coordinator.Stage(CreateEnvelope());
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        return coordinator;
    }

    private static NeonLetterMultiplayerSaveEnvelope CreateEnvelope(
        int nativeSaveId = 0)
    {
        return new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry>
            {
                new()
                {
                    RecipeId = NeonLetterSmallCatalog.Get('A').RecipeId,
                    NativeSaveId = nativeSaveId,
                    Position = new NeonVector3(0f, 0f, 0f),
                    Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                    PackedColor = NeonLetterNetworkProtocol.Pack(
                        NeonRgba.ProjectCyan)
                }
            }
        };
    }

    private sealed class RestoreTarget : IDisposable
    {
        private readonly Action _rollback;

        public RestoreTarget(Action rollback)
        {
            _rollback = rollback;
        }

        public void Dispose()
        {
            _rollback();
        }
    }
}
