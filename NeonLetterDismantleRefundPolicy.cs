#nullable enable

namespace SOTFNeonLetters;

public static class NeonLetterDismantleRefundPolicy
{
    private static readonly IReadOnlyDictionary<int, int[]> ItemIdsByRecipeId =
        NeonLetterSmallCatalog.All.ToDictionary(
            definition => definition.RecipeId,
            definition => definition.Ingredients
                .Select(ingredient => ingredient.ItemId)
                .ToArray());

    public static IReadOnlyList<int> ResolveItemIds(int recipeId)
    {
        return ItemIdsByRecipeId.TryGetValue(recipeId, out int[]? itemIds)
            ? itemIds
            : Array.Empty<int>();
    }

    public static bool ShouldSpawnRefund(bool isMultiplayer, bool isServer)
    {
        return !isMultiplayer || isServer;
    }
}
