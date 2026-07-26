#nullable enable

using Bolt;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RedLoader;
using RedLoader.Unity.IL2CPP.Utils.Collections;
using Sons.Crafting.Structures;
using SonsSdk.Networking;
using UnityEngine;

namespace SOTFNeonLetters;

internal static class NeonLetterDismantleRuntime
{
    internal static NeonLetterDismantleState? Capture(
        IScrewStructure dismantledStructure)
    {
        try
        {
            if (dismantledStructure == null)
            {
                return null;
            }

            int recipeId = dismantledStructure.Recipe?.Id ?? int.MinValue;
            System.Collections.Generic.IReadOnlyList<int> itemIds =
                NeonLetterDismantleRefundPolicy.ResolveItemIds(recipeId);
            if (itemIds.Count == 0)
            {
                return null;
            }

            ScrewStructure? concreteStructure =
                dismantledStructure.TryCast<ScrewStructure>();
            if (concreteStructure == null ||
                concreteStructure.gameObject == null)
            {
                LogFailure(
                    "Cannot clean up dismantled-neon state or return materials " +
                    "because the native structure has no ScrewStructure component.");
                return null;
            }

            int structureInstanceId =
                concreteStructure.gameObject.GetInstanceID();
            int? saveId = TryResolveOwnedSaveId(
                dismantledStructure,
                structureInstanceId);
            ulong? networkIdentity =
                TryResolveNetworkIdentity(concreteStructure);
            NeonLetterRefundState? refund = TryPrepareRefund(
                concreteStructure,
                itemIds);
            var state = new NeonLetterDismantleState(
                new NeonLetterDismantleCleanupState(
                    new NeonLetterDismantleIdentity(
                        structureInstanceId,
                        saveId,
                        networkIdentity)),
                concreteStructure.gameObject,
                refund);
            NeonLetterColorRuntime.SetColorInteractionDismantling(
                structureInstanceId,
                isDismantling: true);
            return state;
        }
        catch (Exception exception)
        {
            LogFailure(
                $"Failed to capture dismantled-neon state: {exception}");
            return null;
        }
    }

    internal static void Complete(
        NeonLetterDismantleState? state,
        bool originalSucceeded)
    {
        if (state == null)
        {
            return;
        }

        try
        {
            if (!originalSucceeded)
            {
                NeonLetterColorRuntime.SetColorInteractionDismantling(
                    state.CleanupState.Identity.StructureInstanceId,
                    isDismantling: false);
            }

            NeonLetterDismantleCleanupCoordinator.Cleanup(
                state.CleanupState,
                originalSucceeded,
                NeonLetterColorRuntime.RemoveColorInteraction,
                NeonLetterColorRuntime.RemoveSessionColor,
                NeonLetterColorRuntime.RemovePersistentColor,
                NeonLetterMultiplayerRuntime.RemoveDismantledColor,
                SOTFNeonLettersUi.OnDismantled,
                state.RemoveEmissionBindingAndSpawnRefund,
                exception => LogFailure(
                    $"Failed to complete one dismantled-neon cleanup: " +
                    exception));
        }
        catch (Exception exception)
        {
            LogFailure(
                $"Failed to complete dismantled-neon cleanup: {exception}");
        }
    }

    private static int? TryResolveOwnedSaveId(
        IScrewStructure structure,
        int structureInstanceId)
    {
        try
        {
            IScrewStructureSaveID? saveIdentity =
                structure.TryCast<IScrewStructureSaveID>();
            if (saveIdentity == null)
            {
                return null;
            }

            int candidateSaveId = saveIdentity.SaveId;
            if (!ScrewStructureManager.TryGetStructureBySaveID(
                    candidateSaveId,
                    out IScrewStructure trackedStructure) ||
                trackedStructure == null ||
                trackedStructure.Recipe?.Id != structure.Recipe?.Id)
            {
                return null;
            }

            ScrewStructure? trackedConcrete =
                trackedStructure.TryCast<ScrewStructure>();
            return trackedConcrete != null &&
                   trackedConcrete.gameObject != null &&
                   trackedConcrete.gameObject.GetInstanceID() ==
                   structureInstanceId
                ? candidateSaveId
                : null;
        }
        catch (Exception exception)
        {
            LogFailure(
                $"Failed to capture a dismantled neon letter SaveId: " +
                exception);
            return null;
        }
    }

