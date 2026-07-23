#nullable enable

using HarmonyLib;
using Il2CppInterop.Runtime;
using RedLoader;
using RedLoader.Unity.IL2CPP.Utils.Collections;
using Sons.Crafting.Structures;
using SonsSdk.Networking;

namespace SOTFNeonLetters;

internal static class NeonLetterDismantleRuntime
{
    internal static void SpawnRefund(IScrewStructure dismantledStructure)
    {
        if (dismantledStructure == null ||
            !NeonLetterDismantleRefundPolicy.ShouldSpawnRefund(
                NetUtils.IsMultiplayer,
                NetUtils.IsServer))
        {
            return;
        }

        int recipeId = dismantledStructure.Recipe?.Id ?? int.MinValue;
        System.Collections.Generic.IReadOnlyList<int> itemIds =
            NeonLetterDismantleRefundPolicy.ResolveItemIds(recipeId);
        if (itemIds.Count == 0)
        {
            return;
        }

        ScrewStructure? concreteStructure = dismantledStructure.TryCast<ScrewStructure>();
        if (concreteStructure == null)
        {
            RLog.Error(
                "[SOTFNeonLetters] Cannot return dismantled-neon materials because " +
                "the native structure has no ScrewStructure component.");
            return;
        }

        var nativeItemIds = new Il2CppSystem.Collections.Generic.List<int>();
        foreach (int itemId in itemIds)
        {
            nativeItemIds.Add(itemId);
        }

        var nativeItemEnumerable =
            new Il2CppSystem.Collections.Generic.IEnumerable<int>(
                IL2CPP.Il2CppObjectBaseToPtrNotNull(nativeItemIds));
        try
        {
            Coroutines.Start(
                new ManagedIl2CppEnumerator(
                    ScrewStructureDestruction.SpawnItemsWorker(
                        nativeItemEnumerable,
                        concreteStructure.transform.position,
                        concreteStructure.transform.rotation)));
        }
        catch (Exception exception)
        {
            RLog.Error(
                "[SOTFNeonLetters] Failed to return materials from a dismantled neon " +
                $"letter: {exception}");
        }
    }
}

[HarmonyPatch(
    typeof(ScrewStructureManager),
    nameof(ScrewStructureManager.RegisterDismantled),
    new[] { typeof(IScrewStructure) })]
internal static class NeonLetterDismantlePatch
{
    [HarmonyPrefix]
    private static void BeforeRegisterDismantled(IScrewStructure screwStructureBase)
    {
        NeonLetterDismantleRuntime.SpawnRefund(screwStructureBase);
    }
}
