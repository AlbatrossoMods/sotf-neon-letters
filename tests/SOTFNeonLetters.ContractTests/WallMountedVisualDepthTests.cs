using Xunit;

namespace SOTFNeonLetters.ContractTests;

public sealed class WallMountedVisualDepthTests
{
    [Fact]
    public void CenteredGeometryIsMovedCompletelyInFrontOfTheWallAnchorPlane()
    {
        const float originalMinimumDepth = -0.06f;
        const float originalMaximumDepth = 0.06f;

        WallMountedVisualDepthLayout layout =
            WallMountedVisualDepthPolicy.Resolve(
                originalMinimumDepth,
                originalMaximumDepth);

        Assert.Equal(0.07f, layout.OutwardTranslation, precision: 5);
        Assert.Equal(
            WallMountedVisualDepthPolicy.SurfaceClearance,
            layout.MinimumDepth,
            precision: 5);
        Assert.Equal(0.13f, layout.MaximumDepth, precision: 5);
    }

    [Fact]
    public void CompletedWallMountedPrefabMovesEveryIngredientAndRollsBackAfterCallbackFailure()
    {
        var wire = new FakeVisual(localDepth: 0f);
        var letter = new FakeVisual(localDepth: 0.32f);
        var collider = new FakeCollider(centerDepth: 0f, depth: 0.12f);
        WallMountedVisualDepthLayout layout =
            WallMountedVisualDepthPolicy.Resolve(
                minimumDepth: -0.06f,
                maximumDepth: 0.06f);
        var visualMutation = new WallMountedVisualDepthMutation<FakeVisual>(
            new[] { wire, letter },
            layout,
            target => target.LocalDepth,
            (target, depth) => target.LocalDepth = depth);
        bool observedCompleteAppliedState = false;

        Assert.Throws<InvalidOperationException>(
            () => NeonLetterCallbackTransaction.Execute(
                transaction =>
                {
                    transaction.Apply(
                        visualMutation.Apply,
                        visualMutation.Restore);
                    transaction.Apply(
                        () =>
                        {
                            collider.CenterDepth =
                                layout.TranslateDepth(collider.CenterDepth);
                            observedCompleteAppliedState =
                                Math.Abs(wire.LocalDepth - 0.07f) < 0.00001f &&
                                Math.Abs(letter.LocalDepth - 0.39f) < 0.00001f &&
                                Math.Abs(collider.CenterDepth - 0.07f) < 0.00001f &&
                                Math.Abs(collider.Depth - 0.12f) < 0.00001f;
                        },
                        () =>
                        {
                            collider.CenterDepth = 0f;
                            collider.Depth = 0.12f;
                        });
                    transaction.Apply(
                        () => throw new InvalidOperationException(
                            "Simulated failure after completed-prefab depth mutations."),
                        () => { });
                }));

        Assert.True(observedCompleteAppliedState);
        Assert.Equal(0f, wire.LocalDepth);
        Assert.Equal(0.32f, letter.LocalDepth);
        Assert.Equal(0f, collider.CenterDepth);
        Assert.Equal(0.12f, collider.Depth);
    }

    [Fact]
    public void WallMountedPlacementPreviewColliderDoesNotPenetrateTheSupportingSurface()
    {
        const float colliderDepth = 0.08f;
        WallMountedVisualDepthLayout layout =
            WallMountedVisualDepthPolicy.Resolve(
                minimumDepth: -0.01f,
                maximumDepth: 0.01f);

        float adjustedVisualCenter = layout.TranslateDepth(0f);
        float adjustedColliderCenter =
            WallMountedVisualDepthPolicy.ResolveColliderCenterDepth(
                adjustedVisualCenter,
                colliderDepth);
        float adjustedColliderMinimumDepth =
            adjustedColliderCenter - colliderDepth / 2f;

        Assert.Equal(
            WallMountedVisualDepthPolicy.SurfaceClearance,
            adjustedColliderMinimumDepth,
            precision: 5);
    }

    [Fact]
    public void PlacementPreviewAndCompletedPrefabUseTheSameWallDepthCorrection()
    {
        string source = File.ReadAllText(
            FindRepositoryFile("NeonLetterASmallBlueprint.cs"))
            .ReplaceLineEndings("\n");

        Assert.Contains(
            "PrepareCraftingNodeVisualDepth(craftingNode, definition)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "craftingNodeVisualDepth.AdjustedVisualBounds",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "transaction.Apply(\n" +
            "                    craftingNodeVisualDepth.Apply,\n" +
            "                    craftingNodeVisualDepth.Restore);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FailedBlueprintRegistrationRestoresInheritedGroundOnlyLookupModes()
    {
        string source = File.ReadAllText(
            FindRepositoryFile("NeonLetterASmallBlueprint.cs"));

        Assert.Contains(
            "bool useFreeFormStructures = recipe._useFreeFormStructures;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "bool autoFoundation = recipe._autoFoundation;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "recipe._useFreeFormStructures = useFreeFormStructures;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "recipe._autoFoundation = autoFoundation;",
            source,
            StringComparison.Ordinal);
    }

    private sealed class FakeVisual
    {
        public FakeVisual(float localDepth)
        {
            LocalDepth = localDepth;
        }

        public float LocalDepth { get; set; }
    }

    private sealed class FakeCollider
    {
        public FakeCollider(float centerDepth, float depth)
        {
            CenterDepth = centerDepth;
            Depth = depth;
        }

        public float CenterDepth { get; set; }
        public float Depth { get; set; }
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
            $"Could not find repository file '{relativePath}'.");
    }
}
