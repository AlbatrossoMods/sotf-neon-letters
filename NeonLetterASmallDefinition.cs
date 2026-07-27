namespace SOTFNeonLetters;

public static class NeonLetterASmallDefinition
{
    public const string RecipeName = "Neon Letter A (Small)";
    public const string BundleName = "sotfneonletters";
    public const string PrefabAssetName = "NeonLetter_A_Small";
    public const string BookPageAssetName = "NeonLetters_Small_Page_01";
    public const string BookIconAssetName = "NeonLetter_A_Small_Icon";
    public const string BookPageTitleLocalizationKey = "BLUEPRINT_PAGE_SOTF_NEON_LETTERS";
    public const string BookPageTitle = "Neon Letters";
    public const string LetterShaderName = "HDRP/Lit";
    public const string WireShaderName = "HDRP/Lit";

    public const int RecipeId = 1_904_177_201;
    public const int CraftingNodeId = RecipeId - 1;
    public const int BookPageWidth = 1024;
    public const int BookPageHeight = 1024;
    public const int BookPageMipCount = 11;
    public const int BookIconSize = 128;
    public const int BookIconMipCount = 8;
    public const float MinimumColliderDepth = 0.08f;
    public const string ColliderVisualChildName = "Ingredient_LightBulb_A";

    public static PlacementDefinition Placement { get; } = new(
        PlacementAnchor.Back,
        PlacementCastRadiusFormula.Z,
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
        PlacementDepthSizeRatio: 1f);

    public static IReadOnlyList<IngredientDefinition> Ingredients { get; } = Array.AsReadOnly(
        new[]
        {
            new IngredientDefinition("Ingredient_Wire_Lead", 418),
            new IngredientDefinition("Ingredient_LightBulb_A", 635)
        });

    public static float ResolveColliderDepth(float visualDepth)
    {
        return Math.Max(visualDepth, MinimumColliderDepth);
    }

    public static ColliderSize ResolveColliderSize(
        float visualWidth,
        float visualHeight,
        float visualDepth)
    {
        return new ColliderSize(
            visualWidth,
            visualHeight,
            ResolveColliderDepth(visualDepth));
    }

    public static bool IsColliderVisualChild(string childName)
    {
        return string.Equals(childName, ColliderVisualChildName, StringComparison.Ordinal);
    }

    public readonly record struct IngredientDefinition(string ChildName, int ItemId);

    public readonly record struct ColliderSize(float Width, float Height, float Depth);

    public readonly record struct PlacementDefinition(
        PlacementAnchor Anchor,
        PlacementCastRadiusFormula CastRadiusFormula,
        bool AlignToSurface,
        bool CanBeRotated,
        bool ForceUp,
        bool LockUpwardVector,
        float InitialRotationX,
        float InitialRotationY,
        float InitialRotationZ,
        bool AllowsTreePlacement,
        bool AllowsNonTreePlacement,
        float MinimumHeightAboveTree,
        float MaximumHeightAboveTree,
        bool AllowDynamicObjectParenting,
        bool AllowScrewStructureParenting,
        bool AllowFreeFormStructureParenting,
        bool UseFreeFormStructures,
        bool AutoFoundation,
        bool UseOverridePlacementSize,
        float PlacementDepthSizeRatio);

    public enum PlacementAnchor
    {
        Back
    }

    public enum PlacementCastRadiusFormula
    {
        Z
    }
}
