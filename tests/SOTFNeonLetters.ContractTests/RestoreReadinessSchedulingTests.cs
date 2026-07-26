using SOTFNeonLetters;
using Xunit;

public sealed class RestoreReadinessSchedulingTests
{
    [Fact]
    public void UnchangedReadinessUsesBoundedSafetyProbesAcrossOneHundredThousandUpdates()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();
        int probeCount = 0;

        for (long updateTick = 0; updateTick < 100_000; updateTick++)
        {
            if (scheduler.TryGetDueToken(
                    observedProgress: 1,
                    updateTick,
                    waveActive: false,
                    out _))
            {
                probeCount++;
            }
        }

        Assert.Equal(104, probeCount);
    }

    [Fact]
    public void ProgressChangeIssuesANewReadinessTokenImmediately()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();

        scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 0,
            waveActive: false,
            out ulong firstToken);
        bool unchangedDue = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 1,
            waveActive: false,
            out _);
        bool changedDue = scheduler.TryGetDueToken(
            observedProgress: 2,
            updateTick: 1,
            waveActive: false,
            out ulong changedToken);

        Assert.Equal(
            (false, true, true),
            (unchangedDue, changedDue, changedToken > firstToken));
    }

    [Fact]
    public void ActiveWaveDefersAnOverdueSafetyProbe()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();
        scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 0,
            waveActive: false,
            out ulong firstToken);

        bool activeWaveProbe = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 100,
            waveActive: true,
            out ulong activeWaveToken);
        bool earlyProbe = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 107,
            waveActive: false,
            out _);
        bool rescheduledProbe = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 108,
            waveActive: false,
            out ulong rescheduledToken);

        Assert.Equal(
            (false, true, false, true),
            (
                activeWaveProbe,
                activeWaveToken == firstToken,
                earlyProbe,
                rescheduledProbe && rescheduledToken > firstToken));
    }

    [Fact]
    public void SaturatedUpdateTickDoesNotRepeatSafetyProbe()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();
        scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: long.MaxValue,
            waveActive: false,
            out ulong firstToken);

        bool firstRepeatedProbe = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: long.MaxValue,
            waveActive: false,
            out ulong repeatedToken);
        bool secondRepeatedProbe = scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: long.MaxValue,
            waveActive: false,
            out _);

        Assert.Equal(
            (false, false, true),
            (
                firstRepeatedProbe,
                secondRepeatedProbe,
                repeatedToken == firstToken));
    }

    [Fact]
    public void MonotonicSequenceFailsClosedBeforeWrappingToZero()
    {
        var sequence = new NeonLetterMonotonicSequence(
            initialValue: ulong.MaxValue - 1);

        ulong lastValue = sequence.Advance();
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                {
                    sequence.Advance();
                });

        Assert.Equal(
            (ulong.MaxValue, ulong.MaxValue, true),
            (
                lastValue,
                sequence.Current,
                exception.Message.Contains(
                    "exhausted",
                    StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ReadinessTokensFailClosedBeforeReuse()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>(
            new NeonLetterMonotonicSequence(ulong.MaxValue - 1));
        scheduler.TryGetDueToken(
            observedProgress: 1,
            updateTick: 0,
            waveActive: false,
            out ulong lastToken);

        Assert.Throws<InvalidOperationException>(
            () => scheduler.TryGetDueToken(
                observedProgress: 2,
                updateTick: 1,
                waveActive: false,
                out _));

        Assert.Equal(ulong.MaxValue, lastToken);
    }

    [Fact]
    public void ExhaustedSinglePlayerEpochCancelsOldRestoreAndFailsClosed()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator(
            new NeonLetterMonotonicSequence(
                initialValue: (ulong)long.MaxValue - 1));
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(1),
            nowSeconds: 0d);
        int attemptCount = 0;

        Assert.Throws<InvalidOperationException>(coordinator.Cancel);
        coordinator.Advance(
            epoch,
            readinessToken: 1,
            _ =>
            {
                attemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal(
            (0, 0),
            (attemptCount, coordinator.PendingCount));
    }

    [Fact]
    public void ParkedSinglePlayerEntryUsesBoundedAttemptsAcrossOneHundredThousandUpdates()
    {
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(CreateSinglePlayerEnvelope(1), nowSeconds: 0d);
        int attemptCount = 0;
        int probeCount = 0;

        for (long updateTick = 0; updateTick < 100_000; updateTick++)
        {
            bool waveActive =
                scheduler.CurrentToken != 0 &&
                coordinator.HasWorkForToken(epoch, scheduler.CurrentToken);
            bool probeDue = scheduler.TryGetDueToken(
                observedProgress: 1,
                updateTick,
                waveActive,
                out ulong token);
            if (probeDue)
            {
                probeCount++;
            }

            if (probeDue || coordinator.HasWorkForToken(epoch, token))
            {
                coordinator.Advance(
                    epoch,
                    token,
                    _ =>
                    {
                        attemptCount++;
                        return NeonLetterSinglePlayerRestoreAttemptResult
                            .TargetUnavailable;
                    });
            }
        }

        Assert.Equal(1, coordinator.PendingCount);
        Assert.InRange(attemptCount + probeCount, 4, 256);
    }

    [Fact]
    public void SinglePlayerWaveContinuesSixteenAtATimeWithoutNewToken()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(Enumerable.Range(1, 18).ToArray()),
            nowSeconds: 0d);
        const ulong readinessToken = 1;
        var attemptedSaveIds = new List<int>();

        coordinator.Advance(
            epoch,
            readinessToken,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.True(coordinator.HasWorkForToken(epoch, readinessToken));

        coordinator.Advance(
            epoch,
            readinessToken,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.False(coordinator.HasWorkForToken(epoch, readinessToken));

        coordinator.Advance(
            epoch,
            readinessToken,
            _ => throw new InvalidOperationException(
                "A completed readiness wave cannot retry parked entries."));

        Assert.Equal(Enumerable.Range(1, 18), attemptedSaveIds);
    }

    [Fact]
    public void SinglePlayerProgressChangeWakesParkedEntryAfterArbitraryDelay()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(1),
            nowSeconds: 0d);
        int attemptCount = 0;

        coordinator.Advance(
            epoch,
            readinessToken: 1,
            _ =>
            {
                attemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        coordinator.Advance(
            epoch,
            readinessToken: 1,
            _ => throw new InvalidOperationException(
                "An unchanged readiness token cannot retry a parked entry."));

        int applied = coordinator.Advance(
            epoch,
            readinessToken: 2,
            _ =>
            {
                attemptCount++;
                return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
            });

        Assert.Equal((1, 0, 2), (applied, coordinator.PendingCount, attemptCount));
    }

    [Fact]
    public void ReentrantSinglePlayerWaveDoesNotRepeatReservedEntries()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(1, 2, 3),
            nowSeconds: 0d);
        const ulong readinessToken = 1;
        var attemptedSaveIds = new List<int>();
        bool nestedAdvanceStarted = false;

        NeonLetterSinglePlayerRestoreAttemptResult Attempt(
            NeonLetterColorSaveEntry entry)
        {
            attemptedSaveIds.Add(entry.SaveId);
            if (!nestedAdvanceStarted)
            {
                nestedAdvanceStarted = true;
                coordinator.Advance(epoch, readinessToken, Attempt);
            }

            return NeonLetterSinglePlayerRestoreAttemptResult
                .TargetUnavailable;
        }

        coordinator.Advance(epoch, readinessToken, Attempt);

        Assert.Equal(new[] { 1, 2, 3 }, attemptedSaveIds);
    }

    [Fact]
    public void ReentrantSinglePlayerTokenWaitsForTheNextUpdate()
    {
        var coordinator = new NeonLetterSinglePlayerRestoreCoordinator();
        long epoch = coordinator.Stage(
            CreateSinglePlayerEnvelope(
                Enumerable.Range(1, 40).ToArray()),
            nowSeconds: 0d);
        var attemptedSaveIds = new List<int>();
        bool nestedAdvanceRequested = false;

        coordinator.Advance(
            epoch,
            readinessToken: 1,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                if (!nestedAdvanceRequested)
                {
                    nestedAdvanceRequested = true;
                    coordinator.Advance(
                        epoch,
                        readinessToken: 2,
                        nestedEntry =>
                        {
                            attemptedSaveIds.Add(nestedEntry.SaveId);
                            return NeonLetterSinglePlayerRestoreAttemptResult
                                .TargetUnavailable;
                        });
                }

                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.Equal(Enumerable.Range(1, 16), attemptedSaveIds);

        coordinator.Advance(
            epoch,
            readinessToken: 2,
            entry =>
            {
                attemptedSaveIds.Add(entry.SaveId);
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.Equal(Enumerable.Range(1, 32), attemptedSaveIds);
    }

    [Fact]
    public void SinglePlayerRoleLossCancelsAnActiveReadinessWave()
    {
        var lifecycle = new NeonLetterSinglePlayerRestoreLifecycle();
        lifecycle.SetSinglePlayerRole(isSinglePlayer: true);
        lifecycle.Stage(
            CreateSinglePlayerEnvelope(
                Enumerable.Range(1, 18).ToArray()),
            nowSeconds: 0d);
        int attemptCount = 0;

        lifecycle.Advance(
            readinessToken: 1,
            _ =>
            {
                attemptCount++;
                lifecycle.SetSinglePlayerRole(isSinglePlayer: false);
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;
            });

        Assert.Equal((1, 0), (attemptCount, lifecycle.PendingCount));
    }

    [Fact]
    public void ParkedMultiplayerEntryUsesBoundedAttemptsAcrossOneHundredThousandUpdates()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(1);
        var scheduler = new NeonLetterRestoreReadinessScheduler<int>();
        int attemptCount = 0;
        int probeCount = 0;

        for (long updateTick = 0; updateTick < 100_000; updateTick++)
        {
            bool waveActive =
                scheduler.CurrentToken != 0 &&
                coordinator.HasWorkForToken(scheduler.CurrentToken);
            bool probeDue = scheduler.TryGetDueToken(
                observedProgress: 1,
                updateTick,
                waveActive,
                out ulong token);
            if (probeDue)
            {
                probeCount++;
            }

            if (probeDue || coordinator.HasWorkForToken(token))
            {
                coordinator.AdvanceForReadinessToken(
                    readinessToken: token,
                    maxItems: 16,
                    maxFallbackSpawns: 2,
                    observe: (_, _, _) =>
                    {
                        attemptCount++;
                        return new NeonLetterMultiplayerRestoreObservation<string>(
                            NeonLetterMultiplayerRestoreObservationKind
                                .ProcessedRecipeUnavailable);
                    },
                    startFallback: _ => "fallback",
                    applyRestored: (_, _) => true,
                    onEntryError: (_, exception) => throw exception);
            }
        }

        Assert.Equal(1, coordinator.PendingCount);
        Assert.InRange(attemptCount + probeCount, 4, 256);
    }

    [Fact]
    public void MultiplayerWaveContinuesSixteenAtATimeWithoutNewToken()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(18);
        const ulong readinessToken = 1;
        var attemptedSaveIds = new List<int>();

        void Advance()
        {
            coordinator.AdvanceForReadinessToken(
                readinessToken,
                maxItems: 16,
                maxFallbackSpawns: 2,
                observe: (entry, _, _) =>
                {
                    attemptedSaveIds.Add(entry.NativeSaveId);
                    return new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .ProcessedRecipeUnavailable);
                },
                startFallback: _ => "fallback",
                applyRestored: (_, _) => true,
                onEntryError: (_, exception) => throw exception);
        }

        Advance();
        Assert.True(coordinator.HasWorkForToken(readinessToken));

        Advance();
        Assert.False(coordinator.HasWorkForToken(readinessToken));

        Advance();
        Assert.Equal(Enumerable.Range(1, 18), attemptedSaveIds);
    }

    [Fact]
    public void MultiplayerWaveStartsAtMostTwoFallbacksPerUpdate()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(
                count: 5,
                includeNativeSaveIds: false);
        int fallbackStartCount = 0;

        void Advance(ulong readinessToken)
        {
            coordinator.AdvanceForReadinessToken(
                readinessToken,
                maxItems: 16,
                maxFallbackSpawns: 2,
                observe: (_, _, _) =>
                    new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .ReadyToSpawnFallback),
                startFallback: _ =>
                {
                    fallbackStartCount++;
                    return "fallback";
                },
                applyRestored: (_, _) => true,
                onEntryError: (_, exception) => throw exception);
        }

        Advance(readinessToken: 1);
        Assert.Equal(2, fallbackStartCount);

        Advance(readinessToken: 1);
        Assert.Equal(2, fallbackStartCount);

        Advance(readinessToken: 2);
        Assert.Equal(4, fallbackStartCount);
    }

    [Fact]
    public void MultiplayerCoordinatorClampsCallerBudgetsToGlobalCaps()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(
                count: 40,
                includeNativeSaveIds: false);
        int observeCount = 0;
        int fallbackStartCount = 0;

        coordinator.Advance(
            nowSeconds: 0d,
            maxItems: 100,
            maxFallbackSpawns: 100,
            observe: (_, _, _) =>
            {
                observeCount++;
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ReadyToSpawnFallback);
            },
            startFallback: _ =>
            {
                fallbackStartCount++;
                return "fallback";
            },
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal((16, 2), (observeCount, fallbackStartCount));
    }

    [Fact]
    public void MultiplayerRoleLossCancelsAnActiveReadinessWave()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(18);
        int attemptCount = 0;

        coordinator.AdvanceForReadinessToken(
            readinessToken: 1,
            maxItems: 16,
            maxFallbackSpawns: 2,
            observe: (_, _, _) =>
            {
                attemptCount++;
                coordinator.SetRole(
                    NeonLetterMultiplayerRestoreRole.Client);
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (1, NeonLetterMultiplayerRestoreRole.Client, 0),
            (attemptCount, coordinator.Role, coordinator.PendingCount));
    }

    [Fact]
    public void ReentrantMultiplayerTokenWaitsForTheNextUpdate()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(40);
        var observedSaveIds = new List<int>();
        bool nestedAdvanceRequested = false;

        coordinator.AdvanceForReadinessToken(
            readinessToken: 1,
            maxItems: 16,
            maxFallbackSpawns: 2,
            observe: (entry, _, _) =>
            {
                observedSaveIds.Add(entry.NativeSaveId);
                if (!nestedAdvanceRequested)
                {
                    nestedAdvanceRequested = true;
                    coordinator.AdvanceForReadinessToken(
                        readinessToken: 2,
                        maxItems: 16,
                        maxFallbackSpawns: 2,
                        observe: (nestedEntry, _, _) =>
                        {
                            observedSaveIds.Add(
                                nestedEntry.NativeSaveId);
                            return new
                                NeonLetterMultiplayerRestoreObservation<string>(
                                    NeonLetterMultiplayerRestoreObservationKind
                                        .ProcessedRecipeUnavailable);
                        },
                        startFallback: _ => "fallback",
                        applyRestored: (_, _) => true,
                        onEntryError: (_, exception) => throw exception);
                }

                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(Enumerable.Range(1, 16), observedSaveIds);

        coordinator.AdvanceForReadinessToken(
            readinessToken: 2,
            maxItems: 16,
            maxFallbackSpawns: 2,
            observe: (entry, _, _) =>
            {
                observedSaveIds.Add(entry.NativeSaveId);
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(Enumerable.Range(1, 32), observedSaveIds);
    }

    [Fact]
    public void ReentrantMultiplayerApplyWaitsForTheNextUpdate()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(40);
        var appliedSaveIds = new List<int>();
        int nestedObserveCount = 0;
        bool nestedAdvanceRequested = false;

        coordinator.AdvanceForReadinessToken(
            readinessToken: 1,
            maxItems: 16,
            maxFallbackSpawns: 2,
            observe: (entry, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetReady,
                    Target: $"native-{entry.NativeSaveId}",
                    ResolvedRecipeId: entry.RecipeId),
            startFallback: _ => "fallback",
            applyRestored: (entry, _) =>
            {
                appliedSaveIds.Add(entry.NativeSaveId);
                if (!nestedAdvanceRequested)
                {
                    nestedAdvanceRequested = true;
                    coordinator.AdvanceForReadinessToken(
                        readinessToken: 2,
                        maxItems: 16,
                        maxFallbackSpawns: 2,
                        observe: (_, _, _) =>
                        {
                            nestedObserveCount++;
                            return new
                                NeonLetterMultiplayerRestoreObservation<string>(
                                    NeonLetterMultiplayerRestoreObservationKind
                                        .ProcessedRecipeUnavailable);
                        },
                        startFallback: _ => "fallback",
                        applyRestored: (_, _) => true,
                        onEntryError: (_, exception) => throw exception);
                }

                return true;
            },
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(
            (string.Join(",", Enumerable.Range(1, 16)), 0),
            (string.Join(",", appliedSaveIds), nestedObserveCount));
    }

    [Fact]
    public void ReentrantMultiplayerFallbackKeepsTheGlobalSpawnBudget()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(
                count: 40,
                includeNativeSaveIds: false);
        int fallbackStartCount = 0;
        bool nestedAdvanceRequested = false;

        void Advance(ulong token)
        {
            coordinator.AdvanceForReadinessToken(
                readinessToken: token,
                maxItems: 16,
                maxFallbackSpawns: 2,
                observe: (_, _, _) =>
                    new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .ReadyToSpawnFallback),
                startFallback: _ =>
                {
                    fallbackStartCount++;
                    if (!nestedAdvanceRequested)
                    {
                        nestedAdvanceRequested = true;
                        coordinator.AdvanceForReadinessToken(
                            readinessToken: 2,
                            maxItems: 16,
                            maxFallbackSpawns: 2,
                            observe: (_, _, _) =>
                                new
                                    NeonLetterMultiplayerRestoreObservation<string>(
                                        NeonLetterMultiplayerRestoreObservationKind
                                            .ReadyToSpawnFallback),
                            startFallback: _ =>
                            {
                                fallbackStartCount++;
                                return "nested-fallback";
                            },
                            applyRestored: (_, _) => true,
                            onEntryError: (_, exception) => throw exception);
                    }

                    return "fallback";
                },
                applyRestored: (_, _) => true,
                onEntryError: (_, exception) => throw exception);
        }

        Advance(token: 1);
        Assert.Equal(2, fallbackStartCount);

        Advance(token: 2);
        Assert.Equal(4, fallbackStartCount);
    }

    [Fact]
    public void IntegerElapsedTimeCallUsesTheCompatibilityOverload()
    {
        NeonLetterMultiplayerRestoreCoordinator<string> coordinator =
            CreateMultiplayerCoordinator(1);
        int attemptCount = 0;

        coordinator.Advance(
            0,
            maxItems: 1,
            maxFallbackSpawns: 0,
            observe: (_, _, _) =>
            {
                attemptCount++;
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable);
            },
            startFallback: _ => "fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) => throw exception);

        Assert.Equal(1, attemptCount);
    }

    private static NeonLetterColorSaveEnvelope CreateSinglePlayerEnvelope(
        params int[] saveIds)
    {
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        return new NeonLetterColorSaveEnvelope
        {
            Entries = saveIds
                .Select(saveId => new NeonLetterColorSaveEntry(
                    saveId,
                    recipeId,
                    NeonRgba.ProjectCyan))
                .ToList()
        };
    }

    private static NeonLetterMultiplayerRestoreCoordinator<string>
        CreateMultiplayerCoordinator(
            int count,
            bool includeNativeSaveIds = true)
    {
        int recipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = Enumerable.Range(1, count)
                .Select(index => new NeonLetterMultiplayerSaveEntry
                {
                    RecipeId = recipeId,
                    NativeSaveId = includeNativeSaveIds ? index : 0,
                    Position = new NeonVector3(index, 0f, 0f),
                    Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                    PackedColor = NeonLetterNetworkProtocol.Pack(
                        NeonRgba.ProjectCyan)
                })
                .ToList()
        });
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        return coordinator;
    }
}
