using HarmonyLib;
using Il2CppInterop.Runtime;
using Sons.Crafting.Structures;
using TheForest.Player.Actions;
using UnityEngine;

namespace SOTFNeonLetters;

[HarmonyPatch(
    typeof(PlayerStructurePlaceAction.SinglePlaceOperation),
    nameof(
        PlayerStructurePlaceAction.SinglePlaceOperation
            .CheckIfPlacementSpaceIsValid),
    new[] { typeof(StructureCraftingNode), typeof(float) })]
internal static class NeonLetterWallPlacementHarmony
{
    [HarmonyPostfix]
    private static void AfterPlacementSpaceCheck(
        PlayerStructurePlaceAction.SinglePlaceOperation __instance,
        StructureCraftingNode __0,
        ref bool __result)
    {
        StructureRecipe recipe = __instance?.Recipe;
        if (recipe == null ||
            __0 == null ||
            !NeonLetterWallPlacementPolicy.IsNeonLetterRecipeId(recipe.Id))
        {
            return;
        }

        if (NeonLetterWallPlacementFrameState
            .IsSupplementalSpaceCheck(__instance))
        {
            return;
        }

        try
        {
            if (!__result)
            {
                NeonLetterPlacementOverlapSummary overlapSummary =
                    NeonLetterPlacementOverlapInspector.Inspect(
                        __instance,
                        __0,
                        recipe);
                __result =
                    NeonLetterWallPlacementPolicy
                        .ResolvePlacementSpaceValidity(
                            nativePlacementSpaceIsClear: false,
                            overlapSummary
                                .HasRecognizedFreeFormStructureSurfaceOverlap,
                            overlapSummary
                                .OverlapsResolvedFreeFormStructureParent,
                            overlapSummary.HasExternalObstruction);
            }
        }
        catch
        {
            // A failed supplemental check must preserve the native rejection.
        }
        finally
        {
            NeonLetterWallPlacementFrameState
                .RecordPlacementSpaceCheck(__instance);
        }
    }
}

[HarmonyPatch(
    typeof(PlayerStructurePlaceAction.SinglePlaceOperation),
    nameof(
        PlayerStructurePlaceAction.SinglePlaceOperation
            .TheForest_Player_Actions_PlayerStructurePlaceAction_IPlaceOperation_Update))]
internal static class NeonLetterWallPlacementUpdateHarmony
{
    [HarmonyPrefix]
    private static void BeforePlacementUpdate(
        PlayerStructurePlaceAction.SinglePlaceOperation __instance)
    {
        NeonLetterWallPlacementFrameState.Begin(__instance);
    }

    [HarmonyPostfix]
    private static void AfterPlacementUpdate(
        PlayerStructurePlaceAction.SinglePlaceOperation __instance)
    {
        if (!NeonLetterWallPlacementFrameState.TryEnd(
                __instance,
                out NeonLetterWallPlacementFrameSnapshot frame))
        {
            return;
        }

        StructureRecipe recipe = __instance.Recipe;
        StructureCraftingNode preview =
            __instance.PreviewStructureNode;
        if (recipe == null || preview == null)
        {
            return;
        }

        bool isSurfaceNormalLimitRejection =
            NeonLetterWallPlacementPolicy
                .IsBackAnchorSurfaceNormalLimitRejection(
                    __instance.HasValidPlacementTarget,
                    recipe._anchor ==
                    StructureRecipe.AnchorType.Back,
                    frame.ProcessedSurfaceNormal,
                    frame.PlacementSpaceCheckWasCalled,
                    __instance._freeFormParent != null,
                    frame.SurfaceNormal.y);
        if (!isSurfaceNormalLimitRejection)
        {
            return;
        }

        try
        {
            NeonLetterWallPlacementFrameState
                .BeginSupplementalSpaceCheck(__instance);
            bool nativePlacementSpaceIsValid;
            try
            {
                nativePlacementSpaceIsValid =
                    __instance.CheckIfPlacementSpaceIsValid(
                        preview,
                        0f);
            }
            finally
            {
                NeonLetterWallPlacementFrameState
                    .EndSupplementalSpaceCheck(__instance);
            }

            NeonLetterPlacementOverlapSummary overlapSummary =
                NeonLetterPlacementOverlapInspector.Inspect(
                    __instance,
                    preview,
                    recipe);
            bool placementSpaceIsValid =
                NeonLetterWallPlacementPolicy
                    .ResolvePlacementSpaceValidity(
                        nativePlacementSpaceIsValid,
                        overlapSummary
                            .HasRecognizedFreeFormStructureSurfaceOverlap,
                        overlapSummary
                            .OverlapsResolvedFreeFormStructureParent,
                        overlapSummary.HasExternalObstruction);
            bool passesAreaMask =
                __instance.PassesAreaMask(recipe);
            bool passesWorldBounds =
                !recipe.BlockUsingWorldBounds ||
                __instance.PassesWorldBoundsCheck(
                    preview.transform.position,
                    recipe.WorldBoundsDefinition,
                    recipe.WorldBoundsOffset);
            bool isWithinPlacementRange =
                __instance._rayTransform != null &&
                Vector3.Distance(
                    __instance._rayTransform.position,
                    preview.transform.position) <=
                __instance._maxPlacementRange;

            if (NeonLetterWallPlacementPolicy
                .CanRecoverBackAnchorSurfaceNormalLimitRejection(
                    isSurfaceNormalLimitRejection,
                    placementSpaceIsValid,
                    overlapSummary
                        .OverlapsResolvedFreeFormStructureParent,
                    overlapSummary.HasExternalObstruction,
                    passesAreaMask,
                    passesWorldBounds,
                    isWithinPlacementRange))
            {
                __instance
                    ._HasValidPlacementTarget_k__BackingField = true;
            }
        }
        catch
        {
            // Supplemental validation is fail-closed.
        }
    }
}

