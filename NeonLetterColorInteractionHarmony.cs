using HarmonyLib;
using Sons.Crafting.Structures;
using Sons.Gameplay;

namespace SOTFNeonLetters;

[HarmonyPatch(
    typeof(ScrewStructureManager),
    nameof(ScrewStructureManager.Register),
    new[] { typeof(IScrewStructure) })]
internal static class NeonLetterColorInteractionRegisterPatch
{
    [HarmonyPostfix]
    private static void AfterRegister(IScrewStructure __0)
    {
        NeonLetterColorRuntime.RegisterColorInteraction(
            __0,
            beginsNewLifecycle: true);
    }
}

[HarmonyPatch(
    typeof(ScrewStructureManager),
    nameof(ScrewStructureManager.Unregister),
    new[] { typeof(IScrewStructure) })]
internal static class NeonLetterColorInteractionUnregisterPatch
{
    [HarmonyPrefix]
    private static void BeforeUnregister(IScrewStructure __0)
    {
        NeonLetterColorRuntime.UnregisterColorInteraction(__0);
    }
}

[HarmonyPatch(
    typeof(GenericInteraction),
    nameof(GenericInteraction.OnEnable))]
internal static class NeonLetterColorInteractionPromptObservationPatch
{
    [HarmonyPostfix]
    private static void AfterOnEnable(GenericInteraction __instance)
    {
        NeonLetterColorRuntime.ObserveNativeInteractionPrompt(__instance);
    }
}
