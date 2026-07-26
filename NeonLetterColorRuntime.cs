using Bolt;
using RedLoader;
using Sons.Crafting.Structures;
using Sons.Gui.Input;
using SonsSdk;
using SonsSdk.Networking;
using TheForest.Utils;
using UnityEngine;

namespace SOTFNeonLetters;

public static class NeonLetterColorRuntime
{
    private const float TargetDistance = 3f;
    private static readonly NeonLetterSessionColors<int> SessionColors = new();
    private static readonly NeonLetterColorSaveState PersistentColors = new();
    private static readonly NeonLetterColorSaveable Saveable = new();
    private static readonly NeonLetterLifecycleCoordinator Lifecycle = new();
    private static readonly NeonLetterSinglePlayerRestoreLifecycle
        RestoreLifecycle = new();
    private static readonly Func<
        NeonLetterColorSaveEntry,
        NeonLetterSinglePlayerRestoreAttemptResult>
        TryRestorePersistentColorCallback = TryRestorePersistentColor;
    private static readonly Action<Exception> LogRestoreErrorCallback =
        LogRestoreError;
    private static readonly NeonLetterEmissionBindingCache<
        GameObject,
        NeonLetterSmallDefinition,
        NeonLetterEmissionBinding> EmissionBindings = new(
            IsStructureRootAlive,
            CreateEmissionBinding);
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            if (GlobalInput.RegisterKey(KeyCode.E, OnUsePerformed))
            {
                Lifecycle.CompleteStage(
                    () => GlobalInput.UnregisterKey(KeyCode.E));
            }
            else
            {
                RLog.Error(
                    "[SOTFNeonLetters] The E key is already registered; " +
                    "the neon color editor cannot be opened.");
            }

