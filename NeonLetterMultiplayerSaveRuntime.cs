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
    private const int MaxRestoreItemsPerTick =
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>
            .MaxItemsPerUpdate;
    private const int MaxFallbackSpawnsPerTick =
        NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>
            .MaxFallbackSpawnsPerUpdate;
    private static readonly NeonLetterMultiplayerSaveRuntime Instance = new();
    private static readonly NeonLetterLifecycleCoordinator Lifecycle = new();
    private static readonly Action<
        NeonLetterMultiplayerSaveEntry,
        Exception> LogRestoreErrorCallback = LogRestoreError;
    private static bool _initialized;

    private readonly NeonLetterMultiplayerRestoreCoordinator<RestoreTarget>
        _restoreCoordinator = new();
    private readonly NeonLetterRestoreReadinessScheduler<
        MultiplayerRestoreProgress> _restoreReadiness = new();
    private readonly NeonLetterMonotonicSequence _restoreSignals = new();
    private readonly NeonLetterMultiplayerRestoreLoadQueue
        _queuedLoads = new();
    private readonly NeonLetterRestoreWorkOwnership _restoreWork = new();
    private readonly Dictionary<int, RestoreTarget> _nativeTargets = new();
    private readonly Dictionary<int, StructureRecipe> _processedRecipes = new();
    private readonly Func<
        NeonLetterMultiplayerSaveEntry,
        bool,
        RestoreTarget,
        NeonLetterMultiplayerRestoreObservation<RestoreTarget>> _observeRestore;
    private readonly Func<NeonLetterMultiplayerSaveEntry, RestoreTarget>
        _startFallback;
    private readonly Func<
        NeonLetterMultiplayerSaveEntry,
        RestoreTarget,
        bool> _applyRestored;
    private volatile bool _afterLoadSaveReceived;
    private volatile bool _afterSpawnReceived;
    private NeonLetterRestoreUpdateOwnership? _activeUpdateOwnership;
    private bool _coordinatorResetPerformed;
    private long _restoreUpdateTick;

    private NeonLetterMultiplayerSaveRuntime()
    {
        _observeRestore = ObserveRestoreWithCancellation;
        _startFallback = StartFallbackWithCancellation;
        _applyRestored = ApplyRestoredColorWithCancellation;
    }

    public string Name => "SOTFNeonLetters.MultiplayerWorld";
    public bool IncludeInPlayerSave => false;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            Instance._queuedLoads.Resume();
            SdkEvents.AfterLoadSave.Subscribe(Instance.OnAfterLoadSave);
            Lifecycle.CompleteStage(
                () => SdkEvents.AfterLoadSave.Unsubscribe(
                    Instance.OnAfterLoadSave));

            SdkEvents.OnAfterSpawn.Subscribe(Instance.OnAfterSpawn);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnAfterSpawn.Unsubscribe(
                    Instance.OnAfterSpawn));

            SdkEvents.OnInWorldUpdate.Subscribe(Instance.OnInWorldUpdate);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnInWorldUpdate.Unsubscribe(
                    Instance.OnInWorldUpdate));

            SdkEvents.OnWorldExited.Subscribe(Instance.OnWorldExited);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnWorldExited.Unsubscribe(
                    Instance.OnWorldExited));

            SonsSaveTools.Register(Instance);
            Lifecycle.CompleteStage(
                () => SonsSaveTools.Unregister(Instance));
            _initialized = true;
        }
        catch
        {
            Deinitialize();
            throw;
        }
    }

    internal static void Deinitialize()
    {
        _initialized = false;
        Lifecycle.Cleanup(
            exception => RLog.Error(
                $"[SOTFNeonLetters] Multiplayer persistence cleanup failed: " +
                exception));
        Instance.OnDeinitialized();
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
        if (_queuedLoads.Enqueue(obj))
        {
            SignalRestoreProgress();
        }
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
        SignalRestoreProgress();
    }

    private void OnAfterSpawn()
    {
        _afterSpawnReceived = true;
        SignalRestoreProgress();
    }

    private void OnInWorldUpdate()
    {
        if (!_restoreWork.TryBeginUpdate(
                out NeonLetterRestoreUpdateOwnership ownership))
        {
            return;
        }

        _activeUpdateOwnership = ownership;
        try
        {
            DrainQueuedLoad();
            if (TryCancelRequestedRestoreWork())
            {
                return;
            }

            AdvanceRestore();
            TryCancelRequestedRestoreWork();
        }
        finally
        {
            bool coordinatorResetPerformed = _coordinatorResetPerformed;
            _coordinatorResetPerformed = false;
            _activeUpdateOwnership = null;
            if (_restoreWork.CompleteUpdate(
                    ownership,
                    out NeonLetterRestoreResetOwnership resetOwnership))
            {
                PerformReset(
                    resetOwnership,
                    coordinatorResetPerformed);
            }
        }
    }

    private void OnWorldExited()
    {
        ResetRuntimeState(
            rollbackOwnedFallbacks: false,
            resumeLoads: true);
    }

    private void OnDeinitialized()
    {
        ResetRuntimeState(
            rollbackOwnedFallbacks: true,
            resumeLoads: false);
    }

    private void ResetRuntimeState(
        bool rollbackOwnedFallbacks,
        bool resumeLoads)
    {
        _queuedLoads.SuspendAndClear();
        if (_restoreWork.RequestReset(
                rollbackOwnedFallbacks,
                resumeLoads,
                out NeonLetterRestoreResetOwnership ownership))
        {
            PerformReset(ownership);
        }
    }

    private void PerformReset(
        NeonLetterRestoreResetOwnership ownership,
        bool coordinatorResetPerformed = false)
    {
        NeonLetterRestoreResetRequest request =
            _restoreWork.GetResetRequest(ownership);
        try
        {
            if (!coordinatorResetPerformed &&
                request.RollbackOwnedFallbacks)
            {
                _restoreCoordinator.Clear();
            }
            else if (!coordinatorResetPerformed)
            {
                _restoreCoordinator.AbandonWithoutWorldMutation();
            }
        }
        finally
        {
            _nativeTargets.Clear();
            _processedRecipes.Clear();
            _afterLoadSaveReceived = false;
            _afterSpawnReceived = false;
            _restoreReadiness.Reset();
            _restoreUpdateTick = 0;
            _queuedLoads.SuspendAndClear();
            NeonLetterRestoreResetCompletion completion =
                _restoreWork.CompleteReset(ownership);
            if (completion.ResumeLoads && _initialized)
            {
                _queuedLoads.Resume();
            }
        }
    }

    private bool TryCancelRequestedRestoreWork()
    {
        if (_coordinatorResetPerformed)
        {
            return true;
        }

        if (!_activeUpdateOwnership.HasValue ||
            !_restoreWork.TryGetPendingResetRequest(
                _activeUpdateOwnership.Value,
                out NeonLetterRestoreResetRequest request))
        {
            return false;
        }

        _coordinatorResetPerformed = true;
        if (request.RollbackOwnedFallbacks)
        {
            _restoreCoordinator.Clear();
        }
        else
        {
            _restoreCoordinator.AbandonWithoutWorldMutation();
        }

        return true;
    }

    private void DrainQueuedLoad()
    {
        if (!_queuedLoads.TryDequeue(
                out NeonLetterMultiplayerRestoreSnapshot snapshot))
        {
            return;
        }

        if (TryCancelRequestedRestoreWork())
        {
            return;
        }

        _restoreCoordinator.StageSnapshot(snapshot);
        ResolveKnownNetworkRole();
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
                if (_restoreCoordinator.Role !=
                    NeonLetterMultiplayerRestoreRole.SinglePlayer)
                {
                    _restoreCoordinator.SetRole(
                        NeonLetterMultiplayerRestoreRole.SinglePlayer);
                }
            }
            return;
        }

        if (NetUtils.IsClient && !NetUtils.IsServer)
        {
            if (_restoreCoordinator.Role !=
                NeonLetterMultiplayerRestoreRole.Client)
            {
                _restoreCoordinator.SetRole(
                    NeonLetterMultiplayerRestoreRole.Client);
            }
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
        if (_restoreCoordinator.PendingCount == 0 ||
            !_afterLoadSaveReceived ||
            !_afterSpawnReceived)
        {
            return;
        }

        bool managerAvailable =
            ScrewStructureManager.TryGetInstance(
                out ScrewStructureManager manager) &&
            manager._structures != null;
        bool managerLoading =
            managerAvailable && manager._isLoadingSave;
        MultiplayerRestoreProgress progress = CaptureRestoreProgress(
            managerAvailable,
            managerLoading,
            managerAvailable ? manager._structures.Count : -1);
        ulong currentToken = _restoreReadiness.CurrentToken;
        bool waveActive =
            currentToken != 0 &&
            _restoreCoordinator.HasWorkForToken(currentToken);
        long updateTick = NextRestoreUpdateTick();
        bool tokenChanged = _restoreReadiness.TryGetDueToken(
            progress,
            updateTick,
            waveActive,
            out ulong readinessToken);
        if (!managerAvailable || managerLoading)
        {
            return;
        }

        if (tokenChanged)
        {
            int processedRecipeCount = _processedRecipes.Count;
            RefreshProcessedRecipes();
            if (_processedRecipes.Count != processedRecipeCount)
            {
                progress = CaptureRestoreProgress(
                    managerAvailable,
                    managerLoading,
                    manager._structures.Count);
                _restoreReadiness.TryGetDueToken(
                    progress,
                    updateTick,
                    waveActive: false,
                    out readinessToken);
            }
        }

        if (!tokenChanged &&
            !_restoreCoordinator.HasWorkForToken(readinessToken))
        {
            return;
        }

        _restoreCoordinator.AdvanceForReadinessToken(
            readinessToken: readinessToken,
            maxItems: MaxRestoreItemsPerTick,
            maxFallbackSpawns: MaxFallbackSpawnsPerTick,
            observe: _observeRestore,
            startFallback: _startFallback,
            applyRestored: _applyRestored,
            onEntryError: LogRestoreErrorCallback);
    }

    private MultiplayerRestoreProgress CaptureRestoreProgress(
        bool managerAvailable,
        bool managerLoading,
        int structureCount)
    {
        ulong signalGeneration =
            _restoreWork.ReadSignal(_restoreSignals);
        return new MultiplayerRestoreProgress(
            signalGeneration,
            _restoreCoordinator.Role,
            managerAvailable,
            managerLoading,
            structureCount,
            _processedRecipes.Count,
            _restoreCoordinator.StartedFallbackCount);
    }

    private long NextRestoreUpdateTick()
    {
        long updateTick = _restoreUpdateTick;
        if (_restoreUpdateTick < long.MaxValue)
        {
            _restoreUpdateTick++;
        }

        return updateTick;
    }

    private void SignalRestoreProgress()
    {
        _restoreWork.RecordSignal(_restoreSignals);
    }

    private readonly record struct MultiplayerRestoreProgress(
        ulong SignalGeneration,
        NeonLetterMultiplayerRestoreRole Role,
        bool ManagerAvailable,
        bool IsLoadingSave,
        int StructureCount,
        int ProcessedRecipeCount,
        int StartedFallbackCount);

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

    private NeonLetterMultiplayerRestoreObservation<RestoreTarget>
        ObserveRestoreWithCancellation(
            NeonLetterMultiplayerSaveEntry entry,
            bool fallbackSpawnStarted,
            RestoreTarget spawnedTarget)
    {
        if (TryCancelRequestedRestoreWork())
        {
            return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ProcessedRecipeUnavailable);
        }

        return ObserveRestore(
            entry,
            fallbackSpawnStarted,
            spawnedTarget);
    }

    private NeonLetterMultiplayerRestoreObservation<RestoreTarget>
        ObserveRestore(
            NeonLetterMultiplayerSaveEntry entry,
            bool fallbackSpawnStarted,
            RestoreTarget spawnedTarget)
    {
        StructureRecipe processedRecipe = ResolveProcessedRecipe(entry.RecipeId);
        if (processedRecipe == null)
        {
            return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ProcessedRecipeUnavailable);
        }

        if (fallbackSpawnStarted)
        {
            return ObserveFallbackTarget(entry, spawnedTarget);
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
                return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeRecipeUnavailable);
            }

            int nativeRecipeId = nativeStructure.Recipe.Id;
            if (nativeRecipeId != entry.RecipeId)
            {
                return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
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
                return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetUnavailable,
                    ResolvedRecipeId: nativeRecipeId);
            }

            return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                ResolveNativeTarget(nativeEntity),
                nativeRecipeId);
        }

        return processedRecipe._builtPrefab == null
            ? new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                NeonLetterMultiplayerRestoreObservationKind
                    .FallbackPrefabUnavailable)
            : new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ReadyToSpawnFallback);
    }

    private static NeonLetterMultiplayerRestoreObservation<RestoreTarget>
        ObserveFallbackTarget(
            NeonLetterMultiplayerSaveEntry entry,
            RestoreTarget spawnedTarget)
    {
        BoltEntity spawnedEntity = spawnedTarget?.Entity;
        if (spawnedEntity == null ||
            !spawnedEntity.isAttached ||
            spawnedEntity.networkId.IsZero)
        {
            return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
                NeonLetterMultiplayerRestoreObservationKind
                    .FallbackTargetUnavailable);
        }

        ScrewStructure structure = spawnedEntity.GetComponent<ScrewStructure>();
        if (structure == null ||
            structure.Recipe == null)
        {
            return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
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

        return new NeonLetterMultiplayerRestoreObservation<RestoreTarget>(
            NeonLetterMultiplayerRestoreObservationKind.FallbackTargetReady,
            spawnedTarget,
            recipeId);
    }

    private RestoreTarget StartFallbackWithCancellation(
        NeonLetterMultiplayerSaveEntry entry)
    {
        if (TryCancelRequestedRestoreWork())
        {
            throw new OperationCanceledException(
                "Multiplayer restore was cancelled before fallback spawn.");
        }

        return StartFallback(entry);
    }

    private RestoreTarget StartFallback(
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

        return new RestoreTarget(
            spawnedEntity,
            RollbackFallback,
            DestroyLocalFallback);
    }

    private bool ApplyRestoredColorWithCancellation(
        NeonLetterMultiplayerSaveEntry entry,
        RestoreTarget target)
    {
        if (TryCancelRequestedRestoreWork())
        {
            return false;
        }

        bool applied = ApplyRestoredColor(entry, target);
        return TryCancelRequestedRestoreWork()
            ? false
            : applied;
    }

    private static bool ApplyRestoredColor(
        NeonLetterMultiplayerSaveEntry entry,
        RestoreTarget target)
    {
        BoltEntity entity = target?.Entity;
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

    private static void RollbackFallback(BoltEntity entity)
    {
        BoltNetwork.Destroy(entity);
    }

    private static void DestroyLocalFallback(BoltEntity entity)
    {
        if (entity != null)
        {
            UnityEngine.Object.Destroy(entity.gameObject);
        }
    }

    private RestoreTarget ResolveNativeTarget(BoltEntity entity)
    {
        int instanceId = entity.GetInstanceID();
        if (_nativeTargets.TryGetValue(
                instanceId,
                out RestoreTarget target) &&
            target.Matches(entity))
        {
            return target;
        }

        target = new RestoreTarget(entity);
        _nativeTargets[instanceId] = target;
        return target;
    }

    private sealed class RestoreTarget : IDisposable
    {
        private readonly NeonLetterFallbackRollbackAdapter<BoltEntity>
            _rollback;

        public RestoreTarget(
            BoltEntity entity,
            Action<BoltEntity> destroyOverNetwork = null,
            Action<BoltEntity> destroyLocally = null)
        {
            Entity = entity;
            if (destroyOverNetwork != null)
            {
                _rollback =
                    new NeonLetterFallbackRollbackAdapter<BoltEntity>(
                        entity,
                        target => target != null,
                        target => target.isAttached,
                        target => !target.networkId.IsZero,
                        destroyOverNetwork,
                        destroyLocally ??
                        throw new ArgumentNullException(
                            nameof(destroyLocally)));
            }
        }

        public BoltEntity Entity { get; }

        public void Dispose()
        {
            _rollback?.Dispose();
        }

        public bool Matches(BoltEntity entity)
        {
            return Entity != null &&
                   entity != null &&
                   Entity.GetInstanceID() == entity.GetInstanceID();
        }
    }

    private void RefreshProcessedRecipes()
    {
        _processedRecipes.Clear();
        foreach (StructureRecipe recipe in
                 CustomBlueprintManager.GetProcessedRecipes())
        {
            if (recipe != null && !_processedRecipes.ContainsKey(recipe.Id))
            {
                _processedRecipes.Add(recipe.Id, recipe);
            }
        }
    }

    private StructureRecipe ResolveProcessedRecipe(int recipeId)
    {
        return _processedRecipes.TryGetValue(
            recipeId,
            out StructureRecipe recipe)
                ? recipe
                : null;
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
