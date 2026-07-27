using RedLoader;
using Sons.Crafting.Structures;
using Sons.Gameplay;
using Sons.Gui.Input;
using SonsSdk.Networking;
using UnityEngine;

namespace SOTFNeonLetters;

public static partial class NeonLetterColorRuntime
{
    private const string NativeUseAction = "Use";
    private static readonly NeonLetterColorInteractionFailureGate
        InteractionFailureGate = new();
    private static readonly HashSet<int> OwnedInteractionInstanceIds = new();
    private static readonly NeonLetterColorInteractionPromptLifecycle<
        GameObject> PromptLifecycle = new(IsPromptTemplateAlive);
    private static readonly NeonLetterColorInteractionBackfillSchedule
        BackfillSchedule = new();
    private static bool _acceptPromptObservations;
    private static bool _isObservingPrompt;

    internal static void BeginInteractionPromptObservation()
    {
        _acceptPromptObservations = !IsDedicatedOrHeadless();
    }

    internal static void EndInteractionPromptObservation()
    {
        _acceptPromptObservations = false;
        ResetInteractionDiscovery();
    }

    internal static void ObserveNativeInteractionPrompt(
        GenericInteraction interaction)
    {
        if (!_acceptPromptObservations ||
            _isObservingPrompt ||
            interaction == null)
        {
            return;
        }

        _isObservingPrompt = true;
        try
        {
            GameObject interactionObject = interaction.gameObject;
            GameObject prompt = interaction._interactGui;
            bool isOwned =
                interactionObject != null &&
                OwnedInteractionInstanceIds.Contains(
                    interactionObject.GetInstanceID());
            DynamicInputIcon inputIcon =
                prompt == null
                    ? null
                    : prompt.GetComponentInChildren<DynamicInputIcon>(true);
            // The cloned prompt is rebound to NativeUseAction, so the
            // source interaction can use any native action.
            NeonLetterColorInteractionPromptObservationResult result =
                PromptLifecycle.Observe(
                    new NeonLetterColorInteractionPromptCandidate<GameObject>(
                        IsOwnedColorInteraction: isOwned,
                        HasInteractionGui: prompt != null,
                        HasDynamicInputIcon: inputIcon != null,
                        prompt));
            if (result !=
                NeonLetterColorInteractionPromptObservationResult.Accepted)
            {
                return;
            }

            BackfillSchedule.Reset();
            BackfillSchedule.TryBeginAttempt(_interactionUpdateTick);
            InteractionFailureGate.ResetPromptFailureReport();
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Native prompt observation failed: " +
                exception);
        }
        finally
        {
            _isObservingPrompt = false;
        }
    }

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

        if (!InteractionCreationFailures.AllowsAttempt(
                structureInstanceId,
                _interactionUpdateTick))
        {
            return;
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

            if (!TryGetPromptTemplate(out GameObject promptTemplate) ||
                !NeonLetterColorInteractionPolicy.ShouldCreateLease(
                    isDedicatedOrHeadless: false,
                    hasCompletedStructure: true,
                    recipeId,
                    hasPromptTemplate: true))
            {
                return;
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
                geometry,
                promptTemplate);
            if (!InteractionLeases.TryAdd(
                    structureInstanceId,
                    lease))
            {
                DisposeInteractionLease(lease);
                InteractionCreationFailures.RecordSuccess(
                    structureInstanceId);
                return;
            }

            OwnedInteractionInstanceIds.Add(
                lease.InteractionInstanceId);
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

    private static bool TryGetPromptTemplate(
        out GameObject promptTemplate)
    {
        if (PromptLifecycle.TryGetTemplate(out promptTemplate))
        {
            return true;
        }

        promptTemplate = null;
        LogMissingPromptOnce();
        return false;
    }

    private static bool IsPromptTemplateAlive(GameObject promptTemplate)
    {
        return promptTemplate != null &&
               promptTemplate.GetComponentInChildren<DynamicInputIcon>(
                   true) != null;
    }

    private static void LogMissingPromptOnce()
    {
        if (!InteractionFailureGate.TryBeginPromptFailureReport())
        {
            return;
        }

        RLog.Error(
            "[SOTFNeonLetters] Native color interaction is waiting for a " +
            "compatible vanilla interaction prompt. The mod will keep " +
            "observing native interaction lifecycle events; no raw-key " +
            "fallback was installed.");
    }

}