            SdkEvents.OnAfterSpawn.Subscribe(QueuePersistentColorRestore);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnAfterSpawn.Unsubscribe(
                    QueuePersistentColorRestore));

            SdkEvents.OnWorldExited.Subscribe(OnWorldExited);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnWorldExited.Unsubscribe(OnWorldExited));

            SdkEvents.AfterLoadSave.Subscribe(QueuePersistentColorRestore);
            Lifecycle.CompleteStage(
                () => SdkEvents.AfterLoadSave.Unsubscribe(
                    QueuePersistentColorRestore));

            SdkEvents.OnInWorldUpdate.Subscribe(AdvancePersistentColorRestore);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnInWorldUpdate.Unsubscribe(
                    AdvancePersistentColorRestore));

            SonsSaveTools.Register(Saveable);
            Lifecycle.CompleteStage(
                () => SonsSaveTools.Unregister(Saveable));
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
                $"[SOTFNeonLetters] Color runtime cleanup failed: {exception}"));
        SessionColors.Clear();
        PersistentColors.Clear();
        EmissionBindings.Clear();
        RestoreLifecycle.Deinitialize();
    }

    internal static NeonRgba ResolveSessionColor(int instanceId)
    {
        return SessionColors.Resolve(instanceId);
    }

    internal static void CommitSessionColor(int instanceId, NeonRgba color)
    {
        SessionColors.Commit(instanceId, color);
    }

    internal static NeonRgba? ResolvePersistentColor(int saveId, int recipeId)
    {
        return PersistentColors.Resolve(saveId, recipeId);
    }

    internal static void CommitPersistentColor(
        int saveId,
        int recipeId,
        NeonRgba color)
    {
        PersistentColors.Upsert(
            new NeonLetterColorSaveEntry(saveId, recipeId, color));
    }

    internal static void RemoveSessionColor(int instanceId)
    {
        SessionColors.Remove(instanceId);
    }

    internal static void RemovePersistentColor(int saveId)
    {
        PersistentColors.Remove(saveId);
    }

    internal static void ApplyEmission(
        GameObject structureRoot,
        NeonLetterSmallDefinition definition,
        NeonRgba color)
    {
        int structureInstanceId = structureRoot.GetInstanceID();
        NeonLetterEmissionBinding binding = EmissionBindings.GetOrCreate(
            structureInstanceId,
            structureRoot,
            definition,
            definition.RecipeId);
        binding.Apply(color);
    }

    internal static void RemoveEmissionBinding(
        int structureInstanceId,
        GameObject structureRoot)
    {
        if (ReferenceEquals(structureRoot, null))
        {
            return;
        }

        EmissionBindings.Remove(structureInstanceId, structureRoot);
    }

    private static void OnUsePerformed()
    {
        bool isPlayerControllable = GameState.IsPlayerControllable;
        if (!isPlayerControllable)
        {
            return;
        }

        try
        {
            bool hasTarget = TryResolveTargetFromView(out NeonLetterColorTarget target);
            if (!NeonLetterColorInteractionPolicy.CanOpenEditor(
                    isPlayerControllable,
                    hasTarget))
            {
                return;
            }

            SOTFNeonLettersUi.Open(target);
        }
        catch (Exception exception)
        {
            RLog.Error($"[SOTFNeonLetters] Failed to open color editor: {exception}");
        }
    }

    private static bool TryResolveTargetFromView(out NeonLetterColorTarget target)
    {
        target = null;
        Transform cameraTransform = LocalPlayer.MainCamTr;
        if (cameraTransform == null)
        {
            return false;
        }

        if (!Physics.Raycast(
                cameraTransform.position,
                cameraTransform.forward,
                out RaycastHit hit,
                TargetDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        Collider hitCollider = hit.collider;
        ScrewStructure structure =
            hitCollider == null
                ? null
                : hitCollider.GetComponentInParent<ScrewStructure>();
        int recipeId = structure?.Recipe?.Id ?? int.MinValue;
        if (!NeonLetterColorInteractionPolicy.IsEditable(
                hasCompletedStructure: structure != null,
                recipeId))
        {
            return false;
        }

        NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.All.FirstOrDefault(
            candidate => candidate.RecipeId == recipeId);
        if (definition == null)
        {
            return false;
        }

        NeonLetterColorTargetMode targetMode = NetUtils.IsMultiplayer
            ? NeonLetterColorTargetMode.Multiplayer
            : NeonLetterColorTargetMode.SinglePlayer;
        BoltEntity networkEntity = null;
        if (targetMode == NeonLetterColorTargetMode.Multiplayer)
        {
            NeonLetterColorCommitRoute route =
                NeonLetterColorCommitRoutingPolicy.Resolve(
                    targetMode,
                    NetUtils.IsServer,
                    NetUtils.IsClient);
            if (!BoltNetwork.isRunning ||
                (route != NeonLetterColorCommitRoute.MultiplayerHost &&
                 route != NeonLetterColorCommitRoute.MultiplayerClient))
            {
                return false;
            }

            networkEntity = structure.gameObject.GetComponent<BoltEntity>();
            if (networkEntity == null ||
                !networkEntity.isAttached ||
                networkEntity.networkId.IsZero)
            {
                return false;
            }
        }

        target = new NeonLetterColorTarget(
            structure,
            definition,
            targetMode,
            networkEntity);
        return true;
    }

    private static void QueuePersistentColorRestore()
    {
        RestoreLifecycle.SetSinglePlayerRole(IsSinglePlayerRole());
        RestoreLifecycle.Stage(
            PersistentColors.Save(),
            Time.realtimeSinceStartupAsDouble);
    }

    private static void AdvancePersistentColorRestore()
    {
        RestoreLifecycle.SetSinglePlayerRole(IsSinglePlayerRole());
        RestoreLifecycle.Advance(
            Time.realtimeSinceStartupAsDouble,
            TryRestorePersistentColorCallback);
    }

    private static void OnWorldExited()
    {
        try
        {
            SOTFNeonLettersUi.OnWorldExited();
        }
        catch (Exception exception)
        {
            RLog.Error($"[SOTFNeonLetters] Failed to close color editor on world exit: {exception}");
        }
        finally
        {
            SessionColors.Clear();
            PersistentColors.Clear();
            EmissionBindings.Clear();
            RestoreLifecycle.OnWorldExited();
        }
    }

    private static NeonLetterSinglePlayerRestoreAttemptResult
        TryRestorePersistentColor(NeonLetterColorSaveEntry entry)
    {
        try
        {
            NeonLetterSinglePlayerRestoreTargetObservation observation =
                ObserveRestoreTarget(entry);
            return NeonLetterSinglePlayerRestoreAttemptPolicy.TryApply(
                entry,
                observation,
                LogRestoreErrorCallback);
        }
        catch (Exception exception)
        {
            LogRestoreError(exception);
            return NeonLetterSinglePlayerRestoreAttemptResult.Terminal;
        }
    }

    private static NeonLetterSinglePlayerRestoreTargetObservation
        ObserveRestoreTarget(NeonLetterColorSaveEntry entry)
    {
        if (!ScrewStructureManager.TryGetInstance(
                out ScrewStructureManager manager) ||
            manager._structures == null ||
            manager._isLoadingSave)
        {
            return new NeonLetterSinglePlayerRestoreTargetObservation(
                NeonLetterSinglePlayerRestoreTargetObservationKind
                    .ManagerUnavailable);
        }

        if (!ScrewStructureManager.TryGetStructureBySaveID(
                entry.SaveId,
                out IScrewStructure structure) ||
            structure == null)
        {
            return new NeonLetterSinglePlayerRestoreTargetObservation(
                NeonLetterSinglePlayerRestoreTargetObservationKind
                    .TargetUnavailable);
        }

        ScrewStructure concreteStructure = structure.TryCast<ScrewStructure>();
        IScrewStructureSaveID saveIdentity =
            structure.TryCast<IScrewStructureSaveID>();
        StructureRecipe recipe = structure.Recipe;
        if (concreteStructure == null ||
            saveIdentity == null ||
            saveIdentity.SaveId != entry.SaveId ||
            recipe == null)
        {
            return new NeonLetterSinglePlayerRestoreTargetObservation(
                NeonLetterSinglePlayerRestoreTargetObservationKind.Resolved,
                Target: null,
                recipe?.Id);
        }

        NeonLetterSmallDefinition definition =
            ResolveRestoreDefinition(recipe.Id);
        INeonLetterColorRestoreTarget target = definition == null
            ? null
            : new UnityColorRestoreTarget(concreteStructure, definition);
        return new NeonLetterSinglePlayerRestoreTargetObservation(
            NeonLetterSinglePlayerRestoreTargetObservationKind.Resolved,
            target,
            recipe.Id);
    }

    private static bool IsSinglePlayerRole()
    {
        return !NetUtils.IsMultiplayer && !BoltNetwork.isRunning;
    }

    private static NeonLetterSmallDefinition ResolveRestoreDefinition(
        int recipeId)
    {
        IReadOnlyList<NeonLetterSmallDefinition> definitions =
            NeonLetterSmallCatalog.All;
        for (int index = 0; index < definitions.Count; index++)
        {
            NeonLetterSmallDefinition definition = definitions[index];
            if (definition.RecipeId == recipeId)
            {
                return definition;
            }
        }

        return null;
    }

    private static void LogRestoreError(Exception exception)
    {
        try
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to restore one saved neon color: " +
                $"{exception}");
        }
        catch
        {
            // Logging cannot make a terminal restore safe to retry.
        }
    }

    private static bool IsStructureRootAlive(GameObject structureRoot)
    {
        return structureRoot != null;
    }

    private static NeonLetterEmissionBinding CreateEmissionBinding(
        GameObject structureRoot,
        NeonLetterSmallDefinition definition)
    {
        Transform[] transforms =
            structureRoot.GetComponentsInChildren<Transform>(true);
        Transform selectedSubtree = null;
        int matchingSubtreeCount = 0;
        foreach (Transform transform in transforms)
        {
            if (transform != null &&
                string.Equals(
                    transform.name,
                    definition.ColliderVisualChildName,
                    StringComparison.Ordinal))
            {
                selectedSubtree = transform;
                matchingSubtreeCount++;
            }
        }

        if (matchingSubtreeCount != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one visual subtree named " +
                $"'{definition.ColliderVisualChildName}', but found " +
                $"{matchingSubtreeCount}.");
        }

        Renderer[] renderers =
            selectedSubtree.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            throw new InvalidOperationException(
                $"Visual subtree '{selectedSubtree.name}' has no renderers.");
        }

        var slots = new List<IEmissionBindingSlot>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                throw new InvalidOperationException(
                    $"Visual subtree '{selectedSubtree.name}' has a null renderer.");
            }

            // Registered Neon Letter prefabs keep renderer slot topology and
            // material assignments immutable until dismantle, world exit, or shutdown.
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' has no shared materials.");
            }

            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has a null shared material " +
                        $"at slot {materialIndex}.");
                }

                slots.Add(
                    new UnityEmissionSlot(
                        renderer,
                        renderer.name,
                        material,
                        materialIndex));
            }
        }

        return new NeonLetterEmissionBinding(slots);
    }

    private sealed class UnityColorRestoreTarget : INeonLetterColorRestoreTarget
    {
        private readonly ScrewStructure _structure;
        private readonly NeonLetterSmallDefinition _definition;

        public UnityColorRestoreTarget(
            ScrewStructure structure,
            NeonLetterSmallDefinition definition)
        {
            _structure = structure;
            _definition = definition;
        }

        public int RecipeId => _definition.RecipeId;

        public void Apply(NeonRgba color)
        {
            ApplyEmission(_structure.gameObject, _definition, color);
            SessionColors.Commit(_structure.gameObject.GetInstanceID(), color);
        }
    }

    private sealed class NeonLetterColorSaveable
        : ICustomSaveable<NeonLetterColorSaveEnvelope>
    {
        public string Name => "SOTFNeonLetters.Colors";
        public bool IncludeInPlayerSave => false;

        public NeonLetterColorSaveEnvelope Save()
        {
            return PersistentColors.Save();
        }

        public void Load(NeonLetterColorSaveEnvelope obj)
        {
            PersistentColors.Load(obj);
        }
    }

    private sealed class UnityEmissionSlot : IEmissionBindingSlot
    {
        private static readonly int EmissiveColorPropertyId =
            Shader.PropertyToID(
                NeonLetterEmissionPolicy.EmissiveColorPropertyName);
        private static readonly int EmissiveIntensityPropertyId =
            Shader.PropertyToID("_EmissiveIntensity");

        private readonly Renderer _renderer;
        private readonly string _rendererName;
        private readonly Material _material;
        private readonly int _materialIndex;
        private readonly UnityEmissionPropertyBlock _propertyBlock =
            new(new MaterialPropertyBlock(), EmissiveColorPropertyId);

        public UnityEmissionSlot(
            Renderer renderer,
            string rendererName,
            Material material,
            int materialIndex)
        {
            _renderer = renderer;
            _rendererName = rendererName;
            _material = material;
            _materialIndex = materialIndex;
        }

        public string RendererName => _rendererName;
        public int MaterialIndex => _materialIndex;
        public bool IsRendererAlive => _renderer != null;
        public bool IsMaterialAlive => _material != null;

        public float ReadEmissiveIntensity()
        {
            return _material.GetFloat(EmissiveIntensityPropertyId);
        }

        public IEmissionPropertyBlock ReadPropertyBlock()
        {
            _renderer.GetPropertyBlock(
                _propertyBlock.Value,
                _materialIndex);
            return _propertyBlock;
        }

        public void WritePropertyBlock(
            IEmissionPropertyBlock propertyBlock)
        {
            var unityPropertyBlock =
                (UnityEmissionPropertyBlock)propertyBlock;
            _renderer.SetPropertyBlock(
                unityPropertyBlock.Value,
                _materialIndex);
        }
    }

    private sealed class UnityEmissionPropertyBlock : IEmissionPropertyBlock
    {
        private readonly int _emissiveColorPropertyId;

        public UnityEmissionPropertyBlock(
            MaterialPropertyBlock value,
            int emissiveColorPropertyId)
        {
            Value = value;
            _emissiveColorPropertyId = emissiveColorPropertyId;
        }

        public MaterialPropertyBlock Value { get; }

        public void SetColor(string propertyName, NeonRgba color)
        {
            Value.SetColor(
                _emissiveColorPropertyId,
                new Color(
                    color.Red,
                    color.Green,
                    color.Blue,
                    color.Alpha));
        }
    }
}

