using Xunit;

namespace SOTFNeonLetters.ContractTests;

public sealed class NeonLetterWallPlacementPolicyTests
{
    [Fact]
    public void RecognizedSurfaceFromResolvedTargetParentIsNotExternalObstruction()
    {
        bool isExternalObstruction =
            NeonLetterWallPlacementPolicy.IsExternalObstruction(
                isPartOfPlacementPreview: false,
                belongsToRecognizedFreeFormStructure: true,
                belongsToResolvedFreeFormStructureParent: true);

        Assert.False(isExternalObstruction);
    }

    [Fact]
    public void AnotherObjectIsClassifiedAsExternalObstruction()
    {
        bool isExternalObstruction =
            NeonLetterWallPlacementPolicy.IsExternalObstruction(
                isPartOfPlacementPreview: false,
                belongsToRecognizedFreeFormStructure: false,
                belongsToResolvedFreeFormStructureParent: false);

        Assert.True(isExternalObstruction);
    }

    [Fact]
    public void PlacementPreviewDoesNotObstructItself()
    {
        bool isExternalObstruction =
            NeonLetterWallPlacementPolicy.IsExternalObstruction(
                isPartOfPlacementPreview: true,
                belongsToRecognizedFreeFormStructure: false,
                belongsToResolvedFreeFormStructureParent: false);

        Assert.False(isExternalObstruction);
    }

    [Fact]
    public void NativeRecognizedSurfaceFromDifferentParentIsExternalObstruction()
    {
        bool isExternalObstruction =
            NeonLetterWallPlacementPolicy.IsExternalObstruction(
                isPartOfPlacementPreview: false,
                belongsToRecognizedFreeFormStructure: true,
                belongsToResolvedFreeFormStructureParent: false);

        Assert.True(isExternalObstruction);
    }

    [Fact]
    public void TargetWallSurfaceOverlapCanBeIgnoredByPlacementSpaceCheck()
    {
        bool isSpaceValid =
            NeonLetterWallPlacementPolicy.ResolvePlacementSpaceValidity(
                nativePlacementSpaceIsClear: false,
                hasRecognizedFreeFormStructureSurfaceOverlap: true,
                overlapsResolvedFreeFormStructureParent: true,
                hasExternalObstruction: false);

        Assert.True(isSpaceValid);
    }

    [Fact]
    public void SpaceRejectionWithoutRecognizedFreeFormSurfaceRemainsRejected()
    {
        bool isSpaceValid =
            NeonLetterWallPlacementPolicy.ResolvePlacementSpaceValidity(
                nativePlacementSpaceIsClear: false,
                hasRecognizedFreeFormStructureSurfaceOverlap: false,
                overlapsResolvedFreeFormStructureParent: false,
                hasExternalObstruction: false);

        Assert.False(isSpaceValid);
    }

    [Fact]
    public void ExternalObstructionRemainsRejectedOnRecognizedStructureParent()
    {
        bool isSpaceValid =
            NeonLetterWallPlacementPolicy.ResolvePlacementSpaceValidity(
                nativePlacementSpaceIsClear: false,
                hasRecognizedFreeFormStructureSurfaceOverlap: true,
                overlapsResolvedFreeFormStructureParent: true,
                hasExternalObstruction: true);

        Assert.False(isSpaceValid);
    }

    [Fact]
    public void NativePlacementSpaceApprovalIsPreserved()
    {
        bool isSpaceValid =
            NeonLetterWallPlacementPolicy.ResolvePlacementSpaceValidity(
                nativePlacementSpaceIsClear: true,
                hasRecognizedFreeFormStructureSurfaceOverlap: false,
                overlapsResolvedFreeFormStructureParent: false,
                hasExternalObstruction: true);

        Assert.True(isSpaceValid);
    }

    [Fact]
    public void NativeSpaceRejectionWithoutTargetWallOverlapIsNotOverridden()
    {
        bool isSpaceValid =
            NeonLetterWallPlacementPolicy.ResolvePlacementSpaceValidity(
                nativePlacementSpaceIsClear: false,
                hasRecognizedFreeFormStructureSurfaceOverlap: false,
                overlapsResolvedFreeFormStructureParent: false,
                hasExternalObstruction: false);

        Assert.False(isSpaceValid);
    }

    [Fact]
    public void RecognizedSurfaceFromDifferentFreeFormParentDoesNotRecoverSpace()
    {
        bool isSpaceValid =
            NeonLetterWallPlacementPolicy.ResolvePlacementSpaceValidity(
                nativePlacementSpaceIsClear: false,
                hasRecognizedFreeFormStructureSurfaceOverlap: true,
                overlapsResolvedFreeFormStructureParent: false,
                hasExternalObstruction: false);

        Assert.False(isSpaceValid);
    }

