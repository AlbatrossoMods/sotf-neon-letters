using SOTFNeonLetters;
using Xunit;

public sealed class NeonLetterDemolitionModeTests
{
    [Fact]
    public void DismantlingANeonLetterUsesCollapseWithoutARelocationOverride()
    {
        var target = new FakePlacementTarget();

        RecipePlacementApplicator.Apply(
            NeonLetterASmallDefinition.Placement,
            target);

        Assert.Equal(
            (1, (object?)null),
            (target.RelocateModeValue, target.RelocateRecipeOverride));
    }

    [Fact]
    public void FailedRecipeSetupRestoresRelocationModeAndOverride()
    {
        var originalOverride = new object();
        var target = new FakePlacementTarget
        {
            RelocateModeValue = 0,
            RelocateRecipeOverride = originalOverride
        };
        RecipeRelocationState<object> original =
            RecipeDemolitionApplicator.Capture(target);
        RecipeDemolitionApplicator.Apply(target);

        RecipeDemolitionApplicator.Restore(target, original);

        Assert.Equal(
            (0, originalOverride),
            (target.RelocateModeValue, target.RelocateRecipeOverride));
    }

    [Fact]
    public void RelocationStateCannotBeCapturedWithoutARecipeTarget()
    {
        Assert.Throws<ArgumentNullException>(
            () => RecipeDemolitionApplicator.Capture<object>(null!));
    }

    [Fact]
    public void DemolitionModeCannotBeAppliedWithoutARecipeTarget()
    {
        Assert.Throws<ArgumentNullException>(
            () => RecipeDemolitionApplicator.Apply<object>(null!));
    }

    [Fact]
    public void DemolitionModeCannotBeInspectedWithoutARecipeTarget()
    {
        Assert.Throws<ArgumentNullException>(
            () => RecipeDemolitionApplicator.IsApplied<object>(null!));
    }

    [Fact]
    public void RelocationStateCannotBeRestoredWithoutARecipeTarget()
    {
        Assert.Throws<ArgumentNullException>(
            () => RecipeDemolitionApplicator.Restore<object>(
                null!,
                default));
    }

    [Fact]
    public void RelocateModeIsNotDemolitionEvenWithoutAnOverride()
    {
        var target = new FakePlacementTarget
        {
            RelocateModeValue = 0,
            RelocateRecipeOverride = null
        };

        Assert.False(RecipeDemolitionApplicator.IsApplied(target));
    }

    [Fact]
    public void CollapseWithARelocationOverrideIsNotDemolitionOnly()
    {
        var target = new FakePlacementTarget
        {
            RelocateModeValue = 1,
            RelocateRecipeOverride = new object()
        };

        Assert.False(RecipeDemolitionApplicator.IsApplied(target));
    }
}
