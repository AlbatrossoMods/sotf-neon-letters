namespace SOTFNeonLetters;

public readonly record struct NeonLetterPlacementOverlapSummary(
    bool HasRecognizedFreeFormStructureSurfaceOverlap,
    bool OverlapsResolvedFreeFormStructureParent,
    bool HasExternalObstruction);

public static class NeonLetterWallPlacementPolicy
{
    public const float NativeBackAnchorMaximumAbsoluteSurfaceNormalY =
        0.5f;

    public static bool IsExternalObstruction(
        bool isPartOfPlacementPreview,
        bool belongsToRecognizedFreeFormStructure,
        bool belongsToResolvedFreeFormStructureParent)
    {
        return !isPartOfPlacementPreview &&
               (!belongsToRecognizedFreeFormStructure ||
                !belongsToResolvedFreeFormStructureParent);
    }

    public static bool ResolvePlacementSpaceValidity(
        bool nativePlacementSpaceIsClear,
        bool hasRecognizedFreeFormStructureSurfaceOverlap,
        bool overlapsResolvedFreeFormStructureParent,
        bool hasExternalObstruction)
    {
        return nativePlacementSpaceIsClear ||
               (hasRecognizedFreeFormStructureSurfaceOverlap &&
                overlapsResolvedFreeFormStructureParent &&
                !hasExternalObstruction);
    }

    public static bool IsBackAnchorSurfaceNormalLimitRejection(
        bool nativePlacementTargetIsValid,
        bool usesBackAnchor,
        bool processedSurfaceNormal,
        bool nativePlacementSpaceCheckWasCalled,
        bool hasResolvedFreeFormStructureParent,
        float surfaceNormalY)
    {
        return !nativePlacementTargetIsValid &&
               usesBackAnchor &&
               processedSurfaceNormal &&
               !nativePlacementSpaceCheckWasCalled &&
               hasResolvedFreeFormStructureParent &&
               Math.Abs(surfaceNormalY) >=
               NativeBackAnchorMaximumAbsoluteSurfaceNormalY;
    }

    public static bool CanRecoverBackAnchorSurfaceNormalLimitRejection(
        bool isBackAnchorSurfaceNormalLimitRejection,
        bool placementSpaceIsValid,
        bool overlapsResolvedFreeFormStructureParent,
        bool hasExternalObstruction,
        bool passesAreaMask,
        bool passesWorldBounds,
        bool isWithinPlacementRange)
    {
        return isBackAnchorSurfaceNormalLimitRejection &&
               placementSpaceIsValid &&
               overlapsResolvedFreeFormStructureParent &&
               !hasExternalObstruction &&
               passesAreaMask &&
               passesWorldBounds &&
               isWithinPlacementRange;
    }

    public static bool IsNeonLetterRecipeId(int recipeId)
    {
        long catalogOffset =
            (long)recipeId - NeonLetterSmallCatalog.BaseRecipeId;
        return catalogOffset >= 0 &&
               catalogOffset % 2 == 0 &&
               catalogOffset / 2 < NeonLetterSmallCatalog.All.Count;
    }
}
