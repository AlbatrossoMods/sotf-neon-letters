using SOTFNeonLetters;
using Xunit;

public sealed class NativeColorInteractionTests
{
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
        var deadRoot = new TrackedRoot(IsAlive: false);
        registry.TryAdd(1, deadRoot);
        registry.TryAdd(2, new TrackedRoot(IsAlive: true));

        IReadOnlyList<TrackedRoot> removed =
            registry.Sweep(1, root => root.IsAlive);

        Assert.Equal((deadRoot, 1), (removed.Single(), registry.Count));
    }

    [Fact]
    public void WorldExitOrDeinitializeDrainsEveryLeaseOnce()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<string>();
        registry.TryAdd(1, "first");
        registry.TryAdd(2, "second");

        IReadOnlyList<string> firstDrain = registry.Drain();
        IReadOnlyList<string> secondDrain = registry.Drain();

        Assert.Equal(
            ("first,second", string.Empty, 0),
            (
                string.Join(",", firstDrain),
                string.Join(",", secondDrain),
                registry.Count));
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
    public void PromptDiscoveryRetriesOnlyAfterTheNamedUpdateBackoff()
    {
        var schedule = new NeonLetterColorInteractionPromptDiscoverySchedule();

        bool first = schedule.TryBeginAttempt(updateTick: 0);
        bool tooEarly = schedule.TryBeginAttempt(
            NeonLetterColorInteractionPromptDiscoverySchedule
                .RetryUpdateDelay - 1);
        bool retry = schedule.TryBeginAttempt(
            NeonLetterColorInteractionPromptDiscoverySchedule
                .RetryUpdateDelay);

        Assert.Equal((true, false, true), (first, tooEarly, retry));
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
    public void BoundedPromptCandidateWindowsAdvanceWithoutPermanentExclusion()
    {
        NeonLetterColorInteractionPromptCandidateWindow first =
            NeonLetterColorInteractionPromptCandidateWindowPolicy.Resolve(
                candidateCount: 300,
                startOffset: 0,
                maximumCandidates: 256);
        NeonLetterColorInteractionPromptCandidateWindow second =
            NeonLetterColorInteractionPromptCandidateWindowPolicy.Resolve(
                candidateCount: 300,
                startOffset: first.NextOffset,
                maximumCandidates: 256);

        Assert.Equal(
            (
                first.StartOffset,
                first.Count,
                first.NextOffset,
                second.StartOffset,
                second.Count,
                second.NextOffset),
            (0, 256, 256, 256, 44, 0));
    }

    [Fact]
    public void PromptBackfillCycleAdvancesInBoundedWindows()
    {
        var cursor = new NeonLetterColorInteractionBackfillCursor();
        cursor.StartCycle();

        NeonLetterColorInteractionBackfillWindow first =
            cursor.TakeWindow(itemCount: 130, maximumItems: 64);
        NeonLetterColorInteractionBackfillWindow second =
            cursor.TakeWindow(itemCount: 130, maximumItems: 64);
        NeonLetterColorInteractionBackfillWindow third =
            cursor.TakeWindow(itemCount: 130, maximumItems: 64);

        Assert.Equal(
            (0, 64, 64, 64, 128, 2, false),
            (
                first.StartOffset,
                first.Count,
                second.StartOffset,
                second.Count,
                third.StartOffset,
                third.Count,
                cursor.IsActive));
    }

    [Fact]
    public void UnavailableManagerKeepsPromptBackfillCycleRetryable()
    {
        var cursor = new NeonLetterColorInteractionBackfillCursor();
        cursor.StartCycle();

        cursor.ReportUnavailable();

        Assert.True(cursor.IsActive);
    }

    private sealed record TrackedRoot(bool IsAlive);
}