public sealed class NeonLetterColorTarget
{
    private readonly ScrewStructure _structure;
    private readonly NeonLetterSmallDefinition _definition;
    private readonly NeonLetterColorTargetMode _targetMode;
    private readonly BoltEntity _networkEntity;
    private readonly int _structureInstanceId;
    private readonly int _recipeId;
    private int _saveId;
    private bool _hasSaveId;

    public NeonLetterColorTarget(
        ScrewStructure structure,
        NeonLetterSmallDefinition definition,
        NeonLetterColorTargetMode targetMode,
        BoltEntity networkEntity)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(definition);

        _structure = structure;
        _definition = definition;
        _targetMode = targetMode;
        _networkEntity = networkEntity;
        _structureInstanceId = structure.gameObject.GetInstanceID();
        _recipeId = definition.RecipeId;
        _hasSaveId =
            targetMode == NeonLetterColorTargetMode.SinglePlayer &&
            TryResolveOwnedSaveId(structure, out _saveId);
    }

    public NeonRgba CurrentColor
    {
        get
        {
            EnsureAlive();
            if (_targetMode == NeonLetterColorTargetMode.Multiplayer)
            {
                return NeonLetterMultiplayerRuntime.ResolveColor(_networkEntity);
            }

            RefreshSaveId();
            if (_hasSaveId)
            {
                NeonRgba? persisted = NeonLetterColorRuntime.ResolvePersistentColor(
                    _saveId,
                    _recipeId);
                if (persisted.HasValue)
                {
                    return persisted.Value;
                }
            }

            return NeonLetterColorRuntime.ResolveSessionColor(_structureInstanceId);
        }
    }

    internal int StructureInstanceId => _structureInstanceId;

    public void PreviewColor(NeonRgba color)
    {
        EnsureAlive();
        NeonLetterColorRuntime.ApplyEmission(
            _structure.gameObject,
            _definition,
            color);
    }

    public void CommitColor(NeonRgba color)
    {
        EnsureAlive();
        NeonLetterColorRoutedCommit commit =
            NeonLetterColorCommitRoutingCoordinator.TryCommit(
                _targetMode,
                NetUtils.IsServer,
                NetUtils.IsClient,
                color,
                CommitSinglePlayerColor,
                requestedColor =>
                    _networkEntity != null &&
                    NeonLetterMultiplayerRuntime.RequestColor(
                        _networkEntity,
                        requestedColor));
        if (!commit.Succeeded)
        {
            throw new InvalidOperationException(
                $"The neon color commit route '{commit.Route}' is unavailable.");
        }
    }

    private void CommitSinglePlayerColor(NeonRgba color)
    {
        NeonLetterColorCommitCoordinator.Commit(
            color,
            PreviewColor,
            committedColor =>
            {
                NeonLetterColorRuntime.CommitSessionColor(
                    _structureInstanceId,
                    committedColor);
                RefreshSaveId();
                if (_hasSaveId)
                {
                    NeonLetterColorRuntime.CommitPersistentColor(
                        _saveId,
                        _recipeId,
                        committedColor);
                }
            });
    }

    private void RefreshSaveId()
    {
        if (!_hasSaveId)
        {
            _hasSaveId = TryResolveOwnedSaveId(_structure, out _saveId);
        }
    }

    private static bool TryResolveOwnedSaveId(
        ScrewStructure structure,
        out int saveId)
    {
        saveId = default;
        IScrewStructureSaveID saveIdentity = structure.TryCast<IScrewStructureSaveID>();
        if (saveIdentity == null)
        {
            return false;
        }

        int candidateSaveId = saveIdentity.SaveId;
        bool resolvesCurrentStructure =
            ScrewStructureManager.TryGetStructureBySaveID(
                candidateSaveId,
                out IScrewStructure trackedStructure) &&
            trackedStructure != null &&
            trackedStructure.TryCast<ScrewStructure>() is ScrewStructure trackedConcrete &&
            trackedConcrete.gameObject.GetInstanceID() ==
            structure.gameObject.GetInstanceID();
        if (!NeonLetterColorPersistenceEligibility.CanPersist(
                hasSaveIdentity: true,
                isTrackedCurrentStructure: resolvesCurrentStructure))
        {
            return false;
        }

        saveId = candidateSaveId;
        return true;
    }

    private void EnsureAlive()
    {
        if (_structure == null || _structure.gameObject == null)
        {
            throw new InvalidOperationException(
                "The color editor target is no longer a live neon letter structure.");
        }
    }
}
