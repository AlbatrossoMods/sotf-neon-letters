using Bolt;
using Il2CppInterop.Runtime;
using RedLoader;
using Sons.Crafting.Structures;
using Sons.Gameplay;
using Sons.Gui.Input;
using SonsSdk;
using SonsSdk.Networking;
using UnityEngine;
using Il2CppAction = Il2CppSystem.Action;

namespace SOTFNeonLetters;

public static partial class NeonLetterColorRuntime
{
    private const string InteractionHolderName =
        "SOTFNeonLetters.ColorInteractionHolder";
    private const string InteractionProxyName =
        "SOTFNeonLetters.ColorInteractionProxy";
    private const string InteractionPromptName =
        "SOTFNeonLetters.ColorInteractionPrompt";
    private static readonly NeonLetterColorInteractionLeaseRegistry<
        ColorInteractionLease> InteractionLeases = new();
    private static readonly NeonLetterColorInteractionCreationFailures<
        ColorInteractionCreationFailureFingerprint>
        InteractionCreationFailures = new();
    private static readonly ColorInteractionCreationFailureFingerprint
        MissingRootColliderFailure = new(
            typeof(BoxCollider),
            "Missing completed-structure root BoxCollider.");

    private static ColorInteractionLease CreateColorInteractionLease(
        ScrewStructure structure,
        GameObject structureRoot,
        int recipeId,
        NeonLetterColorInteractionGeometry geometry,
        GameObject promptTemplate)
    {
        GameObject interactionHolder =
            new(InteractionHolderName);
        interactionHolder.SetActive(false);
        Transform holderTransform = interactionHolder.transform;
        holderTransform.SetParent(structureRoot.transform, false);
        holderTransform.localPosition = Vector3.zero;
        holderTransform.localRotation = Quaternion.identity;
        holderTransform.localScale = Vector3.one;

        ColorInteractionLease lease = null;
        try
        {
            GenericInteraction interaction =
                SonsInteractionTools.CreateInteraction<GenericInteraction>(
                    holderTransform,
                    geometry.Radius);
            if (interaction == null ||
                interaction.gameObject == null ||
                interaction.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "SonsInteractionTools returned an interaction that was " +
                    "enabled before native Use configuration completed.");
            }

            GameObject interactionProxy = interaction.gameObject;
            interactionProxy.name = InteractionProxyName;
            Transform proxyTransform = interactionProxy.transform;
            proxyTransform.localPosition = new Vector3(
                geometry.CenterX,
                geometry.CenterY,
                geometry.CenterZ);
            proxyTransform.localRotation = Quaternion.identity;
            proxyTransform.localScale = Vector3.one;

            SphereCollider proxyCollider =
                interactionProxy.GetComponent<SphereCollider>();
            if (proxyCollider == null)
            {
                throw new InvalidOperationException(
                    "SonsInteractionTools did not create its required " +
                    "PickUp-layer SphereCollider proxy.");
            }

            proxyCollider.center = Vector3.zero;
            proxyCollider.radius = geometry.Radius;
            proxyCollider.isTrigger = true;

            GameObject ownedPrompt =
                UnityEngine.Object.Instantiate<GameObject>(
                    promptTemplate,
                    proxyTransform,
                    false);
            ownedPrompt.name = InteractionPromptName;
            ownedPrompt.SetActive(false);
            DynamicInputIcon inputIcon =
                ownedPrompt.GetComponentInChildren<DynamicInputIcon>(true);
            if (inputIcon == null)
            {
                throw new InvalidOperationException(
                    "The cloned native prompt has no DynamicInputIcon.");
            }

            inputIcon.SetActionId(NativeUseAction);
            interaction._actionId = NativeUseAction;
            interaction._usePlayerNetworkInteraction = false;
            interaction._interactGui = ownedPrompt;
            interaction.SetInteractionBlocked(false);

            lease = new ColorInteractionLease(
                structureRoot.GetInstanceID(),
                recipeId,
                structure,
                structureRoot,
                interactionHolder,
                interactionProxy,
                interaction,
                ownedPrompt);
            lease.RegisterCallback();

            bool geometryConfigured =
                proxyTransform.localPosition ==
                new Vector3(
                    geometry.CenterX,
                    geometry.CenterY,
                    geometry.CenterZ) &&
                proxyCollider.radius == geometry.Radius &&
                proxyCollider.isTrigger;
            if (!NeonLetterColorInteractionActivationPolicy.CanActivate(
                    new NeonLetterColorInteractionActivationState(
                        HolderInactive:
                            !interactionHolder.activeInHierarchy,
                        ActionConfigured:
                            interaction._actionId == NativeUseAction &&
                            !interaction._usePlayerNetworkInteraction,
                        PromptConfigured:
                            interaction._interactGui == ownedPrompt,
                        CallbackRegistered: lease.CallbackRegistered,
                        GeometryConfigured: geometryConfigured)))
            {
                throw new InvalidOperationException(
                    "Native color interaction preparation did not complete " +
                    "while its holder remained inactive.");
            }

            return lease;
        }
        catch
        {
            if (lease != null)
            {
                lease.Dispose();
            }
            else if (interactionHolder != null)
            {
                UnityEngine.Object.Destroy(interactionHolder);
            }

            throw;
        }
    }

    private static void OnInteractionPerformed(
        ColorInteractionLease lease)
    {
        try
        {
            if (!TryCreateColorTarget(
                    lease,
                    out NeonLetterColorTarget target))
            {
                return;
            }

            SOTFNeonLettersUi.Open(target);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to open the native color editor: " +
                exception);
        }
    }

    private static bool TryCreateColorTarget(
        ColorInteractionLease lease,
        out NeonLetterColorTarget target)
    {
        target = null;
        if (!lease.TryGetLiveStructure(
                out ScrewStructure structure,
                out GameObject structureRoot))
        {
            return false;
        }

        int recipeId = structure.Recipe?.Id ?? int.MinValue;
        bool isCurrentLease = InteractionLeases.IsCurrent(
            lease.StructureInstanceId,
            lease);
        NeonLetterSmallDefinition definition =
            ResolveRestoreDefinition(recipeId);
        bool isKnownCompletedStructure =
            definition != null &&
            recipeId == lease.RecipeId &&
            structure.gameObject == structureRoot;
        if (!NeonLetterColorInteractionPolicy.CanOpenEditor(
                new NeonLetterColorInteractionValidation(
                    RootAlive:
                        structureRoot.activeInHierarchy,
                    IsCurrentLease: isCurrentLease,
                    IsKnownCompletedStructure:
                        isKnownCompletedStructure,
                    IsPlayerControllable:
                        GameState.IsPlayerControllable,
                    IsEditorOpen: SOTFNeonLettersUi.IsOpen,
                    IsDismantlingOrBlocked:
                        lease.IsDismantling ||
                        lease.IsBlocked)))
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

            networkEntity = structureRoot.GetComponent<BoltEntity>();
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

    private static void RecordTerminalCreationFailure(
        int structureInstanceId,
        int recipeId,
        ArgumentException exception)
    {
        var fingerprint =
            new ColorInteractionCreationFailureFingerprint(
                exception.GetType(),
                exception.Message);
        if (!InteractionCreationFailures.RecordTerminalFailure(
                structureInstanceId,
                fingerprint))
        {
            return;
        }

        RLog.Error(
            $"[SOTFNeonLetters] Native color interaction geometry is " +
            $"invalid for recipe {recipeId}: {exception}");
    }

    private static void RecordTransientCreationFailure(
        int structureInstanceId,
        int recipeId,
        Exception exception)
    {
        var fingerprint =
            new ColorInteractionCreationFailureFingerprint(
                exception.GetType(),
                exception.Message);
        if (!InteractionCreationFailures.RecordTransientFailure(
                structureInstanceId,
                _interactionUpdateTick,
                fingerprint))
        {
            return;
        }

        RLog.Error(
            $"[SOTFNeonLetters] Failed to create a native color " +
            $"interaction for recipe {recipeId}; retry is scheduled: " +
            exception);
    }

    private sealed class ColorInteractionLease : IDisposable
    {
        private readonly ScrewStructure _structure;
        private readonly GameObject _structureRoot;
        private readonly GameObject _interactionHolder;
        private readonly GameObject _interactionProxy;
        private readonly GenericInteraction _interaction;
        private readonly GameObject _ownedPrompt;
        private readonly Action _managedCallback;
        private readonly Il2CppAction _nativeCallback;
        private bool _callbackRegistered;
        private bool _disposed;

        public ColorInteractionLease(
            int structureInstanceId,
            int recipeId,
            ScrewStructure structure,
            GameObject structureRoot,
            GameObject interactionHolder,
            GameObject interactionProxy,
            GenericInteraction interaction,
            GameObject ownedPrompt)
        {
            StructureInstanceId = structureInstanceId;
            RecipeId = recipeId;
            _structure = structure;
            _structureRoot = structureRoot;
            _interactionHolder = interactionHolder;
            _interactionProxy = interactionProxy;
            InteractionInstanceId = interactionProxy.GetInstanceID();
            _interaction = interaction;
            _ownedPrompt = ownedPrompt;
            _managedCallback = OnActionPerformed;
            _nativeCallback =
                DelegateSupport.ConvertDelegate<Il2CppAction>(
                    _managedCallback);
        }

        internal int StructureInstanceId { get; }
        internal int InteractionInstanceId { get; }
        internal int RecipeId { get; }
        internal bool CallbackRegistered => _callbackRegistered;
        internal bool IsDismantling { get; set; }
        internal bool IsBlocked =>
            _interaction != null &&
            _interaction.InteractionBlocked;

        internal bool IsOwnedHierarchyAlive =>
            !_disposed &&
            _interactionHolder != null &&
            _interactionProxy != null &&
            _interaction != null &&
            _ownedPrompt != null;

        internal bool MatchesLiveRoot(int structureInstanceId)
        {
            return !_disposed &&
                   _structureRoot != null &&
                   _structureRoot.GetInstanceID() ==
                       structureInstanceId &&
                   _structure != null &&
                   _structure.gameObject == _structureRoot;
        }

        internal void RegisterCallback()
        {
            if (_callbackRegistered)
            {
                return;
            }

            _interaction.RegisterActionPerformed(_nativeCallback);
            _callbackRegistered = true;
        }

        internal void Activate()
        {
            if (_disposed ||
                _interactionHolder == null ||
                _interactionHolder.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "The native interaction holder is not ready to activate.");
            }

            _interactionHolder.SetActive(true);
        }

        internal void SetBlocked(bool isBlocked)
        {
            if (!_disposed && _interaction != null)
            {
                _interaction.SetInteractionBlocked(isBlocked);
            }
        }

        internal bool TryGetLiveStructure(
            out ScrewStructure structure,
            out GameObject structureRoot)
        {
            if (_disposed ||
                _structure == null ||
                _structureRoot == null ||
                _structure.gameObject != _structureRoot)
            {
                structure = null;
                structureRoot = null;
                return false;
            }

            structure = _structure;
            structureRoot = _structureRoot;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            bool callbackRegistered = _callbackRegistered;
            _callbackRegistered = false;
            try
            {
                if (callbackRegistered && _interaction != null)
                {
                    _interaction.UnregisterActionPerformed(
                        _nativeCallback);
                }
            }
            finally
            {
                if (_interactionHolder != null)
                {
                    UnityEngine.Object.Destroy(_interactionHolder);
                }
                else if (_interactionProxy != null)
                {
                    UnityEngine.Object.Destroy(_interactionProxy);
                }
                else if (_ownedPrompt != null)
                {
                    UnityEngine.Object.Destroy(_ownedPrompt);
                }

                GC.KeepAlive(_managedCallback);
                GC.KeepAlive(_nativeCallback);
            }
        }

        private void OnActionPerformed()
        {
            OnInteractionPerformed(this);
        }
    }

    private readonly record struct
        ColorInteractionCreationFailureFingerprint(
            Type FailureType,
            string Message);
}
