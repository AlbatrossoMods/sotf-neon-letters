using RedLoader;
using Sons.Crafting.Structures;
using SonsSdk;
using SonsSdk.Networking;
using UnityEngine;

namespace SOTFNeonLetters;

public static partial class NeonLetterColorRuntime
{
    private const int MaxInteractionLeaseSweepsPerUpdate = 16;
    private const int MaxInteractionBackfillsPerUpdate = 64;
    private static readonly Func<ColorInteractionLease, bool>
        IsInteractionLeaseAliveCallback = IsInteractionLeaseAlive;
    private static long _interactionUpdateTick;

    private static void AdvanceColorInteractions()
    {
        if (IsDedicatedOrHeadless())
        {
            ReleaseInteractions(MaxInteractionLeaseSweepsPerUpdate);
            return;
        }

        if (_interactionUpdateTick < long.MaxValue)
        {
            _interactionUpdateTick++;
        }

        int inspectionsRemaining =
            MaxInteractionLeaseSweepsPerUpdate;
        while (inspectionsRemaining > 0)
        {
            bool removed = InteractionLeases.TryTakeNextDead(
                inspectionsRemaining,
                IsInteractionLeaseAliveCallback,
                out ColorInteractionLease expiredLease,
                out int inspectedEntries);
            inspectionsRemaining -= inspectedEntries;
            if (!removed)
            {
                break;
            }

            DisposeInteractionLease(expiredLease);
        }

        InteractionBackfill.TryStartDueCycle(
            _interactionUpdateTick);

        AdvanceInteractionBackfill();
    }

    private static bool IsInteractionLeaseAlive(
        ColorInteractionLease lease)
    {
        try
        {
            if (IsDedicatedOrHeadless() ||
                !lease.IsOwnedHierarchyAlive ||
                !lease.TryGetLiveStructure(
                    out ScrewStructure liveStructure,
                    out GameObject structureRoot) ||
                structureRoot.GetInstanceID() !=
                    lease.StructureInstanceId ||
                liveStructure.Recipe?.Id != lease.RecipeId ||
                !NeonLetterColorInteractionPolicy.IsEditable(
                    hasCompletedStructure: true,
                    lease.RecipeId))
            {
                return false;
            }

            lease.SetBlocked(
                lease.IsDismantling ||
                !GameState.IsPlayerControllable ||
                SOTFNeonLettersUi.IsOpen);
            return true;
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Native color interaction liveness " +
                $"check failed: {exception}");
            return false;
        }
    }

    private static void AdvanceInteractionBackfill()
    {
        if (!InteractionBackfill.IsPending)
        {
            return;
        }

        if (!ScrewStructureManager.TryGetInstance(
                out ScrewStructureManager manager) ||
            manager._structures == null)
        {
            InteractionBackfill.ReportUnavailable();
            return;
        }

        Il2CppSystem.Collections.Generic.List<IScrewStructure> structures =
            manager._structures;
        NeonLetterColorInteractionBackfillWindow window =
            InteractionBackfill.TakeWindow(
                structures.Count,
                MaxInteractionBackfillsPerUpdate);
        int endOffset = window.StartOffset + window.Count;
        for (int index = window.StartOffset;
             index < endOffset &&
             index < structures.Count;
             index++)
        {
            RegisterColorInteraction(
                structures[index],
                beginsNewLifecycle: false);
        }
    }

    private static bool IsDedicatedOrHeadless()
    {
        return NetUtils.IsDedicatedServer ||
               Application.isBatchMode;
    }

    private static void ReleaseAllInteractions()
    {
        while (InteractionLeases.TryTakeFirst(
                   out ColorInteractionLease lease))
        {
            DisposeInteractionLease(lease);
        }
    }

    private static void ReleaseInteractions(int maximumLeases)
    {
        for (int released = 0;
             released < maximumLeases &&
             InteractionLeases.TryTakeFirst(
                 out ColorInteractionLease lease);
             released++)
        {
            DisposeInteractionLease(lease);
        }
    }

    private static void DisposeInteractionLease(
        ColorInteractionLease lease)
    {
        if (lease == null)
        {
            return;
        }

        try
        {
            SOTFNeonLettersUi.OnStructureUnavailable(
                lease.StructureInstanceId);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to close a removed native " +
                $"color interaction target: {exception}");
        }

        try
        {
            lease.Dispose();
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Native color interaction cleanup " +
                $"failed: {exception}");
        }
    }

    private static void ResetInteractionDiscovery()
    {
        _interactionUpdateTick = 0;
        InteractionCreationFailures.Clear();
        InteractionBackfill.Reset();
    }
}
