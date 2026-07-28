using RedLoader;
using Sons.Crafting.Structures;
using SonsSdk.Networking;
using UnityEngine;

namespace SOTFNeonLetters;

public static partial class NeonLetterColorRuntime
{
    private const string NativeUseAction = "Use";
    private static readonly NeonLetterColorInteractionBackfillCoordinator
        InteractionBackfill = new();

    internal static void RegisterColorInteraction(
        IScrewStructure registeredStructure,
        bool beginsNewLifecycle)
    {
        if (!_initialized ||
            IsDedicatedOrHeadless() ||
            registeredStructure == null)
        {
            return;
        }

        ScrewStructure structure;
        GameObject structureRoot;
        try
        {
            structure = registeredStructure.TryCast<ScrewStructure>();
            structureRoot = structure?.gameObject;
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to inspect one native color " +
                $"interaction target: {exception}");
            return;
        }

        if (structure == null || structureRoot == null)
        {
            return;
        }

        int structureInstanceId = structureRoot.GetInstanceID();
        if (beginsNewLifecycle)
        {
            InteractionCreationFailures.Remove(structureInstanceId);
        }

        int recipeId = int.MinValue;
        try
        {
            recipeId = registeredStructure.Recipe?.Id ?? int.MinValue;
            if (!NeonLetterColorInteractionPolicy.IsEditable(
                    hasCompletedStructure: true,
                    recipeId))
            {
                return;
            }

            NeonLetterSmallDefinition definition =
                ResolveRestoreDefinition(recipeId);
            if (definition == null)
            {
                return;
            }

            TryApplyInitialMaterialColor(
                structure,
                structureRoot,
                definition);

            if (!InteractionCreationFailures.AllowsAttempt(
                    structureInstanceId,
                    _interactionUpdateTick))
            {
                return;
            }

            if (InteractionLeases.TryGet(
                    structureInstanceId,
                    out ColorInteractionLease existingLease))
            {
                if (existingLease.MatchesLiveRoot(structureInstanceId))
                {
                    InteractionCreationFailures.RecordSuccess(
                        structureInstanceId);
                    return;
                }

                InteractionLeases.TryRemove(
                    structureInstanceId,
                    out _);
                DisposeInteractionLease(existingLease);
            }

            BoxCollider rootCollider =
                structureRoot.GetComponent<BoxCollider>();
            if (rootCollider == null)
            {
                if (InteractionCreationFailures.RecordTerminalFailure(
                        structureInstanceId,
                        MissingRootColliderFailure))
                {
                    RLog.Error(
                        $"[SOTFNeonLetters] Cannot create a native color " +
                        $"interaction for recipe {recipeId}: the completed " +
                        $"structure root has no BoxCollider.");
                }

                return;
            }

            NeonLetterColorInteractionGeometry geometry;
            try
            {
                geometry =
                    NeonLetterColorInteractionGeometryPolicy.Resolve(
                        new NeonLetterColorInteractionBounds(
                            rootCollider.center.x,
                            rootCollider.center.y,
                            rootCollider.center.z,
                            rootCollider.size.x,
                            rootCollider.size.y,
                            rootCollider.size.z));
            }
            catch (ArgumentException exception)
            {
                RecordTerminalCreationFailure(
                    structureInstanceId,
                    recipeId,
                    exception);
                return;
            }

            ColorInteractionLease lease = CreateColorInteractionLease(
                structure,
                structureRoot,
                recipeId,
                geometry);
            if (!InteractionLeases.TryAdd(
                    structureInstanceId,
                    lease))
            {
                DisposeInteractionLease(lease);
                InteractionCreationFailures.RecordSuccess(
                    structureInstanceId);
                return;
            }

            try
            {
                lease.Activate();
                InteractionCreationFailures.RecordSuccess(
                    structureInstanceId);
            }
            catch
            {
                InteractionLeases.TryRemove(
                    structureInstanceId,
                    out _);
                DisposeInteractionLease(lease);
                throw;
            }
        }
        catch (Exception exception)
        {
            RecordTransientCreationFailure(
                structureInstanceId,
                recipeId,
                exception);
        }
    }

    internal static void UnregisterColorInteraction(
        IScrewStructure registeredStructure)
    {
        if (registeredStructure == null)
        {
            return;
        }

        try
        {
            ScrewStructure structure =
                registeredStructure.TryCast<ScrewStructure>();
            GameObject structureRoot = structure?.gameObject;
            if (structure == null || structureRoot == null)
            {
                return;
            }

            int structureInstanceId = structureRoot.GetInstanceID();
            InteractionCreationFailures.Remove(structureInstanceId);
            MaterialInitialization.Remove(
                structureInstanceId,
                structureRoot);
            if (InteractionLeases.TryRemove(
                    structureInstanceId,
                    out ColorInteractionLease lease))
            {
                DisposeInteractionLease(lease);
            }
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to release one native color " +
                $"interaction: {exception}");
        }
    }

    internal static void RemoveColorInteraction(int structureInstanceId)
    {
        InteractionCreationFailures.Remove(structureInstanceId);
        if (InteractionLeases.TryRemove(
                structureInstanceId,
                out ColorInteractionLease lease))
        {
            DisposeInteractionLease(lease);
        }
    }

    internal static void SetColorInteractionDismantling(
        int structureInstanceId,
        bool isDismantling)
    {
        try
        {
            if (InteractionLeases.TryGet(
                    structureInstanceId,
                    out ColorInteractionLease lease))
            {
                lease.IsDismantling = isDismantling;
                lease.SetBlocked(
                    isDismantling ||
                    SOTFNeonLettersUi.IsOpen);
            }
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to change native color " +
                $"interaction dismantle state: {exception}");
        }
    }

}