    [Fact]
    public void BackAnchorNormalAtNativeLimitIsRecognizedAsEarlyNormalRejection()
    {
        bool isNormalLimitRejection =
            NeonLetterWallPlacementPolicy
                .IsBackAnchorSurfaceNormalLimitRejection(
                    nativePlacementTargetIsValid: false,
                    usesBackAnchor: true,
                    processedSurfaceNormal: true,
                    nativePlacementSpaceCheckWasCalled: false,
                    hasResolvedFreeFormStructureParent: true,
                    surfaceNormalY:
                        NeonLetterWallPlacementPolicy
                            .NativeBackAnchorMaximumAbsoluteSurfaceNormalY);

        Assert.True(isNormalLimitRejection);
    }

    [Fact]
    public void BackAnchorNormalBelowNativeLimitIsNotNormalLimitRejection()
    {
        bool isNormalLimitRejection =
            NeonLetterWallPlacementPolicy
                .IsBackAnchorSurfaceNormalLimitRejection(
                    nativePlacementTargetIsValid: false,
                    usesBackAnchor: true,
                    processedSurfaceNormal: true,
                    nativePlacementSpaceCheckWasCalled: false,
                    hasResolvedFreeFormStructureParent: true,
                    surfaceNormalY: 0.499f);

        Assert.False(isNormalLimitRejection);
    }

    [Fact]
    public void NativeSpaceCheckCallRulesOutEarlyBackAnchorNormalRejection()
    {
        bool isNormalLimitRejection =
            NeonLetterWallPlacementPolicy
                .IsBackAnchorSurfaceNormalLimitRejection(
                    nativePlacementTargetIsValid: false,
                    usesBackAnchor: true,
                    processedSurfaceNormal: true,
                    nativePlacementSpaceCheckWasCalled: true,
                    hasResolvedFreeFormStructureParent: true,
                    surfaceNormalY: 0.75f);

        Assert.False(isNormalLimitRejection);
    }

    [Fact]
    public void NormalLimitRecoveryRequiresAllSupplementalPlacementChecks()
    {
        bool canRecoverTarget =
            NeonLetterWallPlacementPolicy
                .CanRecoverBackAnchorSurfaceNormalLimitRejection(
                    isBackAnchorSurfaceNormalLimitRejection: true,
                    placementSpaceIsValid: true,
                    overlapsResolvedFreeFormStructureParent: true,
                    hasExternalObstruction: false,
                    passesAreaMask: true,
                    passesWorldBounds: true,
                    isWithinPlacementRange: true);

        Assert.True(canRecoverTarget);
    }

    [Theory]
    [InlineData(false, true, true, false, true, true, true)]
    [InlineData(true, false, true, false, true, true, true)]
    [InlineData(true, true, false, false, true, true, true)]
    [InlineData(true, true, true, true, true, true, true)]
    [InlineData(true, true, true, false, false, true, true)]
    [InlineData(true, true, true, false, true, false, true)]
    [InlineData(true, true, true, false, true, true, false)]
    public void FailedSupplementalCheckPreventsNormalLimitRecovery(
        bool isNormalLimitRejection,
        bool placementSpaceIsValid,
        bool overlapsResolvedFreeFormStructureParent,
        bool hasExternalObstruction,
        bool passesAreaMask,
        bool passesWorldBounds,
        bool isWithinPlacementRange)
    {
        bool canRecoverTarget =
            NeonLetterWallPlacementPolicy
                .CanRecoverBackAnchorSurfaceNormalLimitRejection(
                    isNormalLimitRejection,
                    placementSpaceIsValid,
                    overlapsResolvedFreeFormStructureParent,
                    hasExternalObstruction,
                    passesAreaMask,
                    passesWorldBounds,
                    isWithinPlacementRange);

        Assert.False(canRecoverTarget);
    }

    [Fact]
    public void PlacementOverrideAppliesOnlyToRegisteredNeonLetterRecipes()
    {
        int firstRecipeId = NeonLetterSmallCatalog.All[0].RecipeId;
        int lastRecipeId =
            NeonLetterSmallCatalog.All[NeonLetterSmallCatalog.All.Count - 1].RecipeId;

        Assert.True(NeonLetterWallPlacementPolicy.IsNeonLetterRecipeId(firstRecipeId));
        Assert.True(NeonLetterWallPlacementPolicy.IsNeonLetterRecipeId(lastRecipeId));
        Assert.False(NeonLetterWallPlacementPolicy.IsNeonLetterRecipeId(firstRecipeId - 1));
        Assert.False(NeonLetterWallPlacementPolicy.IsNeonLetterRecipeId(firstRecipeId + 1));
        Assert.False(NeonLetterWallPlacementPolicy.IsNeonLetterRecipeId(lastRecipeId + 2));
        Assert.False(NeonLetterWallPlacementPolicy.IsNeonLetterRecipeId(int.MinValue));
        Assert.False(NeonLetterWallPlacementPolicy.IsNeonLetterRecipeId(int.MaxValue));
    }
}
