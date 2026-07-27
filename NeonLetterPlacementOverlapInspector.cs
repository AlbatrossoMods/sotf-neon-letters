using Il2CppInterop.Runtime;
using Sons.Crafting.Structures;
using TheForest.Player.Actions;
using UnityEngine;

namespace SOTFNeonLetters;

internal static class NeonLetterPlacementOverlapInspector
{
    public static NeonLetterPlacementOverlapSummary Inspect(
        PlayerStructurePlaceAction.SinglePlaceOperation operation,
        StructureCraftingNode preview,
        StructureRecipe recipe)
    {
        BoxCollider placementCollider = preview.GetComponent<BoxCollider>();
        if (placementCollider == null)
        {
            return new NeonLetterPlacementOverlapSummary(
                HasRecognizedFreeFormStructureSurfaceOverlap: false,
                OverlapsResolvedFreeFormStructureParent: false,
                HasExternalObstruction: true);
        }

        Transform colliderTransform = placementCollider.transform;
        Vector3 absoluteScale = colliderTransform.lossyScale;
        absoluteScale.x = Math.Abs(absoluteScale.x);
        absoluteScale.y = Math.Abs(absoluteScale.y);
        absoluteScale.z = Math.Abs(absoluteScale.z);
        Vector3 halfExtents =
            Vector3.Scale(placementCollider.size * 0.5f, absoluteScale);
        Collider[] overlaps = Physics.OverlapBox(
            colliderTransform.TransformPoint(placementCollider.center),
            halfExtents,
            colliderTransform.rotation,
            operation.GetOverlapsLayerMask(),
            QueryTriggerInteraction.Ignore);

        IntPtr resolvedFreeFormParentPointer =
            operation._freeFormParent == null
                ? IntPtr.Zero
                : IL2CPP.Il2CppObjectBaseToPtr(
                    operation._freeFormParent);
        bool hasRecognizedFreeFormStructureSurfaceOverlap = false;
        bool overlapsResolvedFreeFormStructureParent = false;
        foreach (Collider overlap in overlaps)
        {
            bool isPartOfPlacementPreview =
                overlap == null ||
                overlap.transform == preview.transform ||
                overlap.transform.IsChildOf(preview.transform);
            IntPtr overlapParentPointer = IntPtr.Zero;
            bool belongsToRecognizedFreeFormStructure =
                !isPartOfPlacementPreview &&
                TryGetRecognizedFreeFormStructureParentPointer(
                    recipe,
                    overlap,
                    out overlapParentPointer);
            bool belongsToResolvedFreeFormStructureParent =
                belongsToRecognizedFreeFormStructure &&
                resolvedFreeFormParentPointer != IntPtr.Zero &&
                overlapParentPointer ==
                resolvedFreeFormParentPointer;
            if (belongsToResolvedFreeFormStructureParent)
            {
                overlapsResolvedFreeFormStructureParent = true;
            }

            if (NeonLetterWallPlacementPolicy.IsExternalObstruction(
                    isPartOfPlacementPreview,
                    belongsToRecognizedFreeFormStructure,
                    belongsToResolvedFreeFormStructureParent))
            {
                return new NeonLetterPlacementOverlapSummary(
                    hasRecognizedFreeFormStructureSurfaceOverlap,
                    OverlapsResolvedFreeFormStructureParent:
                        overlapsResolvedFreeFormStructureParent,
                    HasExternalObstruction: true);
            }

            if (belongsToRecognizedFreeFormStructure)
            {
                hasRecognizedFreeFormStructureSurfaceOverlap = true;
            }
        }

        return new NeonLetterPlacementOverlapSummary(
            hasRecognizedFreeFormStructureSurfaceOverlap,
            OverlapsResolvedFreeFormStructureParent:
                overlapsResolvedFreeFormStructureParent,
            HasExternalObstruction: false);
    }

    internal static bool BelongsToRecognizedFreeFormStructure(
        StructureRecipe recipe,
        Collider overlap)
    {
        return TryGetRecognizedFreeFormStructureParentPointer(
            recipe,
            overlap,
            out _);
    }

    private static bool TryGetRecognizedFreeFormStructureParentPointer(
        StructureRecipe recipe,
        Collider overlap,
        out IntPtr parentPointer)
    {
        parentPointer = IntPtr.Zero;
        if (!PlayerStructurePlaceAction.SinglePlaceOperation
                .TryValidatePlaceOnFreeFormStructureRule(
                    recipe,
                    overlap.gameObject,
                    out IFreeFormStructure overlapParent) ||
            overlapParent == null)
        {
            return false;
        }

        parentPointer = IL2CPP.Il2CppObjectBaseToPtr(overlapParent);
        return parentPointer != IntPtr.Zero;
    }
}