[HarmonyPatch(
    typeof(PlayerStructurePlaceAction.SinglePlaceOperation),
    nameof(
        PlayerStructurePlaceAction.SinglePlaceOperation
            .ProcessRotationAndAlignment),
    new[] { typeof(Vector3) })]
internal static class NeonLetterWallPlacementAlignmentHarmony
{
    [HarmonyPrefix]
    private static void BeforeRotationAndAlignment(
        PlayerStructurePlaceAction.SinglePlaceOperation __instance,
        Vector3 __0)
    {
        NeonLetterWallPlacementFrameState
            .RecordSurfaceNormal(__instance, __0);
    }
}

internal readonly record struct NeonLetterWallPlacementFrameSnapshot(
    bool ProcessedSurfaceNormal,
    Vector3 SurfaceNormal,
    bool PlacementSpaceCheckWasCalled);

internal static class NeonLetterWallPlacementFrameState
{
    private static IntPtr _operationPointer;
    private static bool _processedSurfaceNormal;
    private static Vector3 _surfaceNormal;
    private static bool _placementSpaceCheckWasCalled;
    private static IntPtr _supplementalSpaceCheckOperationPointer;

    public static void Begin(
        PlayerStructurePlaceAction.SinglePlaceOperation operation)
    {
        StructureRecipe recipe = operation?.Recipe;
        if (recipe == null ||
            !NeonLetterWallPlacementPolicy
                .IsNeonLetterRecipeId(recipe.Id))
        {
            Reset();
            return;
        }

        _operationPointer =
            IL2CPP.Il2CppObjectBaseToPtr(operation);
        _processedSurfaceNormal = false;
        _surfaceNormal = Vector3.zero;
        _placementSpaceCheckWasCalled = false;
    }

    public static void RecordSurfaceNormal(
        PlayerStructurePlaceAction.SinglePlaceOperation operation,
        Vector3 surfaceNormal)
    {
        if (!IsCurrentOperation(operation))
        {
            return;
        }

        _processedSurfaceNormal = true;
        _surfaceNormal = surfaceNormal;
    }

    public static void RecordPlacementSpaceCheck(
        PlayerStructurePlaceAction.SinglePlaceOperation operation)
    {
        if (!IsCurrentOperation(operation))
        {
            return;
        }

        _placementSpaceCheckWasCalled = true;
    }

    public static bool TryEnd(
        PlayerStructurePlaceAction.SinglePlaceOperation operation,
        out NeonLetterWallPlacementFrameSnapshot snapshot)
    {
        if (!IsCurrentOperation(operation))
        {
            snapshot = default;
            Reset();
            return false;
        }

        snapshot = new NeonLetterWallPlacementFrameSnapshot(
            _processedSurfaceNormal,
            _surfaceNormal,
            _placementSpaceCheckWasCalled);
        Reset();
        return true;
    }

    public static void BeginSupplementalSpaceCheck(
        PlayerStructurePlaceAction.SinglePlaceOperation operation)
    {
        _supplementalSpaceCheckOperationPointer =
            operation == null
                ? IntPtr.Zero
                : IL2CPP.Il2CppObjectBaseToPtr(operation);
    }

    public static bool IsSupplementalSpaceCheck(
        PlayerStructurePlaceAction.SinglePlaceOperation operation)
    {
        return _supplementalSpaceCheckOperationPointer !=
               IntPtr.Zero &&
               operation != null &&
               IL2CPP.Il2CppObjectBaseToPtr(operation) ==
               _supplementalSpaceCheckOperationPointer;
    }

    public static void EndSupplementalSpaceCheck(
        PlayerStructurePlaceAction.SinglePlaceOperation operation)
    {
        if (IsSupplementalSpaceCheck(operation))
        {
            _supplementalSpaceCheckOperationPointer =
                IntPtr.Zero;
        }
    }

    private static bool IsCurrentOperation(
        PlayerStructurePlaceAction.SinglePlaceOperation operation)
    {
        return _operationPointer != IntPtr.Zero &&
               operation != null &&
               IL2CPP.Il2CppObjectBaseToPtr(operation) ==
               _operationPointer;
    }

    private static void Reset()
    {
        _operationPointer = IntPtr.Zero;
        _processedSurfaceNormal = false;
        _surfaceNormal = Vector3.zero;
        _placementSpaceCheckWasCalled = false;
    }
}
