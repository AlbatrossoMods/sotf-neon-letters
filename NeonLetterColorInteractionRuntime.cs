using Bolt;
using HarmonyLib;
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
    private const int MaxInteractionLeaseSweepsPerUpdate = 16;
    private const int MaxInteractionBackfillsPerUpdate = 64;
    private const string NativeUseAction = "Use";
    private const string InteractionHolderName =
        "SOTFNeonLetters.ColorInteractionHolder";
    private const string InteractionProxyName =
        "SOTFNeonLetters.ColorInteractionProxy";
    private const string InteractionPromptName =
        "SOTFNeonLetters.ColorInteractionPrompt";
    private static readonly NeonLetterColorInteractionLeaseRegistry<
        ColorInteractionLease> InteractionLeases = new();
    private static readonly NeonLetterColorInteractionFailureGate
        InteractionFailureGate = new();
    private static readonly HashSet<int> OwnedInteractionInstanceIds = new();
    private static readonly NeonLetterColorInteractionPromptLifecycle<
        GameObject> PromptLifecycle = new(IsPromptTemplateAlive);
    private static readonly NeonLetterColorInteractionBackfillSchedule
        BackfillSchedule = new();
    private static long _interactionUpdateTick;
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
            NeonLetterColorInteractionPromptObservationResult result =
                PromptLifecycle.Observe(
                    new NeonLetterColorInteractionPromptCandidate<GameObject>(
                        IsOwnedColorInteraction: isOwned,
                        UsesNativeUseAction: string.Equals(
                            interaction._actionId,
                            NativeUseAction,
                            StringComparison.Ordinal),
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
        IScrewStructure registeredStructure)
    {
        if (!_initialized ||
            IsDedicatedOrHeadless() ||
            registeredStructure == null)
        {
            return;
        }

        try
        {
            ScrewStructure structure =
                registeredStructure.TryCast<ScrewStructure>();
            GameObject structureRoot = structure?.gameObject;
            int recipeId = registeredStructure.Recipe?.Id ?? int.MinValue;
            if (structure == null ||
                structureRoot == null ||
                !NeonLetterColorInteractionPolicy.IsEditable(
                    hasCompletedStructure: true,
                    recipeId))
            {
                return;
            }

            int structureInstanceId = structureRoot.GetInstanceID();
            if (InteractionLeases.TryGet(
                    structureInstanceId,
                    out ColorInteractionLease existingLease))
            {
                if (existingLease.MatchesLiveRoot(structureInstanceId))
                {
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
                RLog.Error(
                    $"[SOTFNeonLetters] Cannot create a native color " +
                    $"interaction for recipe {recipeId}: the completed " +
                    $"structure root has no BoxCollider.");
                return;
            }

            NeonLetterColorInteractionGeometry geometry =
                NeonLetterColorInteractionGeometryPolicy.Resolve(
                    new NeonLetterColorInteractionBounds(
                        rootCollider.center.x,
                        rootCollider.center.y,
                        rootCollider.center.z,
                        rootCollider.size.x,
                        rootCollider.size.y,
                        rootCollider.size.z));
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
                return;
            }

            OwnedInteractionInstanceIds.Add(
                lease.InteractionInstanceId);
            try
            {
                lease.Activate();
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
            RLog.Error(
                $"[SOTFNeonLetters] Failed to create one native color " +
                $"interaction: {exception}");
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
            "compatible vanilla Use prompt. The mod will retry; no raw-key " +
            "fallback was installed.");
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

    private static void AdvanceColorInteractions()
    {
        if (IsDedicatedOrHeadless())
        {
            ReleaseAllInteractions();
            return;
        }

        if (_interactionUpdateTick < long.MaxValue)
        {
            _interactionUpdateTick++;
        }

        IReadOnlyList<ColorInteractionLease> expiredLeases =
            InteractionLeases.Sweep(
                MaxInteractionLeaseSweepsPerUpdate,
                IsInteractionLeaseAlive);
        for (int index = 0; index < expiredLeases.Count; index++)
        {
            DisposeInteractionLease(expiredLeases[index]);
        }

        if (!PromptLifecycle.TryGetTemplate(out _))
        {
            return;
        }

        if (!PromptLifecycle.IsBackfillPending &&
            BackfillSchedule.TryBeginAttempt(
                _interactionUpdateTick))
        {
            PromptLifecycle.StartBackfillIfTemplateAvailable();
        }

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
        if (!PromptLifecycle.IsBackfillPending)
        {
            return;
        }

        if (!ScrewStructureManager.TryGetInstance(
                out ScrewStructureManager manager) ||
            manager._structures == null)
        {
            PromptLifecycle.ReportBackfillUnavailable();
            return;
        }

        Il2CppSystem.Collections.Generic.List<IScrewStructure> structures =
            manager._structures;
        NeonLetterColorInteractionBackfillWindow window =
            PromptLifecycle.TakeBackfillWindow(
                structures.Count,
                MaxInteractionBackfillsPerUpdate);
        int endOffset = window.StartOffset + window.Count;
        for (int index = window.StartOffset;
             index < endOffset &&
             index < structures.Count;
             index++)
        {
            RegisterColorInteraction(structures[index]);
        }
    }

    private static bool IsDedicatedOrHeadless()
    {
        return NetUtils.IsDedicatedServer ||
               Application.isBatchMode;
    }

    private static void ReleaseAllInteractions()
    {
        IReadOnlyList<ColorInteractionLease> leases =
            InteractionLeases.Drain();
        for (int index = 0; index < leases.Count; index++)
        {
            DisposeInteractionLease(leases[index]);
        }
    }

    private static void DisposeInteractionLease(
        ColorInteractionLease lease)
    {
        if (lease == null)
        {
            return;
        }

        OwnedInteractionInstanceIds.Remove(
            lease.InteractionInstanceId);
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
        _isObservingPrompt = false;
        _interactionUpdateTick = 0;
        OwnedInteractionInstanceIds.Clear();
        PromptLifecycle.Reset();
        BackfillSchedule.Reset();
        InteractionFailureGate.ResetPromptFailureReport();
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
}

[HarmonyPatch(
    typeof(ScrewStructureManager),
    nameof(ScrewStructureManager.Register),
    new[] { typeof(IScrewStructure) })]
internal static class NeonLetterColorInteractionRegisterPatch
{
    [HarmonyPostfix]
    private static void AfterRegister(IScrewStructure __0)
    {
        NeonLetterColorRuntime.RegisterColorInteraction(__0);
    }
}

[HarmonyPatch(
    typeof(ScrewStructureManager),
    nameof(ScrewStructureManager.Unregister),
    new[] { typeof(IScrewStructure) })]
internal static class NeonLetterColorInteractionUnregisterPatch
{
    [HarmonyPrefix]
    private static void BeforeUnregister(IScrewStructure __0)
    {
        NeonLetterColorRuntime.UnregisterColorInteraction(__0);
    }
}

[HarmonyPatch(
    typeof(GenericInteraction),
    nameof(GenericInteraction.OnEnable))]
internal static class NeonLetterColorInteractionPromptObservationPatch
{
    [HarmonyPostfix]
    private static void AfterOnEnable(GenericInteraction __instance)
    {
        NeonLetterColorRuntime.ObserveNativeInteractionPrompt(__instance);
    }
}
