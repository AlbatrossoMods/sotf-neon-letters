using Bolt;
using Endnight.Extensions;
using RedLoader;
using Sons.Crafting.Structures;
using SonsSdk;
using SonsSdk.Networking;
using UdpKit;
using UnityEngine;

namespace SOTFNeonLetters;

internal static partial class NeonLetterMultiplayerRuntime
{
    private const string ChangeRequestEventId =
        "SOTFNeonLetters.ColorChangeRequest.v2";
    private const string ChangeResultEventId =
        "SOTFNeonLetters.ColorChangeResult.v1";
    private const string ColorStateEventId =
        "SOTFNeonLetters.ColorState.v2";
    private const string HandshakeHelloEventId =
        "SOTFNeonLetters.HandshakeHello.v1";
    private const string HandshakeResultEventId =
        "SOTFNeonLetters.HandshakeResult.v1";
    private const string ColorPageRequestEventId =
        "SOTFNeonLetters.ColorPageRequest.v1";
    private const string ColorPageResponseEventId =
        "SOTFNeonLetters.ColorPageResponse.v1";
    private const int MaxPendingColorItemsPerTick = 16;
    private const byte ClientDisconnectKey = 0;
    private const double PendingColorLifetimeSeconds = 15d;
    private static readonly NeonLetterAuthoritativeColors<ulong> AuthoritativeColors =
        new();
    private static readonly NeonLetterHostApplyCoordinator<
        BoltConnection,
        ulong>
        HostApplyCoordinator = new(AuthoritativeColors);
    private static readonly NeonLetterClientApplyCoordinator<ulong>
        ClientApplyCoordinator = new(PendingColorLifetimeSeconds);
    private static readonly NeonLetterReplicatedColorState<ulong> ReplicatedState =
        new(
            NeonLetterColorPageProtocol.MaxPendingEntries,
            PendingColorLifetimeSeconds);
    private static readonly ColorChangeRequestEvent ChangeRequest = new();
    private static readonly ColorChangeResultEvent ChangeResult = new();
    private static readonly ColorStateEvent ColorState = new();
    private static readonly HandshakeHelloEvent HandshakeHello = new();
    private static readonly HandshakeResultEvent HandshakeResult = new();
    private static readonly ColorPageRequestEvent ColorPageRequest = new();
    private static readonly ColorPageResponseEvent ColorPageResponse = new();
    private static readonly NeonLetterColorPageHostCoordinator<
        BoltConnection,
        ulong> ColorPageHostCoordinator = new(AuthoritativeColors);
    private static readonly NeonLetterColorPageClientCoordinator<ulong>
        ColorPageClientCoordinator = new();
    private static readonly NeonLetterLifecycleCoordinator Lifecycle = new();
    private static readonly NeonLetterHelloScheduler HelloScheduler = new(
        NeonLetterSessionProtocol.HelloResendIntervalSeconds,
        NeonLetterSessionProtocol.NegotiationTimeoutSeconds);
    private static readonly NeonLetterRoleSetupGate RoleSetupGate = new();
    private static readonly HashSet<BoltConnection> HostConnections =
        new();
    private static readonly NeonLetterDeferredDisconnects<BoltConnection>
        DeferredDisconnects = new();
    private static readonly NeonLetterDeferredDisconnects<byte>
        DeferredClientDisconnects = new();
    private static readonly NeonLetterClientSessionGate ClientSession = new();
    private static readonly Func<ulong, bool> IsLiveLetterIdentityCallback =
        IsLiveLetterIdentity;
    private static readonly Func<NeonLetterColorPageEntry<ulong>, bool>
        PublishColorPageEntryCallback = PublishColorPageEntry;
    private static readonly Func<
        NeonLetterClientApplyDecision<ulong>,
        bool> RetainLiveDecisionCallback = RetainLiveDecision;
    private static readonly Func<
        NeonLetterClientApplyDecision<ulong>,
        bool> RetainPagedDecisionCallback = RetainPagedDecision;
    private static readonly Action<ulong, NeonRgba>
        ApplyReplicatedColorCallback = ApplyReplicatedColor;
    private static readonly Action<ulong, Exception>
        PendingColorApplyErrorCallback = LogPendingColorApplyError;
    private static readonly Func<BoltConnection, bool>
        IsHostHandshakeAcceptedCallback = IsHostHandshakeAccepted;
    private static readonly Func<BoltConnection, bool>
        IsAcceptedForModTrafficCallback = IsAcceptedForModTraffic;
    private static readonly Func<byte, bool>
        ClientDisconnectExistsCallback = ClientDisconnectExists;
    private static readonly Action<byte>
        DisconnectClientCallback = DisconnectClient;
    private static readonly Action<byte, Exception>
        LogClientDisconnectFailureCallback = LogClientDisconnectFailure;
    private static readonly Func<BoltConnection, bool>
        HostConnectionExistsCallback = HostConnections.Contains;
    private static readonly Action<BoltConnection>
        DisconnectHostConnectionCallback =
            static connection => connection.Disconnect();
    private static readonly Action<BoltConnection, Exception>
        LogHostDisconnectFailureCallback = LogHostDisconnectFailure;
    private static readonly Action<
        NeonLetterTargetedColorPage<BoltConnection, ulong>>
        SendColorPageDeliveryCallback = SendColorPageDelivery;
    private static readonly Action<BoltConnection, Exception>
        ScheduleFailedColorPageCallback =
            ScheduleFailedColorPageConnection;
    private static bool _changeRequestRegistered;
    private static bool _changeResultRegistered;
    private static bool _colorStateRegistered;
    private static bool _handshakeHelloRegistered;
    private static bool _handshakeResultRegistered;
    private static bool _colorPageRequestRegistered;
    private static bool _colorPageResponseRegistered;
    private static NeonLetterSessionIdentity? _sessionIdentity;
    private static NeonLetterHandshakeRegistry<BoltConnection> _hostHandshakes;
    private static ulong _nextHelloId = 1;
    private static ulong _clientHelloId;
    private static bool _roleReady;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            SdkEvents.OnAfterSpawn.Subscribe(RegisterHandlersForCurrentRole);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnAfterSpawn.Unsubscribe(
                    RegisterHandlersForCurrentRole));

            SdkEvents.OnInWorldUpdate.Subscribe(DrainPendingColors);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnInWorldUpdate.Unsubscribe(
                    DrainPendingColors));

            SdkEvents.OnInWorldUpdate.Subscribe(AdvanceSession);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnInWorldUpdate.Unsubscribe(
                    AdvanceSession));

            SdkEvents.OnInWorldUpdate.Subscribe(DrainColorPageResponses);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnInWorldUpdate.Unsubscribe(
                    DrainColorPageResponses));

            SdkEvents.OnInWorldUpdate.Subscribe(RequestColorPage);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnInWorldUpdate.Unsubscribe(
                    RequestColorPage));

            SdkEvents.OnWorldExited.Subscribe(OnWorldExited);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnWorldExited.Unsubscribe(OnWorldExited));
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
        UnregisterRoleHandlers();
        Lifecycle.Cleanup(
            exception => RLog.Error(
                $"[SOTFNeonLetters] Multiplayer runtime cleanup failed: " +
                exception));
        AuthoritativeColors.Clear();
        HostApplyCoordinator.Clear();
        ColorPageHostCoordinator.Clear();
        ColorPageClientCoordinator.Clear();
        ClearSessionState();
    }

    private static void RegisterHandlersForCurrentRole()
    {
        Exception failure =
            RoleSetupGate.TryRun(RegisterHandlersForCurrentRoleCore);
        if (failure == null)
        {
            return;
        }

        UnregisterRoleHandlers();
        try
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to initialize multiplayer " +
                $"session; setup disabled until lifecycle reset: {failure}");
        }
        catch
        {
            // Role setup must remain non-throwing for SDK event callbacks.
        }
    }

    private static void RegisterHandlersForCurrentRoleCore()
    {
        if (!BoltNetwork.isRunning)
        {
            UnregisterRoleHandlers();
            ClearSessionState();
            return;
        }

        BeginRoleSession();
        EnsureSessionIdentity();
        if (NetUtils.IsServer)
        {
            // Unknown event IDs are dropped before Packets.HandlePacket relays
            // them, so client-bound events stay unregistered on the host.
            UnregisterColorState();
            UnregisterChangeResult();
            UnregisterHandshakeResult();
            UnregisterColorPageResponse();
            try
            {
                RegisterHandshakeHello();
                RegisterChangeRequest();
                RegisterColorPageRequest();
                _roleReady = true;
            }
            catch
            {
                UnregisterRoleHandlers();
                throw;
            }

            return;
        }

        if (NetUtils.IsClient)
        {
            UnregisterHandshakeHello();
            UnregisterChangeRequest();
            UnregisterColorPageRequest();
            try
            {
                RegisterHandshakeResult();
                RegisterChangeResult();
                RegisterColorState();
                RegisterColorPageResponse();
                StartClientHandshake();
                _roleReady = true;
            }
            catch
            {
                UnregisterRoleHandlers();
                throw;
            }

            return;
        }

        UnregisterRoleHandlers();
    }

    private static void UnregisterRoleHandlers()
    {
        UnregisterRoleHandler(UnregisterHandshakeHello);
        UnregisterRoleHandler(UnregisterHandshakeResult);
        UnregisterRoleHandler(UnregisterChangeRequest);
        UnregisterRoleHandler(UnregisterChangeResult);
        UnregisterRoleHandler(UnregisterColorState);
        UnregisterRoleHandler(UnregisterColorPageRequest);
        UnregisterRoleHandler(UnregisterColorPageResponse);
    }

    private static void UnregisterRoleHandler(Action unregister)
    {
        try
        {
            unregister();
        }
        catch (Exception exception)
        {
            try
            {
                RLog.Error(
                    $"[SOTFNeonLetters] Multiplayer packet cleanup failed: " +
                    exception);
            }
            catch
            {
                // Packet cleanup must continue even when logging fails.
            }
        }
    }

    private static void RegisterChangeRequest()
    {
        if (_changeRequestRegistered)
        {
            return;
        }

        Packets.Register(ChangeRequest);
        _changeRequestRegistered = true;
    }

    private static void RegisterChangeResult()
    {
        if (_changeResultRegistered)
        {
            return;
        }

        Packets.Register(ChangeResult);
        _changeResultRegistered = true;
    }

    private static void RegisterHandshakeHello()
    {
        if (_handshakeHelloRegistered)
        {
            return;
        }

        Packets.Register(HandshakeHello);
        _handshakeHelloRegistered = true;
    }

    private static void RegisterHandshakeResult()
    {
        if (_handshakeResultRegistered)
        {
            return;
        }

        Packets.Register(HandshakeResult);
        _handshakeResultRegistered = true;
    }

    private static void RegisterColorState()
    {
        if (_colorStateRegistered)
        {
            return;
        }

        Packets.Register(ColorState);
        _colorStateRegistered = true;
    }

    private static void RegisterColorPageRequest()
    {
        if (_colorPageRequestRegistered)
        {
            return;
        }

        Packets.Register(ColorPageRequest);
        _colorPageRequestRegistered = true;
    }

    private static void RegisterColorPageResponse()
    {
        if (_colorPageResponseRegistered)
        {
            return;
        }

        Packets.Register(ColorPageResponse);
        _colorPageResponseRegistered = true;
    }

    private static void UnregisterChangeRequest()
    {
        if (!_changeRequestRegistered)
        {
            return;
        }

        try
        {
            Packets.Unregister(ChangeRequest);
        }
        finally
        {
            _changeRequestRegistered = false;
        }
    }

    private static void UnregisterChangeResult()
    {
        if (!_changeResultRegistered)
        {
            return;
        }

        try
        {
            Packets.Unregister(ChangeResult);
        }
        finally
        {
            _changeResultRegistered = false;
        }
    }

    private static void UnregisterHandshakeHello()
    {
        if (!_handshakeHelloRegistered)
        {
            return;
        }

        try
        {
            Packets.Unregister(HandshakeHello);
        }
        finally
        {
            _handshakeHelloRegistered = false;
        }
    }

    private static void UnregisterHandshakeResult()
    {
        if (!_handshakeResultRegistered)
        {
            return;
        }

        try
        {
            Packets.Unregister(HandshakeResult);
        }
        finally
        {
            _handshakeResultRegistered = false;
            ClientSession.Reject();
            HelloScheduler.Clear();
        }
    }

    private static void UnregisterColorState()
    {
        try
        {
            if (_colorStateRegistered)
            {
                Packets.Unregister(ColorState);
            }
        }
        finally
        {
            _colorStateRegistered = false;
        }
    }

    private static void UnregisterColorPageRequest()
    {
        try
        {
            if (_colorPageRequestRegistered)
            {
                Packets.Unregister(ColorPageRequest);
            }
        }
        finally
        {
            _colorPageRequestRegistered = false;
            ColorPageHostCoordinator.Clear();
        }
    }

    private static void UnregisterColorPageResponse()
    {
        try
        {
            if (_colorPageResponseRegistered)
            {
                Packets.Unregister(ColorPageResponse);
            }
        }
        finally
        {
            _colorPageResponseRegistered = false;
            ColorPageClientCoordinator.Clear();
        }
    }

    internal static bool RequestColor(BoltEntity entity, NeonRgba color)
    {
        if (!BoltNetwork.isRunning ||
            !TryResolveLiveLetter(entity, out NetworkId networkId, out _, out _))
        {
            return false;
        }

        try
        {
            if (NetUtils.IsServer)
            {
                return AcceptHostColor(networkId, color);
            }

            if (!NetUtils.IsClient)
            {
                return false;
            }

            if (!ClientSession.IsAccepted ||
                DeferredClientDisconnects.IsQuarantined(
                    ClientDisconnectKey))
            {
                return false;
            }

            NeonLetterApplyRequest<ulong> request =
                ClientApplyCoordinator.Start(
                    networkId.PackedValue,
                    color,
                    Time.realtimeSinceStartupAsDouble);
            ChangeRequest.SendRequest(
                request.RequestId,
                networkId,
                request.Color);
            return true;
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send {ChangeRequest.Id}: {exception}");
            return false;
        }
    }

    internal static NeonRgba ResolveColor(BoltEntity entity)
    {
        if (!BoltNetwork.isRunning ||
            entity == null ||
            !entity.isAttached ||
            entity.networkId.IsZero)
        {
            return NeonRgba.ProjectCyan;
        }

        ulong identity = entity.networkId.PackedValue;
        if (NetUtils.IsServer)
        {
            return AuthoritativeColors.Resolve(identity);
        }

        return NetUtils.IsClient
            ? ClientApplyCoordinator.ResolveAuthoritative(identity).Color
            : NeonRgba.ProjectCyan;
    }

    internal static void RemoveDismantledColor(ulong networkIdentity)
    {
        AuthoritativeColors.Remove(networkIdentity);
        ClientApplyCoordinator.Remove(networkIdentity);
        ReplicatedState.Remove(networkIdentity);
    }

    internal static bool TryRestoreHostColor(
        BoltEntity entity,
        int expectedRecipeId,
        NeonRgba color)
    {
        if (!TryResolveLiveLetter(
                entity,
                out NetworkId networkId,
                out _,
                out NeonLetterSmallDefinition definition) ||
            definition.RecipeId != expectedRecipeId)
        {
            return false;
        }

        return AcceptHostColor(networkId, color);
    }

    internal static void RequestColorPage()
    {
        if (!_colorStateRegistered ||
            !_colorPageResponseRegistered ||
            !BoltNetwork.isRunning ||
            !NetUtils.IsClient ||
            NetUtils.IsServer ||
            !ClientSession.IsAccepted ||
            DeferredClientDisconnects.IsQuarantined(ClientDisconnectKey))
        {
            return;
        }

        double nowSeconds = Time.realtimeSinceStartupAsDouble;
        if (!ColorPageClientCoordinator.TryGetDueRequest(
                canRequest: ClientSession.IsAccepted,
                nowSeconds,
                out NeonLetterColorPageRequest request))
        {
            return;
        }

        ColorPageClientCoordinator.RecordRequestAttempt(nowSeconds);
        try
        {
            ColorPageRequest.SendRequest(request);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send {ColorPageRequest.Id}: " +
                exception);
        }
    }

    private static void HandleColorChangeRequest(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!IsAcceptedClient(fromConnection))
        {
            return;
        }

        byte version = packet.ReadByte();
        ulong requestId = packet.ReadULong();
        NetworkId networkId = packet.ReadNetworkId();
        uint packedColor = packet.ReadUInt();
        NeonRgba color = version == NeonLetterNetworkProtocol.CurrentVersion
            ? NeonLetterNetworkProtocol.Unpack(version, packedColor)
            : NeonRgba.ProjectCyan;
        ProcessHostColorRequest(
            fromConnection,
            requestId,
            networkId,
            color,
            version == NeonLetterNetworkProtocol.CurrentVersion);
    }

    private static void HandleColorChangeResult(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!ClientSession.IsAccepted ||
            DeferredClientDisconnects.IsQuarantined(ClientDisconnectKey) ||
            !IsStateFromHost(fromConnection))
        {
            return;
        }

        byte version = packet.ReadByte();
        ulong requestId = packet.ReadULong();
        NetworkId networkId = packet.ReadNetworkId();
        var status = (NeonLetterApplyStatus)packet.ReadByte();
        ulong revision = packet.ReadULong();
        uint packedColor = packet.ReadUInt();
        if (version != NeonLetterNetworkProtocol.CurrentVersion ||
            requestId == 0 ||
            networkId.IsZero ||
            (status != NeonLetterApplyStatus.Accepted &&
                status != NeonLetterApplyStatus.Rejected))
        {
            return;
        }

        NeonRgba color = NeonLetterNetworkProtocol.Unpack(
            version,
            packedColor);
        NeonLetterClientApplyDecision<ulong> decision =
            ClientApplyCoordinator.AcceptResult(
                new NeonLetterApplyResult<ulong>(
                    requestId,
                    networkId.PackedValue,
                    status,
                    color,
                    revision));
        ApplyClientDecision(decision);
    }

    private static void HandleColorState(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!ClientSession.IsAccepted ||
            DeferredClientDisconnects.IsQuarantined(ClientDisconnectKey) ||
            !IsStateFromHost(fromConnection))
        {
            return;
        }

        byte version = packet.ReadByte();
        NetworkId networkId = packet.ReadNetworkId();
        ulong revision = packet.ReadULong();
        NeonRgba color = NeonLetterNetworkProtocol.Unpack(
            version,
            packet.ReadUInt());
        if (networkId.IsZero)
        {
            throw new InvalidOperationException(
                "A neon letter color state cannot use a zero network identity.");
        }

        bool retained = ClientApplyCoordinator.TryAcceptLive(
                networkId.PackedValue,
                color,
                revision,
                RetainLiveDecisionCallback);
        if (!retained)
        {
            DeferredClientDisconnects.Schedule(ClientDisconnectKey);
        }
    }

    private static void HandleColorPageRequest(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!IsAcceptedClient(fromConnection))
        {
            return;
        }

        var request = new NeonLetterColorPageRequest(
            packet.ReadByte(),
            packet.ReadULong(),
            packet.ReadULong(),
            packet.ReadULong());
        NeonLetterColorPageScheduleResult result =
            ColorPageHostCoordinator.TryScheduleRequest(
                fromConnection,
                canSchedule: IsAcceptedForModTraffic(fromConnection),
                request);
        if (result !=
            NeonLetterColorPageScheduleResult.CapacityExceeded)
        {
            return;
        }

        ScheduleFailedColorPageConnection(
            fromConnection,
            new InvalidOperationException(
                "The pending color page peer limit was reached."));
    }

    private static void DrainColorPageResponses()
    {
        if (!BoltNetwork.isRunning ||
            !NetUtils.IsServer ||
            ColorPageHostCoordinator.PendingRequestCount == 0)
        {
            return;
        }

        ColorPageHostCoordinator.DrainScheduledRequests(
            IsAcceptedForModTrafficCallback,
            SendColorPageDeliveryCallback,
            ScheduleFailedColorPageCallback);
    }

    private static void SendColorPageDelivery(
        NeonLetterTargetedColorPage<BoltConnection, ulong> delivery)
    {
        ColorPageResponse.SendResponse(
            delivery.Response,
            delivery.Peer);
    }

    private static void ScheduleFailedColorPageConnection(
        BoltConnection connection,
        Exception exception)
    {
        QuarantineHostConnection(connection);
        try
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send " +
                $"{ColorPageResponse.Id} to " +
                $"{connection.ConnectionId}; disconnect scheduled: " +
                exception);
        }
        catch
        {
            // Logging must not undo quarantine after a delivery failure.
        }
    }

    private static void HandleColorPageResponse(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!ClientSession.IsAccepted ||
            DeferredClientDisconnects.IsQuarantined(ClientDisconnectKey) ||
            !IsStateFromHost(fromConnection))
        {
            return;
        }

        var reader = new ColorPageWireReader(packet);
        NeonLetterColorPageResponse<ulong> response =
            NeonLetterColorPageWireParser.ReadResponse<
                ColorPageWireReader,
                ulong>(ref reader);

        ColorPageClientCoordinator.TryAcceptResponse(
            canApply: ClientSession.IsAccepted,
            response,
            Time.realtimeSinceStartupAsDouble,
            PublishColorPageEntryCallback);
    }

    private static bool PublishColorPageEntry(
        NeonLetterColorPageEntry<ulong> entry)
    {
        if (!ClientSession.IsAccepted ||
            DeferredClientDisconnects.IsQuarantined(ClientDisconnectKey))
        {
            return false;
        }

        bool retained = ClientApplyCoordinator.TryAcceptLive(
                entry.Identity,
                entry.Color,
                entry.EntityRevision,
                RetainPagedDecisionCallback);
        if (!retained)
        {
            DeferredClientDisconnects.Schedule(ClientDisconnectKey);
        }

        return retained;
    }

    private static bool RetainLiveDecision(
        NeonLetterClientApplyDecision<ulong> decision)
    {
        return ReplicatedState.TryReceiveAuthoritative(
            decision.Identity,
            decision.Color,
            Time.realtimeSinceStartupAsDouble,
            IsLiveLetterIdentityCallback,
            ApplyReplicatedColorCallback);
    }

    private static bool RetainPagedDecision(
        NeonLetterClientApplyDecision<ulong> decision)
    {
        return ReplicatedState.TryReceiveAuthoritative(
            decision.Identity,
            decision.Color,
            Time.realtimeSinceStartupAsDouble,
            IsLiveLetterIdentityCallback,
            ApplyReplicatedColorCallback);
    }

    private static void DrainPendingColors()
    {
        if (BoltNetwork.isRunning &&
            NetUtils.IsClient &&
            !NetUtils.IsServer &&
            ClientSession.IsAccepted &&
            !DeferredClientDisconnects.IsQuarantined(ClientDisconnectKey))
        {
            foreach (NeonLetterClientApplyDecision<ulong> decision in
                ClientApplyCoordinator.RejectTimedOut(
                    Time.realtimeSinceStartupAsDouble))
            {
                ApplyClientDecision(decision);
            }
        }

        if (!BoltNetwork.isRunning ||
            !NetUtils.IsClient ||
            NetUtils.IsServer ||
            !ClientSession.IsAccepted ||
            DeferredClientDisconnects.IsQuarantined(ClientDisconnectKey) ||
            ReplicatedState.PendingCount == 0)
        {
            return;
        }

        try
        {
            ReplicatedState.DrainReady(
                Time.realtimeSinceStartupAsDouble,
                MaxPendingColorItemsPerTick,
                IsLiveLetterIdentityCallback,
                ApplyReplicatedColorCallback,
                PendingColorApplyErrorCallback);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to apply pending {ColorState.Id}: " +
                exception);
        }
    }

    private static void OnWorldExited()
    {
        UnregisterRoleHandlers();
        AuthoritativeColors.Clear();
        ColorPageHostCoordinator.Clear();
        ColorPageClientCoordinator.Clear();
        ClearSessionState();
    }

    private static bool IsLiveLetterIdentity(ulong identity)
    {
        return TryResolveLiveLetterIdentity(
            identity,
            out _,
            out _,
            out _);
    }

    private static void ApplyReplicatedColor(ulong identity, NeonRgba color)
    {
        if (!ClientSession.IsAccepted)
        {
            return;
        }

        if (!TryResolveLiveLetterIdentity(
                identity,
                out _,
                out ScrewStructure structure,
                out NeonLetterSmallDefinition definition))
        {
            throw new InvalidOperationException(
                $"Neon letter network identity {identity} is not live.");
        }

        NeonLetterColorRuntime.ApplyEmission(
            structure.gameObject,
            definition,
            color);
    }

    private static void LogPendingColorApplyError(
        ulong identity,
        Exception exception)
    {
        RLog.Error(
            $"[SOTFNeonLetters] Failed to apply pending {ColorState.Id} " +
            $"for network identity {identity}: {exception}");
    }

    private static void ProcessHostColorRequest(
        BoltConnection fromConnection,
        ulong requestId,
        NetworkId networkId,
        NeonRgba color,
        bool protocolAccepted)
    {
        if (HostApplyCoordinator.TryResolveReplay(
                fromConnection,
                requestId,
                networkId.PackedValue,
                out NeonLetterApplyResult<ulong> replay))
        {
            TrySendColorResult(replay, fromConnection);
            return;
        }

        ScrewStructure structure = null;
        NeonLetterSmallDefinition definition = null;
        bool isLive = requestId != 0 &&
            protocolAccepted &&
            !networkId.IsZero &&
            TryResolveLiveLetter(
                networkId,
                out _,
                out structure,
                out definition);

        bool TryApplyRequestedColor(NeonRgba canonicalColor)
        {
            try
            {
                NeonLetterColorRuntime.ApplyEmission(
                    structure.gameObject,
                    definition,
                    canonicalColor);
                return true;
            }
            catch (Exception exception)
            {
                RLog.Error(
                    $"[SOTFNeonLetters] Failed to apply requested color for " +
                    $"{networkId.PackedValue}: {exception}");
                return false;
            }
        }

        int recipeId = isLive
            ? definition.RecipeId
            : int.MinValue;
        NeonLetterHostApplyOutcome<ulong> outcome =
            HostApplyCoordinator.Process(
                fromConnection,
                requestId,
                networkId.PackedValue,
                isHost: true,
                isLive,
                recipeId,
                color,
                TryApplyRequestedColor);
        TrySendColorResult(outcome.Result, fromConnection);
        if (outcome.ShouldBroadcast)
        {
            BroadcastColorState(
                networkId,
                outcome.Result.AuthoritativeColor,
                outcome.Result.Revision);
        }
    }

    private static void TrySendColorResult(
        NeonLetterApplyResult<ulong> result,
        BoltConnection connection)
    {
        try
        {
            ChangeResult.SendResult(result, connection);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send {ChangeResult.Id} " +
                $"request {result.RequestId} to {connection.ConnectionId}: " +
                exception);
        }
    }

    private static void ApplyClientDecision(
        NeonLetterClientApplyDecision<ulong> decision)
    {
        if (decision.Action == NeonLetterClientApplyAction.Ignored ||
            !ClientSession.IsAccepted)
        {
            return;
        }

        if (!ReplicatedState.TryReceiveAuthoritative(
                decision.Identity,
                decision.Color,
                Time.realtimeSinceStartupAsDouble,
                IsLiveLetterIdentityCallback,
                ApplyReplicatedColorCallback))
        {
            DeferredClientDisconnects.Schedule(ClientDisconnectKey);
        }
    }

    private static bool AcceptHostColor(NetworkId networkId, NeonRgba color)
    {
        if (!BoltNetwork.isRunning ||
            !NetUtils.IsServer ||
            !TryResolveLiveLetter(
                networkId,
                out _,
                out ScrewStructure structure,
                out NeonLetterSmallDefinition definition))
        {
            return false;
        }

        NeonLetterColorAcceptance acceptance = AuthoritativeColors.TryAccept(
            isHost: true,
            identity: networkId.PackedValue,
            isLive: true,
            recipeId: definition.RecipeId,
            color,
            tryApply: canonicalColor =>
            {
                NeonLetterColorRuntime.ApplyEmission(
                    structure.gameObject,
                    definition,
                    canonicalColor);
                return true;
            });
        if (!acceptance.Accepted)
        {
            return false;
        }

        try
        {
            BroadcastColorState(
                networkId,
                acceptance.AuthoritativeColor,
                acceptance.Revision);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to broadcast {ColorState.Id}: {exception}");
        }

        return true;
    }

    private static void BroadcastColorState(
        NetworkId networkId,
        NeonRgba color,
        ulong revision)
    {
        if (_hostHandshakes == null)
        {
            return;
        }

        var connections = new List<BoltConnection>();
        BoltNetwork.clients.ForEach(
            (Action<BoltConnection>)(connection => connections.Add(connection)));
        NeonLetterPeerDelivery.Deliver(
            connections,
            IsAcceptedForModTrafficCallback,
            connection => ColorState.SendToClient(
                networkId,
                color,
                revision,
                connection),
            ScheduleFailedColorStateConnection);
    }

    private static void ScheduleFailedColorStateConnection(
        BoltConnection connection,
        Exception exception)
    {
        QuarantineHostConnection(connection);
        try
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send {ColorState.Id} to " +
                $"{connection.ConnectionId}; disconnect scheduled: {exception}");
        }
        catch
        {
            // A logging failure must not block delivery to later peers.
        }
    }

    private static void QuarantineHostConnection(BoltConnection connection)
    {
        ColorPageHostCoordinator.Quarantine(
            connection,
            DeferredDisconnects.Schedule);
    }

    private static bool TryResolveLiveLetter(
        BoltEntity entity,
        out NetworkId networkId,
        out ScrewStructure structure,
        out NeonLetterSmallDefinition definition)
    {
        networkId = default;
        structure = null;
        definition = null;
        if (entity == null || !entity.isAttached)
        {
            return false;
        }

        networkId = entity.networkId;
        return !networkId.IsZero &&
               TryResolveLiveLetter(
                   networkId,
                   out _,
                   out structure,
                   out definition);
    }

    private static bool TryResolveLiveLetter(
        NetworkId networkId,
        out BoltEntity entity,
        out ScrewStructure structure,
        out NeonLetterSmallDefinition definition)
    {
        entity = null;
        structure = null;
        definition = null;
        if (!BoltNetwork.isRunning || networkId.IsZero)
        {
            return false;
        }

        entity = BoltNetwork.FindEntity(networkId);
        if (entity == null ||
            !entity.isAttached ||
            entity.networkId.IsZero ||
            entity.networkId.PackedValue != networkId.PackedValue)
        {
            return false;
        }

        structure = entity.GetComponent<ScrewStructure>();
        int recipeId = structure?.Recipe?.Id ?? int.MinValue;
        definition = NeonLetterSmallCatalog.All.FirstOrDefault(
            candidate => candidate.RecipeId == recipeId);
        return structure != null && definition != null;
    }

    private static bool TryResolveLiveLetterIdentity(
        ulong identity,
        out NetworkId networkId,
        out ScrewStructure structure,
        out NeonLetterSmallDefinition definition)
    {
        networkId = default;
        structure = null;
        definition = null;
        if (identity == 0ul || !BoltNetwork.isRunning)
        {
            return false;
        }

        var candidateNetworkId = new NetworkId(identity);
        if (!TryResolveLiveLetter(
                candidateNetworkId,
                out BoltEntity entity,
                out structure,
                out definition))
        {
            return false;
        }

        networkId = entity.networkId;
        return true;
    }

    private static bool IsStateFromHost(BoltConnection fromConnection)
    {
        if (!BoltNetwork.isRunning ||
            !NetUtils.IsClient ||
            NetUtils.IsServer ||
            fromConnection == null ||
            BoltNetwork.server == null)
        {
            return false;
        }

        return fromConnection.ConnectionId == BoltNetwork.server.ConnectionId;
    }

    private static void LogReadFailure(string eventId, Exception exception)
    {
        RLog.Error(
            $"[SOTFNeonLetters] Failed to read {eventId}: {exception}");
    }

}
