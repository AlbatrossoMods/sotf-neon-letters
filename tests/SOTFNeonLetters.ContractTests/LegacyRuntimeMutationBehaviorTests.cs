using SOTFNeonLetters;
using Xunit;

public sealed class LegacyRuntimeMutationBehaviorTests
{
    [Fact]
    public void WallMountedSymbolPlacementMatchesTheCanonicalWallShelfContract()
    {
        NeonLetterASmallDefinition.PlacementDefinition placement =
            NeonLetterASmallDefinition.Placement;

        Assert.Equal(
            (
                Anchor: NeonLetterASmallDefinition.PlacementAnchor.Back,
                CastRadiusFormula: NeonLetterASmallDefinition.PlacementCastRadiusFormula.Z,
                AlignToSurface: true,
                CanBeRotated: false,
                ForceUp: true,
                LockUpwardVector: true,
                InitialRotationX: 0f,
                InitialRotationY: 0f,
                InitialRotationZ: 0f,
                AllowsTreePlacement: true,
                AllowsNonTreePlacement: false,
                MinimumHeightAboveTree: 0.5f,
                MaximumHeightAboveTree: 4f,
                AllowDynamicObjectParenting: true,
                AllowScrewStructureParenting: true,
                AllowFreeFormStructureParenting: true,
                UseFreeFormStructures: false,
                AutoFoundation: false,
                UseOverridePlacementSize: false,
                PlacementDepthSizeRatio: 1f),
            (
                placement.Anchor,
                placement.CastRadiusFormula,
                placement.AlignToSurface,
                placement.CanBeRotated,
                placement.ForceUp,
                placement.LockUpwardVector,
                placement.InitialRotationX,
                placement.InitialRotationY,
                placement.InitialRotationZ,
                placement.AllowsTreePlacement,
                placement.AllowsNonTreePlacement,
                placement.MinimumHeightAboveTree,
                placement.MaximumHeightAboveTree,
                placement.AllowDynamicObjectParenting,
                placement.AllowScrewStructureParenting,
                placement.AllowFreeFormStructureParenting,
                placement.UseFreeFormStructures,
                placement.AutoFoundation,
                placement.UseOverridePlacementSize,
                placement.PlacementDepthSizeRatio));
    }

    [Fact]
    public void WallMountedPlacementDisablesInheritedGroundOnlyLookupModes()
    {
        var target = new PlacementRetentionTarget();
        Assert.True(target.UseFreeFormStructures);
        Assert.True(target.AutoFoundation);

        RecipePlacementApplicator.Apply(
            NeonLetterASmallDefinition.Placement,
            target);

        Assert.False(target.UseFreeFormStructures);
        Assert.False(target.AutoFoundation);
    }

    [Fact]
    public void PlacementApplicationRejectsRetainedGroundChecks()
    {
        var target = new PlacementRetentionTarget
        {
            RetainGroundRemoval = false
        };

        Exception? error = Record.Exception(
            () => RecipePlacementApplicator.Apply(
                NeonLetterASmallDefinition.Placement,
                target));

        Assert.Equal(typeof(InvalidOperationException), error?.GetType());
    }

    [Fact]
    public void PlacementApplicationRejectsRetainedParentOverrides()
    {
        var target = new PlacementRetentionTarget
        {
            RetainParentOverrides = false
        };

        Exception? error = Record.Exception(
            () => RecipePlacementApplicator.Apply(
                NeonLetterASmallDefinition.Placement,
                target));

        Assert.Equal(typeof(InvalidOperationException), error?.GetType());
    }

    [Fact]
    public void PlacementApplicationRejectsAnIncompletePlacementSnapshot()
    {
        var target = new PlacementRetentionTarget
        {
            RetainSnapshot = false
        };

        Exception? error = Record.Exception(
            () => RecipePlacementApplicator.Apply(
                NeonLetterASmallDefinition.Placement,
                target));

        Assert.Equal(typeof(InvalidOperationException), error?.GetType());
    }

