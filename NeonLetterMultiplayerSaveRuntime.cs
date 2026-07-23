using Bolt;
using RedLoader;
using Sons.Crafting.Structures;
using SonsSdk;
using SonsSdk.Building;
using SonsSdk.Networking;
using UnityEngine;

namespace SOTFNeonLetters;

internal sealed class NeonLetterMultiplayerSaveRuntime
    : ICustomSaveable<NeonLetterMultiplayerSaveEnvelope>
{
    private static readonly NeonLetterMultiplayerSaveRuntime Instance = new();
    private static bool _initialized;

    private readonly NeonLetterMultiplayerRestoreCoordinator<BoltEntity>
        _restoreCoordinator = new();
    private bool _afterLoadSaveReceived;
    private bool _afterSpawnReceived;

    public string Name => "SOTFNeonLetters.MultiplayerWorld";
    public bool IncludeInPlayerSave => false;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        SdkEvents.AfterLoadSave.Subscribe(Instance.OnAfterLoadSave);
        SdkEvents.OnAfterSpawn.Subscribe(Instance.OnAfterSpawn);
        SdkEvents.OnInWorldUpdate.Subscribe(Instance.OnInWorldUpdate);
        SdkEvents.OnWorldExited.Subscribe(Instance.OnWorldExited);
        SonsSaveTools.Register(Instance);
        _initialized = true;
    }

    public NeonLetterMultiplayerSaveEnvelope Save()
    {
        bool isMultiplayer = NetUtils.IsMultiplayer;
        bool isHost = BoltNetwork.isRunning && NetUtils.IsServer;
        var envelope = new NeonLetterMultiplayerSaveEnvelope();
        if (!isMultiplayer ||
            !isHost ||
            !ScrewStructureManager.TryGetInstance(
                out ScrewStructureManager manager) ||
            manager._structures == null)
        {
            return NeonLetterMultiplayerPersistencePolicy.CreateWorldPayload(
                isMultiplayer,
                isHost,
                envelope);
        }

        Il2CppSystem.Collections.Generic.List<IScrewStructure> structures =
            manager._structures;
        for (int index = 0; index < structures.Count; index++)
        {
            try
            {
                if (TryCreateSaveEntry(
                        structures[index],
                        out NeonLetterMultiplayerSaveEntry entry))
                {
                    envelope.Entries.Add(entry);
                }
            }
            catch (Exception exception)
            {
                RLog.Error(
                    $"[SOTFNeonLetters] Failed to save multiplayer neon " +
                    $"letter entry {index}: {exception}");
            }
        }

        return NeonLetterMultiplayerPersistencePolicy.CreateWorldPayload(
            isMultiplayer,
            isHost,
            envelope);
    }

    public void Load(NeonLetterMultiplayerSaveEnvelope obj)
    {
        _restoreCoordinator.Stage(obj);
        ResolveKnownNetworkRole();
    }

    private static bool TryCreateSaveEntry(
        IScrewStructure structure,
        out NeonLetterMultiplayerSaveEntry entry)
    {
        entry = null;
        if (structure == null || structure.Recipe == null)
        {
            return false;
        }

        StructureRecipe recipe = structure.Recipe;
        NeonLetterSmallDefinition definition =
            NeonLetterSmallCatalog.All.FirstOrDefault(
                candidate => candidate.RecipeId == recipe.Id);
        ScrewStructure concreteStructure = structure.TryCast<ScrewStructure>();
        GameObject structureRoot = concreteStructure?.gameObject;
        BoltEntity entity = structureRoot?.GetComponent<BoltEntity>();
        if (definition == null ||
            concreteStructure == null ||
            structureRoot == null ||
            entity == null ||
            !entity.isAttached ||
            entity.networkId.IsZero)
        {
            return false;
        }

        int nativeSaveId = ResolveOwnedNativeSaveId(
            structure,
            concreteStructure,
            recipe.Id);
        Transform transform = structureRoot.transform;
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        entry = new NeonLetterMultiplayerSaveEntry
        {
            RecipeId = recipe.Id,
            NativeSaveId = nativeSaveId,
            Position = new NeonVector3(position.x, position.y, position.z),
            Rotation = new NeonQuaternion(
                rotation.x,
                rotation.y,
                rotation.z,
                rotation.w),
            PackedColor = NeonLetterNetworkProtocol.Pack(
                NeonLetterMultiplayerRuntime.ResolveColor(entity))
        };
        return true;
    }

    private static int ResolveOwnedNativeSaveId(
        IScrewStructure structure,
        ScrewStructure concreteStructure,
        int recipeId)
    {
        IScrewStructureSaveID saveIdentity =
            structure.TryCast<IScrewStructureSaveID>();
        if (saveIdentity == null || saveIdentity.SaveId == 0)
        {
            return 0;
        }

        int candidateSaveId = saveIdentity.SaveId;
        if (!ScrewStructureManager.TryGetStructureBySaveID(
                candidateSaveId,
                out IScrewStructure trackedStructure) ||
            trackedStructure == null ||
            trackedStructure.Recipe == null ||
            trackedStructure.Recipe.Id != recipeId)
        {
            return 0;
        }

        ScrewStructure trackedConcrete = trackedStructure.TryCast<ScrewStructure>();
        return trackedConcrete != null &&
               trackedConcrete.gameObject != null &&
               trackedConcrete.gameObject.GetInstanceID() ==
               concreteStructure.gameObject.GetInstanceID()
            ? candidateSaveId
            : 0;
    }

    private void OnAfterLoadSave()
    {
        _afterLoadSaveReceived = true;
        AdvanceRestore();
    }

    private void OnAfterSpawn()
    {
        _afterSpawnReceived = true;
        if (!BoltNetwork.isRunning)
        {
            _restoreCoordinator.SetRole(
                NeonLetterMultiplayerRestoreRole.SinglePlayer);
            return;
        }

        AdvanceRestore();
    }

    private void OnInWorldUpdate()
    {
        AdvanceRestore();
    }

    private void OnWorldExited()
    {
        _restoreCoordinator.Clear();
        _afterLoadSaveReceived = false;
        _afterSpawnReceived = false;
    }

    private void AdvanceRestore()
    {
        if (!_restoreCoordinator.HasStagedEnvelope &&
            _restoreCoordinator.PendingCount == 0)
        {
            return;
        }

        if (!BoltNetwork.isRunning)
        {
            if (_afterSpawnReceived)
            {
                _restoreCoordinator.SetRole(
                    NeonLetterMultiplayerRestoreRole.SinglePlayer);
            }
            return;
        }

        if (NetUtils.IsClient && !NetUtils.IsServer)
        {
            _restoreCoordinator.SetRole(
                NeonLetterMultiplayerRestoreRole.Client);
            return;
        }

        if (!NetUtils.IsServer)
        {
            return;
        }

        if (_restoreCoordinator.Role !=
            NeonLetterMultiplayerRestoreRole.Host)
        {
            _restoreCoordinator.SetRole(
                NeonLetterMultiplayerRestoreRole.Host);
        }
        if (!_afterLoadSaveReceived ||
            !_afterSpawnReceived ||
            !ScrewStructureManager.TryGetInstance(
                out ScrewStructureManager manager) ||
            manager._structures == null ||
            manager._isLoadingSave)
        {
            return;
        }

        _restoreCoordinator.Advance(
            Time.realtimeSinceStartupAsDouble,
            ObserveRestore,
            StartFallback,
            ApplyRestoredColor,
            LogRestoreError);
    }

    private void ResolveKnownNetworkRole()
    {
        if (!BoltNetwork.isRunning)
        {
            return;
        }

        if (NetUtils.IsServer)
        {
            _restoreCoordinator.SetRole(
                NeonLetterMultiplayerRestoreRole.Host);
            return;
        }

        if (NetUtils.IsClient)
        {
            _restoreCoordinator.SetRole(
                NeonLetterMultiplayerRestoreRole.Client);
        }
    }

    private static NeonLetterMultiplayerRestoreObservation<BoltEntity>
        ObserveRestore(
            NeonLetterMultiplayerSaveEntry entry,
            bool fallbackSpawnStarted,
            BoltEntity spawnedEntity)
    {
        StructureRecipe processedRecipe = ResolveProcessedRecipe(entry.RecipeId);
        if (processedRecipe == null)
        {
            return new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ProcessedRecipeUnavailable);
        }

        if (fallbackSpawnStarted)
        {
            return ObserveFallbackTarget(entry, spawnedEntity);
        }

        IScrewStructure nativeStructure = null;
        bool nativeIdentityResolved =
            entry.NativeSaveId != 0 &&
            ScrewStructureManager.TryGetStructureBySaveID(
                entry.NativeSaveId,
                out nativeStructure);
        if (nativeIdentityResolved)
        {
            if (nativeStructure == null || nativeStructure.Recipe == null)
            {
                return new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeRecipeUnavailable);
            }

            int nativeRecipeId = nativeStructure.Recipe.Id;
            if (nativeRecipeId != entry.RecipeId)
            {
                return new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeRecipeMismatch,
                    Target: null,
                    ResolvedRecipeId: nativeRecipeId);
            }

            ScrewStructure concreteStructure =
                nativeStructure.TryCast<ScrewStructure>();
            GameObject structureRoot = concreteStructure?.gameObject;
            BoltEntity nativeEntity =
                structureRoot?.GetComponent<BoltEntity>();
            if (concreteStructure == null ||
                structureRoot == null ||
                nativeEntity == null ||
                !nativeEntity.isAttached ||
                nativeEntity.networkId.IsZero)
            {
                return new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetUnavailable,
                    ResolvedRecipeId: nativeRecipeId);
            }

            return new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                nativeEntity,
                nativeRecipeId);
        }

        return processedRecipe._builtPrefab == null
            ? new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                NeonLetterMultiplayerRestoreObservationKind
                    .FallbackPrefabUnavailable)
            : new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ReadyToSpawnFallback);
    }

    private static NeonLetterMultiplayerRestoreObservation<BoltEntity>
        ObserveFallbackTarget(
            NeonLetterMultiplayerSaveEntry entry,
            BoltEntity spawnedEntity)
    {
        if (spawnedEntity == null ||
            !spawnedEntity.isAttached ||
            spawnedEntity.networkId.IsZero)
        {
            return new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                NeonLetterMultiplayerRestoreObservationKind
                    .FallbackTargetUnavailable);
        }

        ScrewStructure structure = spawnedEntity.GetComponent<ScrewStructure>();
        if (structure == null ||
            structure.Recipe == null)
        {
            return new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
                NeonLetterMultiplayerRestoreObservationKind
                    .FallbackTargetUnavailable);
        }

        int recipeId = structure.Recipe.Id;
        if (recipeId != entry.RecipeId)
        {
            throw new InvalidOperationException(
                $"Restored Bolt entity recipe {recipeId} does not match " +
                $"saved recipe {entry.RecipeId}.");
        }

        return new NeonLetterMultiplayerRestoreObservation<BoltEntity>(
            NeonLetterMultiplayerRestoreObservationKind.FallbackTargetReady,
            spawnedEntity,
            recipeId);
    }

    private static BoltEntity StartFallback(
        NeonLetterMultiplayerSaveEntry entry)
    {
        StructureRecipe processedRecipe = ResolveProcessedRecipe(entry.RecipeId);
        if (processedRecipe?._builtPrefab == null)
        {
            throw new InvalidOperationException(
                $"Processed neon recipe {entry.RecipeId} lost its built " +
                "Bolt prefab before fallback spawn.");
        }

        BoltEntity spawnedEntity = BoltNetwork.Instantiate(
            processedRecipe._builtPrefab,
            new Vector3(
                entry.Position.X,
                entry.Position.Y,
                entry.Position.Z),
            new Quaternion(
                entry.Rotation.X,
                entry.Rotation.Y,
                entry.Rotation.Z,
                entry.Rotation.W));
        if (spawnedEntity == null)
        {
            throw new InvalidOperationException(
                $"Bolt failed to instantiate neon recipe {entry.RecipeId}.");
        }

        return spawnedEntity;
    }

    private static bool ApplyRestoredColor(
        NeonLetterMultiplayerSaveEntry entry,
        BoltEntity entity)
    {
        if (entity == null ||
            !entity.isAttached ||
            entity.networkId.IsZero)
        {
            return false;
        }

        ScrewStructure structure = entity.GetComponent<ScrewStructure>();
        if (structure == null || structure.Recipe == null)
        {
            return false;
        }

        if (structure.Recipe.Id != entry.RecipeId)
        {
            throw new InvalidOperationException(
                $"Restored Bolt entity recipe {structure.Recipe.Id} does not " +
                $"match saved recipe {entry.RecipeId}.");
        }

        if (!NeonLetterMultiplayerPersistencePolicy.TryDecodeColor(
                NeonLetterNetworkProtocol.CurrentVersion,
                entry.PackedColor,
                out NeonRgba color))
        {
            throw new InvalidOperationException(
                $"Saved neon recipe {entry.RecipeId} has an unsupported " +
                "color protocol version.");
        }

        return NeonLetterMultiplayerRuntime.TryRestoreHostColor(
            entity,
            entry.RecipeId,
            color);
    }

    private static StructureRecipe ResolveProcessedRecipe(int recipeId)
    {
        return CustomBlueprintManager.GetProcessedRecipes().FirstOrDefault(
            candidate => candidate != null && candidate.Id == recipeId);
    }

    private static void LogRestoreError(
        NeonLetterMultiplayerSaveEntry entry,
        Exception exception)
    {
        RLog.Error(
            $"[SOTFNeonLetters] Failed to restore multiplayer neon letter " +
            $"recipe {entry.RecipeId}, native SaveId {entry.NativeSaveId}: " +
            exception);
    }
}
