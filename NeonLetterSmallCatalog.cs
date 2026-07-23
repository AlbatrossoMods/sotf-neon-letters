#nullable enable

namespace SOTFNeonLetters;

public enum NeonLetterBookSlot
{
    Top,
    Bottom
}

public sealed class NeonLetterSmallDefinition
{
    internal NeonLetterSmallDefinition(
        NeonSymbolManifestEntry manifestEntry,
        int catalogIndex)
    {
        Symbol = manifestEntry.Symbol;
        UnicodeCode = manifestEntry.UnicodeCode;
        AssetKey = manifestEntry.AssetKey;
        SourceNodeName = manifestEntry.SourceNodeName;
        Source = manifestEntry.Source;
        RecipeId = NeonLetterSmallCatalog.BaseRecipeId + catalogIndex * 2;
        BookPageIndex = catalogIndex / 2;
        BookSlot = catalogIndex % 2 == 0
            ? NeonLetterBookSlot.Top
            : NeonLetterBookSlot.Bottom;
        Ingredients = Array.AsReadOnly(
            new[]
            {
                new IngredientDefinition(NeonLetterSmallCatalog.WireIngredientName, 418),
                new IngredientDefinition(ColliderVisualChildName, 635)
            });
    }

    public char Symbol { get; }
    public char Letter => Symbol;
    public string UnicodeCode { get; }
    public string AssetKey { get; }
    public string SourceNodeName { get; }
    public NeonSymbolSource Source { get; }
    public int RecipeId { get; }
    public int CraftingNodeId => RecipeId - 1;
    public int BookPageIndex { get; }
    public NeonLetterBookSlot BookSlot { get; }
    public string RecipeName => $"Neon Letter {Symbol} (Small)";
    public string PrefabAssetName => $"NeonLetter_{AssetKey}_Small";
    public string BookIconAssetName => $"NeonLetter_{AssetKey}_Small_Icon";
    public string BookPageAssetName => $"NeonLetters_Small_Page_{BookPageIndex + 1:00}";
    public string BookPageTitleLocalizationKey =>
        NeonLetterSmallCatalog.GetBookPageTitleLocalizationKey(BookPageIndex);
    public string ColliderVisualChildName => $"Ingredient_LightBulb_{AssetKey}";
    public IReadOnlyList<IngredientDefinition> Ingredients { get; }

    public bool IsColliderVisualChild(string childName)
    {
        return string.Equals(childName, ColliderVisualChildName, StringComparison.Ordinal);
    }

    public ColliderSize ResolveColliderSize(
        float visualWidth,
        float visualHeight,
        float visualDepth)
    {
        return new ColliderSize(
            visualWidth,
            visualHeight,
            Math.Max(visualDepth, NeonLetterSmallCatalog.MinimumColliderDepth));
    }

    public readonly record struct IngredientDefinition(string ChildName, int ItemId);
    public readonly record struct ColliderSize(float Width, float Height, float Depth);
}

public static class NeonLetterSmallCatalog
{
    public const int BaseRecipeId = 1_904_177_201;
    public const string BundleName = "sotfneonletters";
    public const string WireIngredientName = "Ingredient_Wire_Lead";
    public const string BookPageTitleLocalizationKeyPrefix =
        "BLUEPRINT_PAGE_SOTF_NEON_LETTERS";
    public const string BookPageTitle = "Neon Symbols";
    public const string LetterShaderName = "HDRP/Lit";
    public const string WireShaderName = "HDRP/Lit";
    public const int BookPageWidth = 1024;
    public const int BookPageHeight = 1024;
    public const int BookPageMipCount = 11;
    public const int BookIconSize = 128;
    public const int BookIconMipCount = 8;
    public const float MinimumColliderDepth = 0.08f;

    private static readonly IReadOnlyList<NeonLetterSmallDefinition> Definitions =
        Array.AsReadOnly(CreateDefinitions());
    private static readonly IReadOnlyDictionary<char, NeonLetterSmallDefinition> BySymbol =
        Definitions.ToDictionary(definition => definition.Symbol);

    public static IReadOnlyList<NeonLetterSmallDefinition> All => Definitions;
    public static NeonLetterASmallDefinition.PlacementDefinition Placement =>
        NeonLetterASmallDefinition.Placement;

    public static NeonLetterSmallDefinition Get(char letter)
    {
        char normalized = char.ToUpperInvariant(letter);
        if (!BySymbol.TryGetValue(normalized, out NeonLetterSmallDefinition? definition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(letter),
                letter,
                "The requested neon symbol is not supported.");
        }

        return definition;
    }

    public static string GetBookPageTitleLocalizationKey(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Definitions.Count / 2)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        return $"{BookPageTitleLocalizationKeyPrefix}_{pageIndex + 1:00}";
    }

    private static NeonLetterSmallDefinition[] CreateDefinitions()
    {
        IReadOnlyList<NeonSymbolManifestEntry> manifest = NeonSymbolManifest.All;
        var definitions = new NeonLetterSmallDefinition[manifest.Count];
        for (int index = 0; index < definitions.Length; index++)
        {
            definitions[index] = new NeonLetterSmallDefinition(
                manifest[index],
                index);
        }

        return definitions;
    }
}
