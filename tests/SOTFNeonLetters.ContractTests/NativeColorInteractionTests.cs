using SOTFNeonLetters;
using Xunit;

public sealed class NativeColorInteractionTests
{
    private static readonly Func<TrackedRoot, bool>
        IsRootAliveCallback = IsRootAlive;

    [Fact]
    public void ProxyGeometryKeepsTheGlyphColliderCenter()
    {
        NeonLetterColorInteractionGeometry geometry =
            NeonLetterColorInteractionGeometryPolicy.Resolve(
                new NeonLetterColorInteractionBounds(
                    CenterX: 1f,
                    CenterY: 2f,
                    CenterZ: 3f,
                    SizeX: 1f,
                    SizeY: 1f,
                    SizeZ: 1f));

        Assert.Equal((1f, 2f, 3f), (geometry.CenterX, geometry.CenterY, geometry.CenterZ));
    }

    [Fact]
    public void SmallGlyphProxyRadiusUsesTheNamedMinimum()
    {
        NeonLetterColorInteractionGeometry geometry =
            NeonLetterColorInteractionGeometryPolicy.Resolve(
                new NeonLetterColorInteractionBounds(
                    CenterX: 0f,
                    CenterY: 0f,
                    CenterZ: 0f,
                    SizeX: 0.1f,
                    SizeY: 0.2f,
                    SizeZ: 0.1f));

        Assert.Equal(
            NeonLetterColorInteractionGeometryPolicy.MinimumProxyRadius,
            geometry.Radius);
    }

    [Fact]
    public void LargeGlyphProxyRadiusUsesTheNamedMaximum()
    {
        NeonLetterColorInteractionGeometry geometry =
            NeonLetterColorInteractionGeometryPolicy.Resolve(
                new NeonLetterColorInteractionBounds(
                    CenterX: 0f,
                    CenterY: 0f,
                    CenterZ: 0f,
                    SizeX: 8f,
                    SizeY: 2f,
                    SizeZ: 1f));

        Assert.Equal(
            NeonLetterColorInteractionGeometryPolicy.MaximumProxyRadius,
            geometry.Radius);
    }

    [Fact]
    public void CompletedKnownGlyphCanOwnANativeInteraction()
    {
        bool shouldCreate =
            NeonLetterColorInteractionPolicy.ShouldCreateLease(
                isDedicatedOrHeadless: false,
                hasCompletedStructure: true,
                NeonLetterSmallCatalog.Get('A').RecipeId,
                hasPromptTemplate: true);

        Assert.True(shouldCreate);
    }

    [Fact]
    public void CraftingPreviewCannotOwnANativeInteraction()
    {
        bool shouldCreate =
            NeonLetterColorInteractionPolicy.ShouldCreateLease(
                isDedicatedOrHeadless: false,
                hasCompletedStructure: false,
                NeonLetterSmallCatalog.Get('A').RecipeId,
                hasPromptTemplate: true);

        Assert.False(shouldCreate);
    }

    [Fact]
    public void UnknownRecipeCannotOwnANativeInteraction()
    {
        bool shouldCreate =
            NeonLetterColorInteractionPolicy.ShouldCreateLease(
                isDedicatedOrHeadless: false,
                hasCompletedStructure: true,
                recipeId: int.MinValue,
                hasPromptTemplate: true);

        Assert.False(shouldCreate);
    }

    [Fact]
    public void DedicatedServerCannotOwnANativeInteraction()
    {
        bool shouldCreate =
            NeonLetterColorInteractionPolicy.ShouldCreateLease(
                isDedicatedOrHeadless: true,
                hasCompletedStructure: true,
                NeonLetterSmallCatalog.Get('A').RecipeId,
                hasPromptTemplate: true);

        Assert.False(shouldCreate);
    }