    private static ulong? TryResolveNetworkIdentity(
        ScrewStructure concreteStructure)
    {
        try
        {
            BoltEntity entity =
                concreteStructure.gameObject.GetComponent<BoltEntity>();
            return entity != null &&
                   entity.isAttached &&
                   !entity.networkId.IsZero
                ? entity.networkId.PackedValue
                : null;
        }
        catch (Exception exception)
        {
            LogFailure(
                $"Failed to capture a dismantled neon letter network identity: " +
                exception);
            return null;
        }
    }

    private static NeonLetterRefundState? TryPrepareRefund(
        ScrewStructure concreteStructure,
        System.Collections.Generic.IReadOnlyList<int> itemIds)
    {
        if (!NeonLetterDismantleRefundPolicy.ShouldSpawnRefund(
                NetUtils.IsMultiplayer,
                NetUtils.IsServer))
        {
            return null;
        }

        try
        {
            var nativeItemIds =
                new Il2CppSystem.Collections.Generic.List<int>();
            foreach (int itemId in itemIds)
            {
                nativeItemIds.Add(itemId);
            }

            var nativeItemEnumerable =
                new Il2CppSystem.Collections.Generic.IEnumerable<int>(
                    IL2CPP.Il2CppObjectBaseToPtrNotNull(nativeItemIds));
            return new NeonLetterRefundState(
                nativeItemIds,
                nativeItemEnumerable,
                concreteStructure.transform.position,
                concreteStructure.transform.rotation);
        }
        catch (Exception exception)
        {
            LogFailure(
                $"Failed to prepare a dismantled neon letter refund: " +
                exception);
            return null;
        }
    }

    private static void LogFailure(string message)
    {
        try
        {
            RLog.Error($"[SOTFNeonLetters] {message}");
        }
        catch
        {
            // Dismantling must remain independent from mod logging failures.
        }
    }

    internal sealed class NeonLetterDismantleState
    {
        private readonly GameObject _structureRoot;
        private readonly NeonLetterRefundState? _refund;

        public NeonLetterDismantleState(
            NeonLetterDismantleCleanupState cleanupState,
            GameObject structureRoot,
            NeonLetterRefundState? refund)
        {
            CleanupState = cleanupState;
            _structureRoot = structureRoot;
            _refund = refund;
        }

        public NeonLetterDismantleCleanupState CleanupState { get; }

        public void RemoveEmissionBindingAndSpawnRefund()
        {
            try
            {
                NeonLetterColorRuntime.RemoveEmissionBinding(
                    CleanupState.Identity.StructureInstanceId,
                    _structureRoot);
            }
            finally
            {
                _refund?.Spawn();
            }
        }
    }

    internal sealed class NeonLetterRefundState
    {
        private readonly Il2CppSystem.Collections.Generic.List<int>
            _nativeItemIdStorage;
        private readonly Il2CppSystem.Collections.Generic.IEnumerable<int>
            _nativeItemIds;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;

        public NeonLetterRefundState(
            Il2CppSystem.Collections.Generic.List<int> nativeItemIdStorage,
            Il2CppSystem.Collections.Generic.IEnumerable<int> nativeItemIds,
            Vector3 position,
            Quaternion rotation)
        {
            _nativeItemIdStorage = nativeItemIdStorage;
            _nativeItemIds = nativeItemIds;
            _position = position;
            _rotation = rotation;
        }

        public void Spawn()
        {
            try
            {
                Coroutines.Start(
                    new ManagedIl2CppEnumerator(
                        ScrewStructureDestruction.SpawnItemsWorker(
                            _nativeItemIds,
                            _position,
                            _rotation)));
            }
            finally
            {
                GC.KeepAlive(_nativeItemIdStorage);
            }
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
    private static void BeforeRegisterDismantled(
        IScrewStructure screwStructureBase,
        out NeonLetterDismantleRuntime.NeonLetterDismantleState? __state)
    {
        __state = NeonLetterDismantleRuntime.Capture(screwStructureBase);
    }

    [HarmonyFinalizer]
    private static Exception? AfterRegisterDismantled(
        Exception? __exception,
        bool __runOriginal,
        NeonLetterDismantleRuntime.NeonLetterDismantleState? __state)
    {
        NeonLetterDismantleRuntime.Complete(
            __state,
            originalSucceeded: __runOriginal && __exception == null);
        return __exception;
    }
}
