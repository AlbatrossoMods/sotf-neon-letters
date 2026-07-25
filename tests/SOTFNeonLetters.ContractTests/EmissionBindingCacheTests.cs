using System.Runtime.CompilerServices;
using SOTFNeonLetters;
using Xunit;

public sealed class EmissionBindingCacheTests
{
    [Fact]
    public void MissingRootLivenessValidatorIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NeonLetterEmissionBindingCache<
                FakeRoot,
                FakeDefinition,
                FakeBinding>(
                null!,
                static (root, definition) =>
                    new FakeBinding(root, definition, slotCount: 1)));
    }

    [Fact]
    public void MissingBindingFactoryIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NeonLetterEmissionBindingCache<
                FakeRoot,
                FakeDefinition,
                FakeBinding>(
                static root => root.IsAlive,
                null!));
    }

    [Fact]
    public void MissingStructureRootIsRejected()
    {
        NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding> cache = CreateReferenceTrackingCache();

        Assert.Throws<ArgumentNullException>(
            () => cache.GetOrCreate(
                17,
                null!,
                new FakeDefinition(),
                recipeId: 29));
    }

    [Fact]
    public void MissingDefinitionIsRejected()
    {
        NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding> cache = CreateReferenceTrackingCache();

        Assert.Throws<ArgumentNullException>(
            () => cache.GetOrCreate(
                17,
                new FakeRoot(),
                null!,
                recipeId: 29));
    }

    [Fact]
    public void MissingExpectedRootForRemovalIsRejected()
    {
        NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding> cache = CreateReferenceTrackingCache();

        Assert.Throws<ArgumentNullException>(
            () => cache.Remove(17, null!));
    }

    [Fact]
    public void SameLiveStructureAndRecipeReuseTheCreatedBinding()
    {
        int discoveryCount = 0;
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            root => root.IsAlive,
            (root, definition) =>
            {
                discoveryCount++;
                return new FakeBinding(root, definition, slotCount: 1);
            });
        var root = new FakeRoot();
        var definition = new FakeDefinition();

        FakeBinding first = cache.GetOrCreate(
            instanceId: 17,
            root,
            definition,
            recipeId: 29);
        FakeBinding second = cache.GetOrCreate(
            instanceId: 17,
            root,
            definition,
            recipeId: 29);

        Assert.Equal(
            (true, 1),
            (ReferenceEquals(first, second), discoveryCount));
    }

    [Fact]
    public void SameInstanceIdWithDifferentRootDefinitionOrRecipeRebuildsTheBinding()
    {
        int discoveryCount = 0;
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            root => root.IsAlive,
            (root, definition) =>
            {
                discoveryCount++;
                return new FakeBinding(root, definition, slotCount: 1);
            });
        var firstRoot = new FakeRoot();
        var secondRoot = new FakeRoot();
        var firstDefinition = new FakeDefinition();
        var secondDefinition = new FakeDefinition();

        FakeBinding first = cache.GetOrCreate(
            instanceId: 17,
            firstRoot,
            firstDefinition,
            recipeId: 29);
        FakeBinding afterRootChange = cache.GetOrCreate(
            instanceId: 17,
            secondRoot,
            firstDefinition,
            recipeId: 29);
        FakeBinding afterDefinitionChange = cache.GetOrCreate(
            instanceId: 17,
            secondRoot,
            secondDefinition,
            recipeId: 29);
        FakeBinding afterRecipeChange = cache.GetOrCreate(
            instanceId: 17,
            secondRoot,
            secondDefinition,
            recipeId: 30);

        Assert.Equal(
            (false, false, false, 4),
            (
                ReferenceEquals(first, afterRootChange),
                ReferenceEquals(afterRootChange, afterDefinitionChange),
                ReferenceEquals(afterDefinitionChange, afterRecipeChange),
                discoveryCount));
    }

    [Fact]
    public void DestroyedStructureRemovesItsStaleBindingWithoutCallingTheFactory()
    {
        int discoveryCount = 0;
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            root => root.IsAlive,
            (root, definition) =>
            {
                discoveryCount++;
                return new FakeBinding(root, definition, slotCount: 1);
            });
        var root = new FakeRoot();
        var definition = new FakeDefinition();
        cache.GetOrCreate(17, root, definition, recipeId: 29);
        root.IsAlive = false;

        Assert.Throws<InvalidOperationException>(
            () => cache.GetOrCreate(17, root, definition, recipeId: 29));
        bool removedAfterInvalidation = cache.Remove(17, root);
        var replacementRoot = new FakeRoot();
        cache.GetOrCreate(17, replacementRoot, definition, recipeId: 29);

        Assert.Equal(
            (false, 2),
            (removedAfterInvalidation, discoveryCount));
    }

    [Fact]
    public void InvalidStructureDoesNotDisplaceAReusedInstanceIdsLiveBinding()
    {
        int discoveryCount = 0;
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            root => root.IsAlive,
            (root, definition) =>
            {
                discoveryCount++;
                return new FakeBinding(root, definition, slotCount: 1);
            });
        var liveRoot = new FakeRoot();
        var staleRoot = new FakeRoot { IsAlive = false };
        var definition = new FakeDefinition();
        FakeBinding liveBinding = cache.GetOrCreate(
            17,
            liveRoot,
            definition,
            recipeId: 29);

        Assert.Throws<InvalidOperationException>(
            () => cache.GetOrCreate(17, staleRoot, definition, recipeId: 29));
        FakeBinding resolved = cache.GetOrCreate(
            17,
            liveRoot,
            definition,
            recipeId: 29);

        Assert.Equal(
            (true, 1),
            (ReferenceEquals(liveBinding, resolved), discoveryCount));
    }

    [Fact]
    public void ExplicitRemovalRecreatesOnlyTheExactStructuresBinding()
    {
        int discoveryCount = 0;
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            root => root.IsAlive,
            (root, definition) =>
            {
                discoveryCount++;
                return new FakeBinding(root, definition, slotCount: 1);
            });
        var root = new FakeRoot();
        var unrelatedRoot = new FakeRoot();
        var definition = new FakeDefinition();
        FakeBinding first = cache.GetOrCreate(
            17,
            root,
            definition,
            recipeId: 29);

        bool unrelatedRemoved = cache.Remove(17, unrelatedRoot);
        FakeBinding afterUnrelatedRemoval = cache.GetOrCreate(
            17,
            root,
            definition,
            recipeId: 29);
        bool exactRemoved = cache.Remove(17, root);
        FakeBinding recreated = cache.GetOrCreate(
            17,
            root,
            definition,
            recipeId: 29);

        Assert.Equal(
            (false, true, true, false, 2),
            (
                unrelatedRemoved,
                ReferenceEquals(first, afterUnrelatedRemoval),
                exactRemoved,
                ReferenceEquals(first, recreated),
                discoveryCount));
    }

    [Fact]
    public void ClearRecreatesEveryBindingOnItsNextUse()
    {
        int discoveryCount = 0;
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            root => root.IsAlive,
            (root, definition) =>
            {
                discoveryCount++;
                return new FakeBinding(root, definition, slotCount: 1);
            });
        var firstRoot = new FakeRoot();
        var secondRoot = new FakeRoot();
        var definition = new FakeDefinition();
        FakeBinding first = cache.GetOrCreate(
            17,
            firstRoot,
            definition,
            recipeId: 29);
        FakeBinding second = cache.GetOrCreate(
            18,
            secondRoot,
            definition,
            recipeId: 29);

        cache.Clear();
        FakeBinding recreatedFirst = cache.GetOrCreate(
            17,
            firstRoot,
            definition,
            recipeId: 29);
        FakeBinding recreatedSecond = cache.GetOrCreate(
            18,
            secondRoot,
            definition,
            recipeId: 29);

        Assert.Equal(
            (false, false, 4),
            (
                ReferenceEquals(first, recreatedFirst),
                ReferenceEquals(second, recreatedSecond),
                discoveryCount));
    }

    [Fact]
    public void FactoryFailureDoesNotLeaveAPoisonedEntry()
    {
        int discoveryCount = 0;
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            root => root.IsAlive,
            (root, definition) =>
            {
                discoveryCount++;
                if (discoveryCount == 1)
                {
                    throw new InvalidOperationException("discovery failed");
                }

                return new FakeBinding(root, definition, slotCount: 1);
            });
        var root = new FakeRoot();
        var definition = new FakeDefinition();

        Assert.Throws<InvalidOperationException>(
            () => cache.GetOrCreate(17, root, definition, recipeId: 29));
        FakeBinding recovered = cache.GetOrCreate(
            17,
            root,
            definition,
            recipeId: 29);
        FakeBinding reused = cache.GetOrCreate(
            17,
            root,
            definition,
            recipeId: 29);

        Assert.Equal(
            (true, 2),
            (ReferenceEquals(recovered, reused), discoveryCount));
    }

    [Fact]
    public void RecursiveCreationForTheSameIdentityLeavesNoCachedReferences()
    {
        NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding> cache = null!;
        bool recurse = true;
        int factoryCallCount = 0;
        cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            static root => root.IsAlive,
            (root, definition) =>
            {
                factoryCallCount++;
                if (recurse)
                {
                    recurse = false;
                    cache.GetOrCreate(
                        17,
                        root,
                        definition,
                        recipeId: 29);
                }

                return new FakeBinding(root, definition, slotCount: 1);
            });

        (
            WeakReference failedRoot,
            WeakReference failedDefinition,
            Type? failureType) = FailRecursiveCreation(cache);
        CollectReleasedReferences();
        var replacementRoot = new FakeRoot();
        var replacementDefinition = new FakeDefinition();
        FakeBinding recovered = cache.GetOrCreate(
            17,
            replacementRoot,
            replacementDefinition,
            recipeId: 29);
        FakeBinding reused = cache.GetOrCreate(
            17,
            replacementRoot,
            replacementDefinition,
            recipeId: 29);

        Assert.Equal(
            (
                typeof(InvalidOperationException),
                2,
                1,
                false,
                false,
                true),
            (
                failureType,
                factoryCallCount,
                cache.Count,
                failedRoot.IsAlive,
                failedDefinition.IsAlive,
                ReferenceEquals(recovered, reused)));
    }

    [Fact]
    public void FactoryCannotHideRecursiveCreationForTheSameIdentity()
    {
        NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding> cache = null!;
        bool recurse = true;
        int factoryCallCount = 0;
        cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            static root => root.IsAlive,
            (root, definition) =>
            {
                factoryCallCount++;
                if (recurse)
                {
                    recurse = false;
                    Record.Exception(
                        () => cache.GetOrCreate(
                            17,
                            root,
                            definition,
                            recipeId: 29));
                }

                return new FakeBinding(root, definition, slotCount: 1);
            });

        Exception? failure = Record.Exception(
            () => cache.GetOrCreate(
                17,
                new FakeRoot(),
                new FakeDefinition(),
                recipeId: 29));

        Assert.Equal(
            (typeof(InvalidOperationException), 1, 0),
            (failure?.GetType(), factoryCallCount, cache.Count));
    }

    [Fact]
    public void MissingFactoryResultDoesNotLeaveAPoisonedEntry()
    {
        int discoveryCount = 0;
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            root => root.IsAlive,
            (root, definition) =>
            {
                discoveryCount++;
                return discoveryCount == 1
                    ? null!
                    : new FakeBinding(root, definition, slotCount: 1);
            });
        var root = new FakeRoot();
        var definition = new FakeDefinition();

        Exception? failure = Record.Exception(
            () => cache.GetOrCreate(17, root, definition, recipeId: 29));
        FakeBinding recovered = cache.GetOrCreate(
            17,
            root,
            definition,
            recipeId: 29);

        Assert.Equal(
            (typeof(InvalidOperationException), 2, true),
            (failure?.GetType(), discoveryCount, recovered != null));
    }

    [Fact]
    public void RemoveReleasesCachedRootDefinitionAndBindingReferences()
    {
        var cache = CreateReferenceTrackingCache();

        (WeakReference root, WeakReference definition, WeakReference binding) =
            CacheThenRemove(cache);
        CollectReleasedReferences();

        Assert.Equal(
            (false, false, false),
            (root.IsAlive, definition.IsAlive, binding.IsAlive));
    }

    [Fact]
    public void ClearReleasesCachedRootDefinitionAndBindingReferences()
    {
        var cache = CreateReferenceTrackingCache();

        (WeakReference root, WeakReference definition, WeakReference binding) =
            CacheThenClear(cache);
        CollectReleasedReferences();

        Assert.Equal(
            (false, false, false),
            (root.IsAlive, definition.IsAlive, binding.IsAlive));
    }

    [Fact]
    public void RepeatedPreviewApplicationsDiscoverOnceAndUpdateEverySlot()
    {
        var factory = new InstrumentedEmissionBindingFactory(slotCount: 3);
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            NeonLetterEmissionBinding>(
            root => root.IsAlive,
            factory.Discover);
        var root = new FakeRoot();
        var definition = new FakeDefinition();
        var color = new NeonRgba(1f, 0f, 0f, 0.5f);

        for (int preview = 0; preview < 3; preview++)
        {
            cache.GetOrCreate(17, root, definition, recipeId: 29)
                .Apply(color);
        }

        Assert.Equal(
            (1, 3, "3,3,3", "2,3,4", "100,101,102"),
            (
                factory.DiscoveryCount,
                factory.TotalApplyCount,
                string.Join(",", factory.TotalSlotWriteCounts),
                string.Join(",", factory.LatestRedValues),
                string.Join(",", factory.LatestForeignValues)));
    }

    [Fact]
    public void ExplicitInvalidationCausesExactlyOneNewDiscovery()
    {
        var factory = new InstrumentedEmissionBindingFactory(slotCount: 2);
        var cache = new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            NeonLetterEmissionBinding>(
            root => root.IsAlive,
            factory.Discover);
        var root = new FakeRoot();
        var definition = new FakeDefinition();
        var color = new NeonRgba(1f, 0f, 0f, 1f);
        cache.GetOrCreate(17, root, definition, recipeId: 29).Apply(color);

        cache.Remove(17, root);
        cache.GetOrCreate(17, root, definition, recipeId: 29).Apply(color);
        cache.GetOrCreate(17, root, definition, recipeId: 29).Apply(color);

        Assert.Equal(
            (2, 3, "3,3", "2,3"),
            (
                factory.DiscoveryCount,
                factory.TotalApplyCount,
                string.Join(",", factory.TotalSlotWriteCounts),
                string.Join(",", factory.LatestRedValues)));
    }

    [Fact]
    public void ReusableEmissionBindingRefreshesMaterialIntensityEveryApply()
    {
        var slot = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 0,
            intensity: 2f,
            foreignValue: 41f);
        var binding = new NeonLetterEmissionBinding(
            new IEmissionBindingSlot[] { slot });
        var color = new NeonRgba(1f, 0f, 0f, 1f);
        binding.Apply(color);

        slot.Intensity = 5f;
        binding.Apply(color);

        NeonRgba applied = slot.PropertyBlock.ColorProperties[
            NeonLetterEmissionPolicy.EmissiveColorPropertyName];
        Assert.Equal(
            (2, 2, 2, 5f),
            (
                slot.IntensityReadCount,
                slot.PropertyBlockReadCount,
                slot.WriteCount,
                applied.Red));
    }

    [Fact]
    public void ReusableEmissionBindingKeepsEachSlotsBlockAndForeignValues()
    {
        var first = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 0,
            intensity: 2f,
            foreignValue: 41f);
        var second = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 1,
            intensity: 3f,
            foreignValue: 42f);
        var binding = new NeonLetterEmissionBinding(
            new IEmissionBindingSlot[] { first, second });

        binding.Apply(new NeonRgba(0f, 1f, 0f, 0.5f));

        NeonRgba firstColor = first.PropertyBlock.ColorProperties[
            NeonLetterEmissionPolicy.EmissiveColorPropertyName];
        NeonRgba secondColor = second.PropertyBlock.ColorProperties[
            NeonLetterEmissionPolicy.EmissiveColorPropertyName];
        Assert.Equal(
            (false, 41f, 42f, 2f, 3f, 0.5f, 0.5f),
            (
                ReferenceEquals(
                    first.PropertyBlock,
                    second.PropertyBlock),
                first.PropertyBlock.FloatProperties["foreign"],
                second.PropertyBlock.FloatProperties["foreign"],
                firstColor.Green,
                secondColor.Green,
                firstColor.Alpha,
                secondColor.Alpha));
    }

    [Fact]
    public void InvalidLaterSlotPreventsEveryPropertyBlockWrite()
    {
        var first = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 0,
            intensity: 2f,
            foreignValue: 41f);
        var invalid = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 1,
            intensity: float.NaN,
            foreignValue: 42f);
        var binding = new NeonLetterEmissionBinding(
            new IEmissionBindingSlot[] { first, invalid });

        Exception? failure = Record.Exception(
            () => binding.Apply(new NeonRgba(1f, 0f, 0f, 1f)));

        Assert.Equal(
            (typeof(InvalidOperationException), 0, 0, 0, 0),
            (
                failure?.GetType(),
                first.WriteCount,
                invalid.WriteCount,
                first.PropertyBlock.ColorProperties.Count,
                invalid.PropertyBlock.ColorProperties.Count));
    }

    [Fact]
    public void MissingEmissionSlotCollectionIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NeonLetterEmissionBinding(null!));
    }

    [Fact]
    public void EmptyEmissionSlotCollectionIsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => new NeonLetterEmissionBinding(
                Array.Empty<IEmissionBindingSlot>()));
    }

    [Fact]
    public void NullLaterSlotPreventsEveryPropertyBlockWrite()
    {
        var first = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 0,
            intensity: 2f,
            foreignValue: 41f);
        var binding = new NeonLetterEmissionBinding(
            new IEmissionBindingSlot[] { first, null! });

        Exception? failure = Record.Exception(
            () => binding.Apply(new NeonRgba(1f, 0f, 0f, 1f)));

        Assert.Equal(
            (typeof(InvalidOperationException), 0, 0),
            (
                failure?.GetType(),
                first.WriteCount,
                first.PropertyBlock.ColorProperties.Count));
    }

    [Fact]
    public void DestroyedLaterRendererPreventsEveryPropertyBlockWrite()
    {
        var first = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 0,
            intensity: 2f,
            foreignValue: 41f);
        var destroyed = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 1,
            intensity: 3f,
            foreignValue: 42f)
        {
            IsRendererAlive = false
        };
        var binding = new NeonLetterEmissionBinding(
            new IEmissionBindingSlot[] { first, destroyed });

        Exception? failure = Record.Exception(
            () => binding.Apply(new NeonRgba(1f, 0f, 0f, 1f)));

        Assert.Equal(
            (typeof(InvalidOperationException), 0, 0),
            (failure?.GetType(), first.WriteCount, destroyed.WriteCount));
    }

    [Fact]
    public void DestroyedLaterMaterialPreventsEveryPropertyBlockWrite()
    {
        var first = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 0,
            intensity: 2f,
            foreignValue: 41f);
        var destroyed = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 1,
            intensity: 3f,
            foreignValue: 42f)
        {
            IsMaterialAlive = false
        };
        var binding = new NeonLetterEmissionBinding(
            new IEmissionBindingSlot[] { first, destroyed });

        Exception? failure = Record.Exception(
            () => binding.Apply(new NeonRgba(1f, 0f, 0f, 1f)));

        Assert.Equal(
            (typeof(InvalidOperationException), 0, 0),
            (failure?.GetType(), first.WriteCount, destroyed.WriteCount));
    }

    [Fact]
    public void MissingLaterPropertyBlockPreventsEveryWrite()
    {
        var first = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 0,
            intensity: 2f,
            foreignValue: 41f);
        var missingBlock = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 1,
            intensity: 3f,
            foreignValue: 42f)
        {
            HasPropertyBlock = false
        };
        var binding = new NeonLetterEmissionBinding(
            new IEmissionBindingSlot[] { first, missingBlock });

        Exception? failure = Record.Exception(
            () => binding.Apply(new NeonRgba(1f, 0f, 0f, 1f)));

        Assert.Equal(
            (typeof(InvalidOperationException), 0, 0),
            (failure?.GetType(), first.WriteCount, missingBlock.WriteCount));
    }

    [Theory]
    [InlineData(0.02f)]
    [InlineData(0.04045f)]
    public void LowSrgbComponentsUseTheLinearSegment(float component)
    {
        var slot = new InstrumentedEmissionSlot(
            rendererName: "letter",
            materialIndex: 0,
            intensity: 1f,
            foreignValue: 41f);
        var binding = new NeonLetterEmissionBinding(
            new IEmissionBindingSlot[] { slot });

        binding.Apply(new NeonRgba(component, 0f, 0f, 1f));

        NeonRgba applied = slot.PropertyBlock.ColorProperties[
            NeonLetterEmissionPolicy.EmissiveColorPropertyName];
        Assert.Equal(component / 12.92f, applied.Red);
    }

    [Fact]
    public void LegacyPolicyRejectsMissingVisualSubtree()
    {
        NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.Get('A');

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterEmissionPolicy.Apply(
                definition,
                new IEmissionVisualSubtree[]
                {
                    new FakeEmissionSubtree("unrelated")
                },
                NeonRgba.ProjectCyan));
    }

    [Fact]
    public void LegacyPolicyRejectsNullRenderer()
    {
        NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.Get('A');
        var validRenderer = new FakeEmissionRenderer(
            "valid",
            new FakeEmissionMaterial(2f));

        Exception? failure = Record.Exception(
            () => NeonLetterEmissionPolicy.Apply(
                definition,
                new IEmissionVisualSubtree[]
                {
                    new FakeEmissionSubtree(
                        definition.ColliderVisualChildName,
                        new IEmissionRenderer[]
                        {
                            null!,
                            validRenderer
                        })
                },
                NeonRgba.ProjectCyan));

        Assert.Equal(
            (typeof(InvalidOperationException), 0),
            (failure?.GetType(), validRenderer.Writes.Count));
    }

    [Fact]
    public void LegacyPolicyRejectsRendererWithoutMaterials()
    {
        NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.Get('A');
        var validRenderer = new FakeEmissionRenderer(
            "valid",
            new FakeEmissionMaterial(2f));

        Exception? failure = Record.Exception(
            () => NeonLetterEmissionPolicy.Apply(
                definition,
                new IEmissionVisualSubtree[]
                {
                    new FakeEmissionSubtree(
                        definition.ColliderVisualChildName,
                        new FakeEmissionRenderer("empty"),
                        validRenderer)
                },
                NeonRgba.ProjectCyan));

        Assert.Equal(
            (typeof(InvalidOperationException), 0),
            (failure?.GetType(), validRenderer.Writes.Count));
    }

    [Fact]
    public void LegacyPolicyRejectsNullMaterial()
    {
        NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.Get('A');

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterEmissionPolicy.Apply(
                definition,
                new IEmissionVisualSubtree[]
                {
                    new FakeEmissionSubtree(
                        definition.ColliderVisualChildName,
                        new FakeEmissionRenderer(
                            "letter",
                            new FakeEmissionMaterial[] { null! }))
                },
                NeonRgba.ProjectCyan));
    }

    [Fact]
    public void LegacyPolicyRejectsMissingDefinition()
    {
        Assert.Throws<ArgumentNullException>(
            () => NeonLetterEmissionPolicy.Apply(
                null!,
                Array.Empty<IEmissionVisualSubtree>(),
                NeonRgba.ProjectCyan));
    }

    [Fact]
    public void LegacyPolicyRejectsMissingCandidateSubtrees()
    {
        Assert.Throws<ArgumentNullException>(
            () => NeonLetterEmissionPolicy.Apply(
                NeonLetterSmallCatalog.Get('A'),
                null!,
                NeonRgba.ProjectCyan));
    }

    private static NeonLetterEmissionBindingCache<
        FakeRoot,
        FakeDefinition,
        FakeBinding> CreateReferenceTrackingCache()
    {
        return new NeonLetterEmissionBindingCache<
            FakeRoot,
            FakeDefinition,
            FakeBinding>(
            static root => root.IsAlive,
            static (root, definition) =>
                new FakeBinding(root, definition, slotCount: 1));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        WeakReference Root,
        WeakReference Definition,
        WeakReference Binding) CacheThenRemove(
            NeonLetterEmissionBindingCache<
                FakeRoot,
                FakeDefinition,
                FakeBinding> cache)
    {
        var root = new FakeRoot();
        var definition = new FakeDefinition();
        FakeBinding binding = cache.GetOrCreate(
            17,
            root,
            definition,
            recipeId: 29);
        var references = (
            new WeakReference(root),
            new WeakReference(definition),
            new WeakReference(binding));

        cache.Remove(17, root);
        return references;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        WeakReference Root,
        WeakReference Definition,
        WeakReference Binding) CacheThenClear(
            NeonLetterEmissionBindingCache<
                FakeRoot,
                FakeDefinition,
                FakeBinding> cache)
    {
        var root = new FakeRoot();
        var definition = new FakeDefinition();
        FakeBinding binding = cache.GetOrCreate(
            17,
            root,
            definition,
            recipeId: 29);
        var references = (
            new WeakReference(root),
            new WeakReference(definition),
            new WeakReference(binding));

        cache.Clear();
        return references;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        WeakReference Root,
        WeakReference Definition,
        Type? FailureType) FailRecursiveCreation(
            NeonLetterEmissionBindingCache<
                FakeRoot,
                FakeDefinition,
                FakeBinding> cache)
    {
        var root = new FakeRoot();
        var definition = new FakeDefinition();
        var rootReference = new WeakReference(root);
        var definitionReference = new WeakReference(definition);
        Exception? failure = Record.Exception(
            () => cache.GetOrCreate(
                17,
                root,
                definition,
                recipeId: 29));

        return (
            rootReference,
            definitionReference,
            failure?.GetType());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectReleasedReferences()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class FakeRoot
    {
        public bool IsAlive { get; set; } = true;
    }

    private sealed class FakeDefinition
    {
    }

    private sealed class FakeBinding
    {
        private readonly FakeRoot _root;
        private readonly FakeDefinition _definition;
        private readonly int[] _slotValues;
        private readonly int[] _slotApplyCounts;

        public FakeBinding(
            FakeRoot root,
            FakeDefinition definition,
            int slotCount)
        {
            _root = root;
            _definition = definition;
            _slotValues = new int[slotCount];
            _slotApplyCounts = new int[slotCount];
        }

        public int ApplyCount { get; private set; }
        public IReadOnlyList<int> SlotValues => _slotValues;
        public IReadOnlyList<int> SlotApplyCounts => _slotApplyCounts;

        public void Apply(int value)
        {
            ApplyCount++;
            for (int slotIndex = 0;
                 slotIndex < _slotValues.Length;
                 slotIndex++)
            {
                _slotValues[slotIndex] = value;
                _slotApplyCounts[slotIndex]++;
            }

            GC.KeepAlive(_root);
            GC.KeepAlive(_definition);
        }
    }

    private sealed class InstrumentedEmissionBindingFactory
    {
        private readonly int _slotCount;
        private readonly List<InstrumentedEmissionSlot[]> _slotsByBinding =
            new();

        public InstrumentedEmissionBindingFactory(int slotCount)
        {
            _slotCount = slotCount;
        }

        public int DiscoveryCount { get; private set; }
        public int TotalApplyCount =>
            _slotsByBinding.Sum(slots => slots[0].WriteCount);
        public IReadOnlyList<int> TotalSlotWriteCounts =>
            Enumerable.Range(0, _slotCount)
                .Select(slotIndex =>
                    _slotsByBinding.Sum(slots =>
                        slots[slotIndex].WriteCount))
                .ToArray();
        public IReadOnlyList<float> LatestRedValues =>
            _slotsByBinding[^1]
                .Select(slot => slot.PropertyBlock.ColorProperties[
                    NeonLetterEmissionPolicy.EmissiveColorPropertyName].Red)
                .ToArray();
        public IReadOnlyList<float> LatestForeignValues =>
            _slotsByBinding[^1]
                .Select(slot => slot.PropertyBlock.FloatProperties["foreign"])
                .ToArray();

        public NeonLetterEmissionBinding Discover(
            FakeRoot root,
            FakeDefinition definition)
        {
            DiscoveryCount++;
            var slots = new InstrumentedEmissionSlot[_slotCount];
            for (int slotIndex = 0;
                 slotIndex < slots.Length;
                 slotIndex++)
            {
                slots[slotIndex] = new InstrumentedEmissionSlot(
                    rendererName: "letter",
                    materialIndex: slotIndex,
                    intensity: slotIndex + 2f,
                    foreignValue: slotIndex + 100f);
            }

            _slotsByBinding.Add(slots);
            GC.KeepAlive(root);
            GC.KeepAlive(definition);
            return new NeonLetterEmissionBinding(slots);
        }
    }

    private sealed class InstrumentedEmissionSlot : IEmissionBindingSlot
    {
        public InstrumentedEmissionSlot(
            string rendererName,
            int materialIndex,
            float intensity,
            float foreignValue)
        {
            RendererName = rendererName;
            MaterialIndex = materialIndex;
            Intensity = intensity;
            PropertyBlock.FloatProperties["foreign"] = foreignValue;
        }

        public string RendererName { get; }
        public int MaterialIndex { get; }
        public bool IsRendererAlive { get; set; } = true;
        public bool IsMaterialAlive { get; set; } = true;
        public bool HasPropertyBlock { get; set; } = true;
        public float Intensity { get; set; }
        public int IntensityReadCount { get; private set; }
        public int PropertyBlockReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public InstrumentedEmissionPropertyBlock PropertyBlock { get; } =
            new();

        public float ReadEmissiveIntensity()
        {
            IntensityReadCount++;
            return Intensity;
        }

        public IEmissionPropertyBlock ReadPropertyBlock()
        {
            PropertyBlockReadCount++;
            return HasPropertyBlock
                ? PropertyBlock
                : null!;
        }

        public void WritePropertyBlock(
            IEmissionPropertyBlock propertyBlock)
        {
            if (!ReferenceEquals(propertyBlock, PropertyBlock))
            {
                throw new InvalidOperationException(
                    "The slot received another slot's property block.");
            }

            WriteCount++;
        }
    }

    private sealed class InstrumentedEmissionPropertyBlock
        : IEmissionPropertyBlock
    {
        public Dictionary<string, float> FloatProperties { get; } = new();
        public Dictionary<string, NeonRgba> ColorProperties { get; } = new();

        public void SetColor(string propertyName, NeonRgba color)
        {
            ColorProperties[propertyName] = color;
        }
    }
}