    [Fact]
    public void MissingVanillaPromptFailsClosed()
    {
        bool shouldCreate =
            NeonLetterColorInteractionPolicy.ShouldCreateLease(
                isDedicatedOrHeadless: false,
                hasCompletedStructure: true,
                NeonLetterSmallCatalog.Get('A').RecipeId,
                hasPromptTemplate: false);

        Assert.False(shouldCreate);
    }

    [Theory]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    public void NativeInteractionCannotActivateBeforePreparationCompletes(
        bool holderInactive,
        bool actionConfigured,
        bool promptConfigured,
        bool callbackRegistered,
        bool geometryConfigured)
    {
        bool canActivate =
            NeonLetterColorInteractionActivationPolicy.CanActivate(
                new NeonLetterColorInteractionActivationState(
                    holderInactive,
                    actionConfigured,
                    promptConfigured,
                    callbackRegistered,
                    geometryConfigured));

        Assert.False(canActivate);
    }

    [Fact]
    public void FullyPreparedNativeInteractionCanActivate()
    {
        bool canActivate =
            NeonLetterColorInteractionActivationPolicy.CanActivate(
                new NeonLetterColorInteractionActivationState(
                    HolderInactive: true,
                    ActionConfigured: true,
                    PromptConfigured: true,
                    CallbackRegistered: true,
                    GeometryConfigured: true));

        Assert.True(canActivate);
    }

    [Fact]
    public void DoubleRegistrationKeepsExactlyOneLease()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<object>();

        bool first = registry.TryAdd(7, new object());
        bool second = registry.TryAdd(7, new object());

