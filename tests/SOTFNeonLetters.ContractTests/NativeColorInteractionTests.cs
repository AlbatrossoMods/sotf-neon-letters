using SOTFNeonLetters;
using Xunit;

[Collection(AllocationSensitiveTestCollection.Name)]
public sealed class NativeColorInteractionTests
{
    private static readonly Func<TrackedRoot, bool>
        IsRootAliveCallback = IsRootAlive;

    [Theory]
    [InlineData(float.NaN, 0f, 0f, 1f, 1f, 1f)]
    [InlineData(0f, float.NaN, 0f, 1f, 1f, 1f)]
    [InlineData(0f, 0f, float.NaN, 1f, 1f, 1f)]
    [InlineData(0f, 0f, 0f, float.NaN, 1f, 1f)]
    [InlineData(0f, 0f, 0f, 1f, float.NaN, 1f)]
    [InlineData(0f, 0f, 0f, 1f, 1f, float.NaN)]
    public void NonFiniteGeometryComponentIsRejected(
        float centerX,
        float centerY,
        float centerZ,
        float sizeX,
        float sizeY,
        float sizeZ)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NeonLetterColorInteractionGeometryPolicy.Resolve(
                new NeonLetterColorInteractionBounds(
                    centerX,
                    centerY,
                    centerZ,
                    sizeX,
                    sizeY,
                    sizeZ)));
    }

    [Fact]
    public void ZeroSizedGlyphProxyRadiusUsesTheNamedMinimum()
    {
        NeonLetterColorInteractionGeometry geometry =
            NeonLetterColorInteractionGeometryPolicy.Resolve(
                new NeonLetterColorInteractionBounds(
                    CenterX: 0f,
                    CenterY: 0f,
                    CenterZ: 0f,
                    SizeX: 0f,
                    SizeY: 0f,
                    SizeZ: 0f));

        Assert.Equal(
            NeonLetterColorInteractionGeometryPolicy.MinimumProxyRadius,
            geometry.Radius);
    }

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
    public void CompletedKnownGlyphIsEditable()
    {
        bool isEditable = NeonLetterColorInteractionPolicy.IsEditable(
            hasCompletedStructure: true,
            NeonLetterSmallCatalog.Get('A').RecipeId);

        Assert.True(isEditable);
    }

    [Fact]
    public void CraftingPreviewIsNotEditable()
    {
        bool isEditable = NeonLetterColorInteractionPolicy.IsEditable(
            hasCompletedStructure: false,
            NeonLetterSmallCatalog.Get('A').RecipeId);

        Assert.False(isEditable);
    }

    [Fact]
    public void UnknownRecipeIsNotEditable()
    {
        bool isEditable = NeonLetterColorInteractionPolicy.IsEditable(
            hasCompletedStructure: true,
            recipeId: int.MinValue);

        Assert.False(isEditable);
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
    public void LiveLeaseMaintenanceInspectsOnlyItsConfiguredBound()
    {
        const int LeaseCount = 10_000;
        const int EntriesPerUpdate = 16;
        const int UpdateCount = 10_000;
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<TrackedRoot>();
        for (int index = 0; index < LeaseCount; index++)
        {
            registry.TryAdd(index, new TrackedRoot(isAlive: true));
        }

        int inspectedEntries = 0;
        bool removed = false;
        for (int update = 0; update < UpdateCount; update++)
        {
            removed |= registry.TryTakeNextDead(
                EntriesPerUpdate,
                IsRootAliveCallback,
                out _,
                out int inspectedThisUpdate);
            inspectedEntries += inspectedThisUpdate;
        }

        Assert.Equal(
            (
                EntriesPerUpdate * UpdateCount,
                false,
                LeaseCount),
            (
                inspectedEntries,
                removed,
                registry.Count));
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
    public void NativeUsePromptLinkSourceUsesScreenUseWhileInactive()
    {
        string leaseSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterColorInteractionLeaseRuntime.cs"));
        string runtimeSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterColorInteractionRuntime.cs"));
        int holderDisabled = leaseSource.IndexOf(
            "interactionHolder.SetActive(false)",
            StringComparison.Ordinal);
        int interactionCreated = leaseSource.IndexOf(
            "SonsInteractionTools.CreateInteraction<GenericInteraction>",
            StringComparison.Ordinal);
        int promptCreated = leaseSource.IndexOf(
            "SonsUiTools.CreateLinkUi(",
            StringComparison.Ordinal);
        int actionConfigured = leaseSource.IndexOf(
            "interaction._actionId = NativeUseAction",
            StringComparison.Ordinal);
        int promptConfigured = leaseSource.IndexOf(
            "interaction._interactGui = ownedPrompt",
            StringComparison.Ordinal);
        int callbackRegistered = leaseSource.IndexOf(
            "lease.RegisterCallback()",
            StringComparison.Ordinal);
        int leaseCreated = runtimeSource.IndexOf(
            "ColorInteractionLease lease = CreateColorInteractionLease(",
            StringComparison.Ordinal);
        int holderActivated = runtimeSource.IndexOf(
            "lease.Activate();",
            StringComparison.Ordinal);

        Assert.True(
            leaseSource.Contains(
                "SonsUiTools.CreateLinkUi(",
                StringComparison.Ordinal) &&
            leaseSource.Contains(
                "proxyTransform,\n" +
                "                    " +
                "\"screen.use\")",
                StringComparison.Ordinal) &&
            holderDisabled >= 0 &&
            holderDisabled < interactionCreated &&
            interactionCreated < promptCreated &&
            promptCreated < actionConfigured &&
            actionConfigured < promptConfigured &&
            promptConfigured < callbackRegistered &&
            leaseCreated >= 0 &&
            leaseCreated < holderActivated);
    }

    [Fact]
    public void NativePromptLinkSourceFailsClosedWhenCreationFails()
    {
        string leaseSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterColorInteractionLeaseRuntime.cs"));

        Assert.True(
            leaseSource.Contains(
                "if (promptTransform == null ||\n" +
                "                promptTransform.gameObject == null)",
                StringComparison.Ordinal) &&
            leaseSource.Contains(
                "\"SonsUiTools did not create its native Use \" +",
                StringComparison.Ordinal) &&
            leaseSource.Contains(
                "\"prompt link.\"",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NativeUsePromptLeaseSourceHasNoTemplateOrStaticIconDependency()
    {
        string leaseSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterColorInteractionLeaseRuntime.cs"));
        string runtimeSource = File.ReadAllText(
            FindRepositoryFile("NeonLetterColorInteractionRuntime.cs"));
        int registerStart = runtimeSource.IndexOf(
            "internal static void RegisterColorInteraction(",
            StringComparison.Ordinal);
        int unregisterStart = runtimeSource.IndexOf(
            "internal static void UnregisterColorInteraction(",
            StringComparison.Ordinal);
        string registrationSource =
            registerStart >= 0 && unregisterStart > registerStart
                ? runtimeSource.Substring(
                    registerStart,
                    unregisterStart - registerStart)
                : string.Empty;

        Assert.True(
            !leaseSource.Contains(
                "DynamicInputIcon",
                StringComparison.Ordinal) &&
            !leaseSource.Contains(
                "InputIconManager",
                StringComparison.Ordinal) &&
            !leaseSource.Contains(
                "Instantiate<GameObject>",
                StringComparison.Ordinal) &&
            !leaseSource.Contains(
                "Resources.FindObjectsOfTypeAll",
                StringComparison.Ordinal) &&
            !registrationSource.Contains(
                "TryGetPromptTemplate",
                StringComparison.Ordinal) &&
            !registrationSource.Contains(
                "promptTemplate",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NativeColorInteractionUsesOwnedLinkUiAndManagerBackfill()
    {
        string source = string.Join(
            "\n",
            new[]
            {
                "NeonLetterColorInteractionLeaseRuntime.cs",
                "NeonLetterColorInteractionLifecycleRuntime.cs",
                "NeonLetterColorInteractionRuntime.cs",
                "NeonLetterColorInteractionPolicy.cs",
                "NeonLetterColorInteractionHarmony.cs",
                "NeonLetterColorRuntime.cs",
                "SOTFNeonLetters.cs"
            }.Select(path => File.ReadAllText(FindRepositoryFile(path))));
        string[] removedArchitecture =
        {
            "nameof(GenericInteraction.OnEnable)",
            "ObserveNativeInteractionPrompt",
            "BeginInteractionPromptObservation",
            "EndInteractionPromptObservation",
            "PromptLifecycle",
            "PromptDiagnostics",
            "WeakReference<",
            "DynamicInputIcon",
            "OwnedInteractionInstanceIds",
            "TryGetPromptTemplate",
            "hasPromptTemplate",
            "compatible vanilla interaction prompt",
            "NeonLetterLinkUiPromptRuntime"
        };

        Assert.True(
            removedArchitecture.All(
                token => !source.Contains(token, StringComparison.Ordinal)) &&
            source.Contains(
                "SonsUiTools.CreateLinkUi(",
                StringComparison.Ordinal) &&
            source.Contains(
                "\"screen.use\"",
                StringComparison.Ordinal) &&
            source.Contains(
                "InteractionBackfill.TryStartDueCycle(",
                StringComparison.Ordinal) &&
            source.Contains(
                "interaction.RegisterActionPerformed(",
                StringComparison.Ordinal));
    }

    [Fact]
    public void DueBackfillAttemptsExistingStructuresWithoutAPrompt()
    {
        var backfill =
            new NeonLetterColorInteractionBackfillCoordinator();
        int attempts = 0;

        bool started = backfill.TryStartDueCycle(updateTick: 0);
        NeonLetterColorInteractionBackfillWindow window =
            backfill.TakeWindow(itemCount: 3, maximumItems: 64);
        for (int index = window.StartOffset;
             index < window.StartOffset + window.Count;
             index++)
        {
            attempts++;
        }

        Assert.Equal((true, 3, false), (started, attempts, backfill.IsPending));
    }

    [Fact]
    public void TransientLeaseFailureRetriesAtTheFirstDueBackfillTick()
    {
        var backfill =
            new NeonLetterColorInteractionBackfillCoordinator();
        var failures =
            new NeonLetterColorInteractionCreationFailures<string>();
        var attemptTicks = new List<long>();
        for (long updateTick = 0;
             updateTick <=
             NeonLetterColorInteractionBackfillSchedule.RetryUpdateDelay;
             updateTick++)
        {
            if (!backfill.TryStartDueCycle(updateTick))
            {
                continue;
            }

            NeonLetterColorInteractionBackfillWindow window =
                backfill.TakeWindow(itemCount: 1, maximumItems: 64);
            if (window.Count == 0 ||
                !failures.AllowsAttempt(
                    structureInstanceId: 7,
                    updateTick))
            {
                continue;
            }

            attemptTicks.Add(updateTick);
            failures.RecordTransientFailure(
                structureInstanceId: 7,
                updateTick,
                "native prompt link unavailable");
        }

        Assert.Equal(new long[] { 0, 120 }, attemptTicks);
    }

    [Fact]
    public void RepeatedBackfillCyclesKeepOneLeasePerLiveStructure()
    {
        var backfill =
            new NeonLetterColorInteractionBackfillCoordinator();
        var leases =
            new NeonLetterColorInteractionLeaseRegistry<object>();
        int leasesCreated = 0;
        foreach (long updateTick in new long[] { 0, 120, 240 })
        {
            if (!backfill.TryStartDueCycle(updateTick))
            {
                continue;
            }

            NeonLetterColorInteractionBackfillWindow window =
                backfill.TakeWindow(itemCount: 3, maximumItems: 64);
            for (int structureInstanceId = window.StartOffset + 1;
                 structureInstanceId <=
                 window.StartOffset + window.Count;
                 structureInstanceId++)
            {
                if (leases.Contains(structureInstanceId))
                {
                    continue;
                }

                if (leases.TryAdd(structureInstanceId, new object()))
                {
                    leasesCreated++;
                }
            }
        }

        Assert.Equal((3, 3), (leasesCreated, leases.Count));
    }

    [Fact]
    public void RuntimeBackfillDecisionUsesOnlyTheManagerCoordinator()
    {
        string lifecycleSource = File.ReadAllText(
            FindRepositoryFile(
                "NeonLetterColorInteractionLifecycleRuntime.cs"));
        int advanceStart = lifecycleSource.IndexOf(
            "private static void AdvanceColorInteractions()",
            StringComparison.Ordinal);
        int livenessStart = lifecycleSource.IndexOf(
            "private static bool IsInteractionLeaseAlive(",
            StringComparison.Ordinal);
        string advanceMethod =
            advanceStart >= 0 && livenessStart > advanceStart
                ? lifecycleSource.Substring(
                    advanceStart,
                    livenessStart - advanceStart)
                : string.Empty;
        int backfillStart = lifecycleSource.IndexOf(
            "private static void AdvanceInteractionBackfill()",
            StringComparison.Ordinal);
        int headlessStart = lifecycleSource.IndexOf(
            "private static bool IsDedicatedOrHeadless()",
            StringComparison.Ordinal);
        string backfillMethod =
            backfillStart >= 0 && headlessStart > backfillStart
                ? lifecycleSource.Substring(
                    backfillStart,
                    headlessStart - backfillStart)
                : string.Empty;

        Assert.True(
            !advanceMethod.Contains(
                "PromptLifecycle",
                StringComparison.Ordinal) &&
            !backfillMethod.Contains(
                "PromptLifecycle",
                StringComparison.Ordinal) &&
            advanceMethod.Contains(
                "InteractionBackfill.TryStartDueCycle(",
                StringComparison.Ordinal));
    }

    private static bool IsRootAlive(TrackedRoot root)
    {
        return root.IsAlive;
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}'.");
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

    private sealed class TrackedRoot
    {
        internal TrackedRoot(bool isAlive)
        {
            IsAlive = isAlive;
        }

        internal bool IsAlive { get; }
    }

}
