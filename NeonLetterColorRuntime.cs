using System.Collections;
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
    private static bool _initialized;
    private static bool _restoreQueued;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        if (!GlobalInput.RegisterKey(KeyCode.E, OnUsePerformed))
        {
            RLog.Error(
                "[SOTFNeonLetters] The E key is already registered; " +
                "the neon color editor cannot be opened.");
        }
        SdkEvents.OnAfterSpawn.Subscribe(QueuePersistentColorRestore);
        SdkEvents.OnWorldExited.Subscribe(OnWorldExited);
        SdkEvents.AfterLoadSave.Subscribe(QueuePersistentColorRestore);
        SonsSaveTools.Register(Saveable);
        _initialized = true;
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

    internal static void ApplyEmission(
        GameObject structureRoot,
        NeonLetterSmallDefinition definition,
        NeonRgba color)
    {
        Transform[] transforms = structureRoot.GetComponentsInChildren<Transform>(true);
        var subtrees = new List<IEmissionVisualSubtree>(transforms.Length);
        foreach (Transform transform in transforms)
        {
            subtrees.Add(new UnityEmissionVisualSubtree(transform));
        }

        NeonLetterEmissionPolicy.Apply(definition, subtrees, color);
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
        if (_restoreQueued)
        {
            return;
        }

        _restoreQueued = true;
        RestorePersistentColorsNextFrame().RunCoro();
    }

    private static IEnumerator RestorePersistentColorsNextFrame()
    {
        yield return null;

        _restoreQueued = false;
        RestorePersistentColors();
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
            _restoreQueued = false;
        }
    }

    private static void RestorePersistentColors()
    {
        NeonLetterColorSaveEnvelope snapshot = PersistentColors.Save();
        int restoredCount = NeonLetterColorRestoreCoordinator.Restore(
            snapshot,
            ResolveRestoreTarget,
            exception => RLog.Error(
                $"[SOTFNeonLetters] Failed to restore one saved neon color: " +
                $"{exception}"));
        RLog.Msg(
            $"[SOTFNeonLetters] Restored {restoredCount} saved neon letter color(s).");
    }

    private static INeonLetterColorRestoreTarget ResolveRestoreTarget(int saveId)
    {
        if (!ScrewStructureManager.TryGetStructureBySaveID(
                saveId,
                out IScrewStructure structure) ||
            structure == null)
        {
            return null;
        }

        ScrewStructure concreteStructure = structure.TryCast<ScrewStructure>();
        IScrewStructureSaveID saveIdentity = structure.TryCast<IScrewStructureSaveID>();
        StructureRecipe recipe = structure.Recipe;
        if (concreteStructure == null ||
            saveIdentity == null ||
            saveIdentity.SaveId != saveId ||
            recipe == null)
        {
            return null;
        }

        NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.All.FirstOrDefault(
            candidate => candidate.RecipeId == recipe.Id);
        return definition == null
            ? null
            : new UnityColorRestoreTarget(concreteStructure, definition);
    }

    private sealed class UnityEmissionVisualSubtree : IEmissionVisualSubtree
    {
        public UnityEmissionVisualSubtree(Transform root)
        {
            Name = root.name;
            Renderers = root
                .GetComponentsInChildren<Renderer>(true)
                .Select(renderer => (IEmissionRenderer)new UnityEmissionRenderer(renderer))
                .ToArray();
        }

        public string Name { get; }
        public IReadOnlyList<IEmissionRenderer> Renderers { get; }
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

    private sealed class UnityEmissionRenderer : IEmissionRenderer
    {
        private readonly Renderer _renderer;

        public UnityEmissionRenderer(Renderer renderer)
        {
            _renderer = renderer;
            Material[] materials = renderer.sharedMaterials;
            SharedMaterials = materials
                .Select(material => material == null
                    ? null
                    : (IEmissionMaterial)new UnityEmissionMaterial(material))
                .ToArray();
        }

        public string Name => _renderer.name;
        public IReadOnlyList<IEmissionMaterial> SharedMaterials { get; }

        public IEmissionPropertyBlock ReadPropertyBlock(int materialIndex)
        {
            var propertyBlock = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(propertyBlock, materialIndex);
            return new UnityEmissionPropertyBlock(propertyBlock);
        }

        public void WritePropertyBlock(
            int materialIndex,
            IEmissionPropertyBlock propertyBlock)
        {
            var unityPropertyBlock = (UnityEmissionPropertyBlock)propertyBlock;
            _renderer.SetPropertyBlock(unityPropertyBlock.Value, materialIndex);
        }
    }

    private sealed class UnityEmissionMaterial : IEmissionMaterial
    {
        private const string EmissiveIntensityPropertyName = "_EmissiveIntensity";
        private readonly Material _material;

        public UnityEmissionMaterial(Material material)
        {
            _material = material;
        }

        public float ReadEmissiveIntensity()
        {
            return _material.GetFloat(EmissiveIntensityPropertyName);
        }
    }

    private sealed class UnityEmissionPropertyBlock : IEmissionPropertyBlock
    {
        public UnityEmissionPropertyBlock(MaterialPropertyBlock value)
        {
            Value = value;
        }

        public MaterialPropertyBlock Value { get; }

        public void SetColor(string propertyName, NeonRgba color)
        {
            Value.SetColor(
                propertyName,
                new Color(color.Red, color.Green, color.Blue, color.Alpha));
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
