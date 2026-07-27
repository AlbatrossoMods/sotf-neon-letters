#nullable enable

namespace SOTFNeonLetters;

public static class NeonLetterPrefabCloneActivationPolicy
{
    public static bool ShouldReplaceSonsSdkActivation(string? prefabName)
    {
        if (string.IsNullOrEmpty(prefabName))
        {
            return false;
        }

        foreach (NeonLetterSmallDefinition definition in NeonLetterSmallCatalog.All)
        {
            if (string.Equals(
                    prefabName,
                    $"{definition.PrefabAssetName}(Clone)",
                    StringComparison.Ordinal) ||
                string.Equals(
                    prefabName,
                    $"{definition.PrefabAssetName}CraftingNode",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