    [Fact]
    public void ColorUpsertRemovesDeserializedNullsBeforeReplacingAnEntry()
    {
        var envelope = new NeonLetterColorSaveEnvelope
        {
            Entries = new List<NeonLetterColorSaveEntry>
            {
                null!,
                new(saveId: 7, recipeId: 11, NeonRgba.ProjectCyan)
            }
        };
        var replacement = new NeonLetterColorSaveEntry(
            saveId: 7,
            recipeId: 12,
            new NeonRgba(1f, 0f, 0f, 1f));

        NeonLetterColorStore.Upsert(envelope, replacement);

        Assert.Equal(
            (Count: 1, SaveId: 7, RecipeId: 12, SameEntry: true),
            (
                Count: envelope.Entries.Count,
                SaveId: envelope.Entries[0].SaveId,
                RecipeId: envelope.Entries[0].RecipeId,
                SameEntry: ReferenceEquals(
                    replacement,
                    envelope.Entries[0])));
    }

    [Fact]
    public void RolledBackMaterialLeaseCannotBecomeSdkOwned()
    {
        var source = new TransactionMaterial("source", cloneDepth: 0);
        var renderer = new TransactionRenderer("renderer", source);
        var factory = new TransactionMaterialFactory();
        var transaction = new RuntimeMaterialCatalogTransaction(
            () => factory,
            new[]
            {
                new RuntimeMaterialCatalogEntry(
                    "prefab",
                    new IRuntimeRendererHandle[] { renderer })
            });
        RuntimeMaterialReplacementLease lease = transaction.Execute();
        lease.Rollback();

        Exception? error = Record.Exception(lease.Retain);

        Assert.Equal(
            (
                Error: typeof(InvalidOperationException),
                OriginalRestored: true,
                Released: 1),
            (
                Error: error?.GetType(),
                OriginalRestored: ReferenceEquals(
                    source,
                    renderer.Materials[0]),
                Released: factory.Released.Count));
    }

    [Fact]
    public void MaterialRollbackRestoresAndReleasesEverySlotInReverseOrder()
    {
        var operations = new List<string>();
        var firstSource = new TransactionMaterial("first", cloneDepth: 0);
        var secondSource = new TransactionMaterial("second", cloneDepth: 0);
        var firstRenderer = new TransactionRenderer(
            "first-renderer",
            operations,
            firstSource);
        var secondRenderer = new TransactionRenderer(
            "second-renderer",
            operations,
            secondSource);
        var factory = new TransactionMaterialFactory();
        var transaction = new RuntimeMaterialCatalogTransaction(
            () => factory,
            new[]
            {
                new RuntimeMaterialCatalogEntry(
                    "prefab",
                    new IRuntimeRendererHandle[]
                    {
                        firstRenderer,
                        secondRenderer
                    })
            });
        RuntimeMaterialReplacementLease lease = transaction.Execute();

        lease.Rollback();

        Assert.Equal(
            (
                Operations:
                    "assign:first-renderer,assign:second-renderer," +
                    "restore:second-renderer,restore:first-renderer",
                Released: "clone-2,clone-1",
                FirstRestored: true,
                SecondRestored: true),
            (
                Operations: string.Join(",", operations),
                Released: string.Join(
                    ",",
                    factory.Released.Select(material => material.Id)),
                FirstRestored: ReferenceEquals(
                    firstSource,
                    firstRenderer.Materials[0]),
                SecondRestored: ReferenceEquals(
                    secondSource,
                    secondRenderer.Materials[0])));
    }