        Assert.Equal((true, false, 1), (first, second, registry.Count));
    }

    [Fact]
    public void ExistingStructureCanBeDetectedBeforeAllocatingAnotherLease()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<object>();
        registry.TryAdd(7, new object());

        bool contains = registry.Contains(7);

        Assert.True(contains);
    }

    [Fact]
    public void SiblingCallbacksResolveOnlyTheirRegisteredStructure()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(1, "first");
        registry.TryAdd(2, "second");

        bool firstCurrent = registry.IsCurrent(1, "first");
        bool siblingCurrent = registry.IsCurrent(1, "second");

        Assert.Equal((true, false), (firstCurrent, siblingCurrent));
    }

    [Fact]
    public void UnregisterRemovesOnlyTheMatchingLease()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(1, "first");
        registry.TryAdd(2, "second");

        bool removed = registry.TryRemove(1, out string? lease);

        Assert.Equal(
            (true, "first", false, true),
            (
                removed,
                lease,
                registry.Contains(1),
                registry.Contains(2)));
    }

    [Fact]
    public void DismantleCleanupCanRemoveALeaseByStableInstanceId()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(1, "lease");

        bool removed = registry.TryRemove(1, out string? lease);

        Assert.Equal((true, "lease", 0), (removed, lease, registry.Count));
    }

    [Fact]
    public void DismantleStartCanResolveTheCurrentLeaseByStableInstanceId()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(1, "lease");

        bool found = registry.TryGet(
            1,
            out string? lease);

        Assert.Equal((true, "lease"), (found, lease));
    }

    [Fact]
    public void ReboundNativeWrapperCanUnregisterByStableInstanceId()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(19, "lease");
        var registeredWrapper = new object();
        var reboundWrapper = new object();

        bool removed = registry.TryRemove(19, out string? lease);

        Assert.Equal(
            (false, "lease"),
            (
                ReferenceEquals(registeredWrapper, reboundWrapper),
                removed ? lease : null));
    }

    [Fact]
    public void DestroyedRootsAreRemovedByABoundedSweep()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<TrackedRoot>();
        var deadRoot = new TrackedRoot(isAlive: false);
        registry.TryAdd(1, deadRoot);
        registry.TryAdd(2, new TrackedRoot(isAlive: true));

        bool removed = registry.TryTakeNextDead(
            maxEntries: 1,
            IsRootAliveCallback,
            out TrackedRoot? removedRoot,
            out int inspected);

        Assert.Equal(
            (true, deadRoot, 1, 1),
            (removed, removedRoot, inspected, registry.Count));
    }

    [Fact]
    public void WorldExitOrDeinitializeDrainsEveryLeaseOnce()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(1, "first");
        registry.TryAdd(2, "second");

        bool removedFirst = registry.TryTakeFirst(out string? first);
        bool removedSecond = registry.TryTakeFirst(out string? second);
        bool removedThird = registry.TryTakeFirst(out _);

        Assert.Equal(
            (true, "first", true, "second", false, 0),
            (
                removedFirst,
                first,
                removedSecond,
                second,
                removedThird,
                registry.Count));
    }

    [Fact]
    public void EmptyLeaseMaintenanceHasNoPerUpdateAllocation()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<TrackedRoot>();
        MeasureEmptyLeaseMaintenanceAllocation(
            registry,
            iterations: 2_048);
        long maximumAllocatedBytes = 0;

        for (int sample = 0; sample < 5; sample++)
        {
            maximumAllocatedBytes = Math.Max(
                maximumAllocatedBytes,
                MeasureEmptyLeaseMaintenanceAllocation(
                    registry,
                    iterations: 100_000));
        }

        Assert.InRange(maximumAllocatedBytes, 0, 256);
    }

    [Fact]
    public void LiveLeaseMaintenanceInspectsOnlyItsBoundWithoutAllocating()
    {
        const int LeaseCount = 10_000;
        const int EntriesPerUpdate = 16;
        const int UpdatesPerSample = 10_000;
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<TrackedRoot>();
        for (int index = 0; index < LeaseCount; index++)
        {
            registry.TryAdd(index, new TrackedRoot(isAlive: true));
        }

        MeasureLiveLeaseMaintenanceAllocation(
            registry,
            EntriesPerUpdate,
            iterations: 2_048,
            out _,
            out _);
        long maximumAllocatedBytes = 0;
        int inspected = 0;
        bool removed = false;
        for (int sample = 0; sample < 5; sample++)
        {
            maximumAllocatedBytes = Math.Max(
                maximumAllocatedBytes,
                MeasureLiveLeaseMaintenanceAllocation(
                    registry,
                    EntriesPerUpdate,
                    UpdatesPerSample,
                    out inspected,
                    out removed));
        }

        Assert.Equal(
            (
                EntriesPerUpdate * UpdatesPerSample,
                false,
                LeaseCount,
                true),
            (
                inspected,
                removed,
                registry.Count,
                maximumAllocatedBytes <= 256));
    }

    [Fact]
    public void PermanentStructuralFailureAttemptsAndLogsOnlyOnce()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();
        int attempts = 0;
        int logs = 0;
        for (long updateTick = 0;
             updateTick < 100_000;
             updateTick++)
        {
            if (!failures.AllowsAttempt(7, updateTick))
            {
                continue;
            }

            attempts++;
            if (failures.RecordTerminalFailure(
                    7,
                    "missing root collider"))
            {
                logs++;
            }
        }

        Assert.Equal((1, 1, 1), (attempts, logs, failures.Count));
    }

    [Fact]
    public void RepeatedTransientFailureUsesCappedBackoffAndCanRecover()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();
        int attempts = 0;
        int logs = 0;
        for (long updateTick = 0;
             updateTick < 100_000;
             updateTick++)
        {
            if (!failures.AllowsAttempt(7, updateTick))
            {
                continue;
            }

            attempts++;
            if (attempts < 9)
            {
                if (failures.RecordTransientFailure(
                        7,
                        updateTick,
                        "temporary creation failure"))
                {
                    logs++;
                }

                continue;
            }

            failures.RecordSuccess(7);
            break;
        }

        Assert.Equal((9, 1, 0), (attempts, logs, failures.Count));
    }

    [Fact]
    public void PersistentTransientFailureHasBoundedAttemptsOverManyUpdates()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();
        int attempts = 0;
        int logs = 0;
        for (long updateTick = 0;
             updateTick < 100_000;
             updateTick++)
        {
            if (!failures.AllowsAttempt(7, updateTick))
            {
                continue;
            }

            attempts++;
            if (failures.RecordTransientFailure(
                    7,
                    updateTick,
                    "temporary creation failure"))
            {
                logs++;
            }
        }

        Assert.Equal((19, 1, 1), (attempts, logs, failures.Count));
    }

    [Fact]
    public void ChangedFailureFingerprintStartsOneNewLoggingEpisode()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();
        bool first = failures.RecordTransientFailure(
            7,
            updateTick: 0,
            "first failure");
        bool changed = failures.RecordTransientFailure(
            7,
            NeonLetterColorInteractionCreationFailures<string>
                .InitialRetryDelayUpdates,
            "changed failure");
        bool repeated = failures.RecordTransientFailure(
            7,
            NeonLetterColorInteractionCreationFailures<string>
                .InitialRetryDelayUpdates * 3,
            "changed failure");

        Assert.Equal((true, true, false), (first, changed, repeated));
    }

    [Fact]
    public void UnregisterAllowsAReplacementLifecycleToRetry()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();
        failures.RecordTerminalFailure(7, "missing root collider");

        failures.Remove(7);

        Assert.True(failures.AllowsAttempt(7, updateTick: 0));
    }

    [Fact]
    public void WorldCleanupClearsEveryCreationFailure()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();
        failures.RecordTerminalFailure(7, "missing root collider");
        failures.RecordTransientFailure(
            8,
            updateTick: 0,
            "temporary creation failure");

        failures.Clear();

        Assert.Equal(
            (0, true, true),
            (
                failures.Count,
                failures.AllowsAttempt(7, updateTick: 0),
                failures.AllowsAttempt(8, updateTick: 0)));
    }

    [Fact]
    public void FailedStructureDoesNotStarveHealthyNeighbors()
    {
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();
        failures.RecordTerminalFailure(1, "missing root collider");
        int attempts = 0;
        for (int structureInstanceId = 1;
             structureInstanceId <= 3;
             structureInstanceId++)
        {
            if (failures.AllowsAttempt(
                    structureInstanceId,
                    updateTick: 0))
            {
                attempts++;
            }
        }

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void ValidCurrentLeaseCanOpenTheEditor()
    {
        bool canOpen = NeonLetterColorInteractionPolicy.CanOpenEditor(
            new NeonLetterColorInteractionValidation(
                RootAlive: true,
                IsCurrentLease: true,
                IsKnownCompletedStructure: true,
                IsPlayerControllable: true,
                IsEditorOpen: false,
                IsDismantlingOrBlocked: false));

        Assert.True(canOpen);
    }

    [Fact]
    public void StaleLeaseCannotOpenTheEditor()
    {
        bool canOpen = NeonLetterColorInteractionPolicy.CanOpenEditor(
            new NeonLetterColorInteractionValidation(
                RootAlive: true,
                IsCurrentLease: false,
                IsKnownCompletedStructure: true,
                IsPlayerControllable: true,
                IsEditorOpen: false,
                IsDismantlingOrBlocked: false));

        Assert.False(canOpen);
    }

    [Fact]
    public void DismantlingGlyphCannotOpenTheEditor()
    {
        bool canOpen = NeonLetterColorInteractionPolicy.CanOpenEditor(
            new NeonLetterColorInteractionValidation(
                RootAlive: true,
                IsCurrentLease: true,
                IsKnownCompletedStructure: true,
                IsPlayerControllable: true,
                IsEditorOpen: false,
                IsDismantlingOrBlocked: true));

        Assert.False(canOpen);
    }

    [Fact]
    public void OpenEditorBlocksAnotherGlyphInteraction()
    {
        bool canOpen = NeonLetterColorInteractionPolicy.CanOpenEditor(
            new NeonLetterColorInteractionValidation(
                RootAlive: true,
                IsCurrentLease: true,
                IsKnownCompletedStructure: true,
                IsPlayerControllable: true,
                IsEditorOpen: true,
                IsDismantlingOrBlocked: false));

        Assert.False(canOpen);
    }

    [Fact]
    public void MissingPromptFailureIsReportedOnlyOnce()
    {
        var gate = new NeonLetterColorInteractionFailureGate();

        bool first = gate.TryBeginPromptFailureReport();
        bool second = gate.TryBeginPromptFailureReport();

        Assert.Equal((true, false), (first, second));
    }

    [Fact]
    public void SuccessfulPromptDiscoveryStartsANewFailureReportingEpisode()
    {
        var gate = new NeonLetterColorInteractionFailureGate();
        gate.TryBeginPromptFailureReport();

        gate.ResetPromptFailureReport();

        Assert.True(gate.TryBeginPromptFailureReport());
    }

    [Fact]
    public void ObservedNativeUsePromptStartsPendingBackfill()
    {
        var prompt = new TrackedPrompt();
        var lifecycle =
            new NeonLetterColorInteractionPromptLifecycle<TrackedPrompt>(
                candidate => candidate.IsAlive);

        NeonLetterColorInteractionPromptObservationResult result =
            lifecycle.Observe(
                new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                    IsOwnedColorInteraction: false,
                    UsesNativeUseAction: true,
                    HasInteractionGui: true,
                    HasDynamicInputIcon: true,
                    prompt));

        Assert.Equal(
            (
                NeonLetterColorInteractionPromptObservationResult.Accepted,
                true,
                1UL),
            (result, lifecycle.IsBackfillPending, lifecycle.Generation));
    }

    [Fact]
    public void OwnedColorInteractionCannotBecomePromptTemplate()
    {
        var lifecycle =
            new NeonLetterColorInteractionPromptLifecycle<TrackedPrompt>(
                candidate => candidate.IsAlive);

        NeonLetterColorInteractionPromptObservationResult result =
            lifecycle.Observe(
                new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                    IsOwnedColorInteraction: true,
                    UsesNativeUseAction: true,
                    HasInteractionGui: true,
                    HasDynamicInputIcon: true,
                    new TrackedPrompt()));

        Assert.Equal(
            NeonLetterColorInteractionPromptObservationResult.Ignored,
            result);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void InvalidNativePromptCandidateIsIgnored(
        bool usesNativeUseAction,
        bool hasInteractionGui,
        bool hasDynamicInputIcon)
    {
        var lifecycle =
            new NeonLetterColorInteractionPromptLifecycle<TrackedPrompt>(
                candidate => candidate.IsAlive);

        NeonLetterColorInteractionPromptObservationResult result =
            lifecycle.Observe(
                new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                    IsOwnedColorInteraction: false,
                    usesNativeUseAction,
                    hasInteractionGui,
                    hasDynamicInputIcon,
                    new TrackedPrompt()));

        Assert.Equal(
            NeonLetterColorInteractionPromptObservationResult.Ignored,
            result);
    }

    [Fact]
    public void DestroyedPromptCanBeReplacedByTheNextObservedCandidate()
    {
        var first = new TrackedPrompt();
        var replacement = new TrackedPrompt();
        var lifecycle =
            new NeonLetterColorInteractionPromptLifecycle<TrackedPrompt>(
                candidate => candidate.IsAlive);
        lifecycle.Observe(
            new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                false,
                true,
                true,
                true,
                first));
        first.IsAlive = false;

        NeonLetterColorInteractionPromptObservationResult result =
            lifecycle.Observe(
                new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                    false,
                    true,
                    true,
                    true,
                    replacement));
        bool found = lifecycle.TryGetTemplate(
            out TrackedPrompt? resolved);

        Assert.Equal(
            (
                NeonLetterColorInteractionPromptObservationResult.Accepted,
                true,
                replacement,
                2UL),
            (result, found, resolved, lifecycle.Generation));
    }

    [Fact]
    public void RepeatedOnEnableForCurrentPromptIsIdempotent()
    {
        var prompt = new TrackedPrompt();
        var lifecycle =
            new NeonLetterColorInteractionPromptLifecycle<TrackedPrompt>(
                candidate => candidate.IsAlive);
        var candidate =
            new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                false,
                true,
                true,
                true,
                prompt);
        lifecycle.Observe(candidate);

        NeonLetterColorInteractionPromptObservationResult result =
            lifecycle.Observe(candidate);

        Assert.Equal(
            (
                NeonLetterColorInteractionPromptObservationResult.Unchanged,
                1UL,
                1),
            (result, lifecycle.Generation, lifecycle.RetainedTemplateCount));
    }

    [Fact]
    public void PromptObservationRetainsOnlyTheCurrentTemplate()
    {
        var lifecycle =
            new NeonLetterColorInteractionPromptLifecycle<TrackedPrompt>(
                candidate => candidate.IsAlive);
        lifecycle.Observe(
            new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                false,
                true,
                true,
                true,
                new TrackedPrompt()));

        lifecycle.Observe(
            new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                false,
                true,
                true,
                true,
                new TrackedPrompt()));

        Assert.Equal(1, lifecycle.RetainedTemplateCount);
    }

    [Fact]
    public void PromptBackfillCycleAdvancesInBoundedWindows()
    {
        var lifecycle =
            new NeonLetterColorInteractionPromptLifecycle<TrackedPrompt>(
                candidate => candidate.IsAlive);
        lifecycle.Observe(
            new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                false,
                true,
                true,
                true,
                new TrackedPrompt()));

        NeonLetterColorInteractionBackfillWindow first =
            lifecycle.TakeBackfillWindow(
                itemCount: 130,
                maximumItems: 64);
        NeonLetterColorInteractionBackfillWindow second =
            lifecycle.TakeBackfillWindow(
                itemCount: 130,
                maximumItems: 64);
        NeonLetterColorInteractionBackfillWindow third =
            lifecycle.TakeBackfillWindow(
                itemCount: 130,
                maximumItems: 64);

        Assert.Equal(
            (0, 64, 64, 64, 128, 2, false),
            (
                first.StartOffset,
                first.Count,
                second.StartOffset,
                second.Count,
                third.StartOffset,
                third.Count,
                lifecycle.IsBackfillPending));
    }

    [Fact]
    public void UnavailableManagerKeepsPromptBackfillCycleRetryable()
    {
        var lifecycle =
            new NeonLetterColorInteractionPromptLifecycle<TrackedPrompt>(
                candidate => candidate.IsAlive);
        lifecycle.Observe(
            new NeonLetterColorInteractionPromptCandidate<TrackedPrompt>(
                false,
                true,
                true,
                true,
                new TrackedPrompt()));

        lifecycle.ReportBackfillUnavailable();

        Assert.True(lifecycle.IsBackfillPending);
    }

    private static bool IsRootAlive(TrackedRoot root)
    {
        return root.IsAlive;
    }

    private static long MeasureEmptyLeaseMaintenanceAllocation(
        NeonLetterColorInteractionLeaseRegistry<TrackedRoot> registry,
        int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            registry.TryTakeNextDead(
                maxEntries: 16,
                IsRootAliveCallback,
                out _,
                out _);
            registry.TryTakeFirst(out _);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static long MeasureLiveLeaseMaintenanceAllocation(
        NeonLetterColorInteractionLeaseRegistry<TrackedRoot> registry,
        int maxEntries,
        int iterations,
        out int inspected,
        out bool removed)
    {
        inspected = 0;
        removed = false;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            removed |= registry.TryTakeNextDead(
                maxEntries,
                IsRootAliveCallback,
                out _,
                out int inspectedThisUpdate);
            inspected += inspectedThisUpdate;
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class TrackedRoot
    {
        internal TrackedRoot(bool isAlive)
        {
            IsAlive = isAlive;
        }

        internal bool IsAlive { get; }
    }

    private sealed class TrackedPrompt
    {
        internal bool IsAlive { get; set; } = true;
    }
}
