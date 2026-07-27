using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Sons.Crafting.Structures;
using SonsSdk;
using UnityEngine;

namespace SOTFNeonLetters;

internal sealed class NeonLetterPrefabCloneActivator : MonoBehaviour
{
    static NeonLetterPrefabCloneActivator()
    {
        ClassInjector.RegisterTypeInIl2Cpp<NeonLetterPrefabCloneActivator>();
    }

    public NeonLetterPrefabCloneActivator(IntPtr nativePointer)
        : base(nativePointer)
    {
    }

    public void OnEnable()
    {
        if (gameObject.scene.name == "DontDestroyOnLoad")
        {
            return;
        }

        StructureCraftingNode craftingNode =
            gameObject.GetComponent<StructureCraftingNode>();
        if (craftingNode != null)
        {
            craftingNode.enabled = true;
        }

        ScrewStructure builtStructure =
            gameObject.GetComponent<ScrewStructure>();
        if (builtStructure != null)
        {
            builtStructure.enabled = true;
        }

        UnityEngine.Object.Destroy(this);
    }
}

[HarmonyPatch(
    typeof(UnityUtils),
    nameof(UnityUtils.AsPrefab),
    new[] { typeof(GameObject), typeof(MonoBehaviour[]) })]
internal static class NeonLetterPrefabActivationHarmony
{
    [HarmonyPrefix]
    private static bool BeforeSonsSdkPrefabActivation(GameObject go)
    {
        if (!NeonLetterPrefabCloneActivationPolicy
                .ShouldReplaceSonsSdkActivation(go?.name))
        {
            return true;
        }

        go.DontDestroyOnLoad();
        go.AddComponent<NeonLetterPrefabCloneActivator>();
        return false;
    }
}