    [Fact]
    public void ClearingBookPagesForgetsRecipesAndCompletedPageIndexes()
    {
        var coordinator = new AlphabetBookPageCoordinator<string>();
        NeonLetterSmallDefinition first = NeonLetterSmallCatalog.All[0];
        NeonLetterSmallDefinition second = NeonLetterSmallCatalog.All[1];
        NeonLetterSmallDefinition third = NeonLetterSmallCatalog.All[2];
        NeonLetterSmallDefinition fourth = NeonLetterSmallCatalog.All[3];
        coordinator.Add(first, "first");
        ReadyAlphabetBookPage<string>? firstPage =
            coordinator.Add(second, "second");
        coordinator.MarkCompleted(firstPage!.PageIndex);
        coordinator.Add(third, "third");
        ReadyAlphabetBookPage<string>? secondPage =
            coordinator.Add(fourth, "fourth");

        coordinator.Clear();
        ReadyAlphabetBookPage<string>? afterClear =
            coordinator.GetNextReadyPage();
        coordinator.Add(first, "first-reloaded");
        ReadyAlphabetBookPage<string>? restarted =
            coordinator.Add(second, "second-reloaded");

        Assert.Equal(
            (
                FirstPage: 0,
                SecondPage: 1,
                ReadyAfterClear: false,
                RestartedPage: 0),
            (
                FirstPage: firstPage.PageIndex,
                SecondPage: secondPage?.PageIndex,
                ReadyAfterClear: afterClear != null,
                RestartedPage: restarted?.PageIndex));
    }

    private sealed class PlacementRetentionTarget : IRecipePlacementTarget
    {
        public bool RetainGroundRemoval { get; init; } = true;
        public bool RetainParentOverrides { get; init; } = true;
        public bool RetainSnapshot { get; init; } = true;
        public bool GroundPlacementChecksRemoved { get; private set; }
        public bool ParentRecipeOverridesCleared => RetainParentOverrides;
        public NeonLetterASmallDefinition.PlacementDefinition Snapshot
        {
            get
            {
                NeonLetterASmallDefinition.PlacementDefinition snapshot = new(
                    Anchor,
                    CastRadiusFormula,
                    AlignToSurface,
                    CanBeRotated,
                    ForceUp,
                    LockUpwardVector,
                    InitialRotationX,
                    InitialRotationY,
                    InitialRotationZ,
                    AllowsTreePlacement,
                    AllowsNonTreePlacement,
                    MinimumHeightAboveTree,
                    MaximumHeightAboveTree,
                    AllowDynamicObjectParenting,
                    AllowScrewStructureParenting,
                    AllowFreeFormStructureParenting,
                    UseFreeFormStructures,
                    AutoFoundation,
                    UseOverridePlacementSize,
                    PlacementDepthSizeRatio);
                return RetainSnapshot
                    ? snapshot
                    : snapshot with
                    {
                        CanBeRotated = !snapshot.CanBeRotated
                    };
            }
        }

        public NeonLetterASmallDefinition.PlacementAnchor Anchor { get; set; }
        public NeonLetterASmallDefinition.PlacementCastRadiusFormula
            CastRadiusFormula { get; set; }
        public bool AlignToSurface { get; set; }
        public bool CanBeRotated { get; set; }
        public bool ForceUp { get; set; }
        public bool LockUpwardVector { get; set; }
        public float InitialRotationX { get; private set; }
        public float InitialRotationY { get; private set; }
        public float InitialRotationZ { get; private set; }
        public bool AllowsTreePlacement { get; set; }
        public bool AllowsNonTreePlacement { get; set; }
        public float MinimumHeightAboveTree { get; set; }
        public float MaximumHeightAboveTree { get; set; }
        public bool AllowDynamicObjectParenting { get; set; }
        public bool AllowScrewStructureParenting { get; set; }
        public bool AllowFreeFormStructureParenting { get; set; }
        public bool UseFreeFormStructures { get; set; } = true;
        public bool AutoFoundation { get; set; } = true;
        public bool UseOverridePlacementSize { get; set; }
        public float PlacementDepthSizeRatio { get; set; }

        public void RemoveGroundPlacementChecks()
        {
            GroundPlacementChecksRemoved = RetainGroundRemoval;
        }

        public void SetInitialRotation(float x, float y, float z)
        {
            InitialRotationX = x;
            InitialRotationY = y;
            InitialRotationZ = z;
        }
    }
}
