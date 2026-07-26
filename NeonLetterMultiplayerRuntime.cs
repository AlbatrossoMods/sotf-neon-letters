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
    private const string SnapshotRequestEventId =
        "SOTFNeonLetters.ColorSnapshotRequest.v2";
    private const string SnapshotFrameEventId =
        "SOTFNeonLetters.ColorSnapshotFrame.v1";
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
            NeonLetterSnapshotProtocol.MaxSnapshotEntries,
            PendingColorLifetimeSeconds);
    private static readonly ColorChangeRequestEvent ChangeRequest = new();
    private static readonly ColorChangeResultEvent ChangeResult = new();
    private static readonly ColorStateEvent ColorState = new();
    private static readonly HandshakeHelloEvent HandshakeHello = new();
    private static readonly HandshakeResultEvent HandshakeResult = new();
    private static readonly ColorSnapshotRequestEvent SnapshotRequest = new();
    private static readonly ColorSnapshotFrameEvent SnapshotFrame = new();
    private static readonly NeonLetterSnapshotRequestScheduler
        SnapshotRequestScheduler = new();
    private static readonly NeonLetterSnapshotBatchCoordinator
        SnapshotBatchCoordinator = new();
    private static readonly NeonLetterSnapshotSendCoordinator<BoltConnection>
        SnapshotSendCoordinator = new();
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
    private static readonly Func<NeonLetterSnapshotEntry[]>
        FreezeAuthoritativeSnapshotCallback = FreezeAuthoritativeSnapshot;
    private static readonly Func<NeonLetterSnapshotEntry, ulong>
        GetSnapshotEntryIdentityCallback =
            static entry => entry.Identity;
    private static readonly Func<NeonLetterSnapshotEntry, NeonRgba>
        GetSnapshotEntryColorCallback =
            static entry => entry.Color;
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
    private static bool _changeRequestRegistered;
    private static bool _changeResultRegistered;
    private static bool _colorStateRegistered;
    private static bool _handshakeHelloRegistered;
    private static bool _handshakeResultRegistered;
    private static bool _snapshotRequestRegistered;
    private static bool _snapshotFrameRegistered;
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

            SdkEvents.OnInWorldUpdate.Subscribe(RequestSnapshot);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnInWorldUpdate.Unsubscribe(
                    RequestSnapshot));

            SdkEvents.OnInWorldUpdate.Subscribe(DrainSnapshotSendJobs);
            Lifecycle.CompleteStage(
                () => SdkEvents.OnInWorldUpdate.Unsubscribe(
                    DrainSnapshotSendJobs));

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
        SnapshotRequestScheduler.Rearm();
        SnapshotBatchCoordinator.Reset();
        SnapshotSendCoordinator.Clear();
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
            UnregisterSnapshotFrame();
            try
            {
                RegisterHandshakeHello();
                RegisterChangeRequest();
                RegisterSnapshotRequest();
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
            UnregisterSnapshotRequest();
            try
            {
                RegisterHandshakeResult();
                RegisterChangeResult();
                RegisterColorState();
                RegisterSnapshotFrame();
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
        UnregisterRoleHandler(UnregisterSnapshotRequest);
        UnregisterRoleHandler(UnregisterSnapshotFrame);
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

    private static void RegisterSnapshotRequest()
    {
        if (_snapshotRequestRegistered)
        {
            return;
        }

        Packets.Register(SnapshotRequest);
        _snapshotRequestRegistered = true;
    }

    private static void RegisterSnapshotFrame()
    {
        if (_snapshotFrameRegistered)
        {
            return;
        }

        Packets.Register(SnapshotFrame);
        _snapshotFrameRegistered = true;
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
            SnapshotRequestScheduler.Rearm();
            SnapshotBatchCoordinator.Reset();
        }
    }

    private static void UnregisterSnapshotRequest()
    {
        try
        {
            if (_snapshotRequestRegistered)
            {
                Packets.Unregister(SnapshotRequest);
            }
        }
        finally
        {
            _snapshotRequestRegistered = false;
            SnapshotSendCoordinator.Clear();
        }
    }

    private static void UnregisterSnapshotFrame()
    {
        try
        {
            if (_snapshotFrameRegistered)
            {
                Packets.Unregister(SnapshotFrame);
            }
        }
        finally
        {
            _snapshotFrameRegistered = false;
            SnapshotBatchCoordinator.Reset();
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

            if (!ClientSession.IsAccepted)
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

    internal static void RequestSnapshot()
    {
        if (!_colorStateRegistered ||
            !_snapshotFrameRegistered ||
            !BoltNetwork.isRunning ||
            !NetUtils.IsClient ||
            NetUtils.IsServer ||
            !ClientSession.IsAccepted ||
            !SnapshotRequestScheduler.CanAttempt)
        {
            return;
        }

        double nowSeconds = Time.realtimeSinceStartupAsDouble;
        if (!SnapshotRequestScheduler.IsDue(nowSeconds))
        {
            return;
        }

        try
        {
            ulong requestId = SnapshotBatchCoordinator.StartRequest();
            SnapshotRequest.SendRequest(requestId);
            SnapshotRequestScheduler.RecordSuccessfulSend(nowSeconds);
        }
        catch (Exception exception)
        {
            SnapshotRequestScheduler.DeferRetry(nowSeconds);
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send {SnapshotRequest.Id}: {exception}");
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
        if (!ClientSession.IsAccepted || !IsStateFromHost(fromConnection))
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
        if (!ClientSession.IsAccepted || !IsStateFromHost(fromConnection))
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

        NeonLetterClientApplyDecision<ulong> decision =
            ClientApplyCoordinator.AcceptLive(
                networkId.PackedValue,
                color,
                revision);
        if (decision.Action == NeonLetterClientApplyAction.Ignored)
        {
            return;
        }

        SnapshotBatchCoordinator.RecordLiveColor(networkId.PackedValue);
        ReplicatedState.Receive(
            networkId.PackedValue,
            decision.Color,
            Time.realtimeSinceStartupAsDouble,
            IsLiveLetterIdentityCallback,
            ApplyReplicatedColorCallback);
    }

    private static void HandleSnapshotRequest(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!IsAcceptedClient(fromConnection))
        {
            return;
        }

        byte version = packet.ReadByte();
        ulong requestId = packet.ReadULong();
        if (version != NeonLetterSnapshotProtocol.ProtocolVersion ||
            requestId == 0)
        {
            return;
        }

        SnapshotSendCoordinator.Stage(
            fromConnection,
            requestId,
            FreezeAuthoritativeSnapshotCallback);
    }

    private static NeonLetterSnapshotEntry[] FreezeAuthoritativeSnapshot()
    {
        IReadOnlyList<KeyValuePair<ulong, NeonRgba>> snapshot =
            AuthoritativeColors.Snapshot(identity =>
            {
                return TryResolveLiveLetterIdentity(
                    identity,
                    out _,
                    out _,
                    out _);
            });
        if (snapshot.Count >
            NeonLetterSnapshotProtocol.MaxSnapshotEntries)
        {
            throw new InvalidOperationException(
                $"Authoritative color snapshot has {snapshot.Count} " +
                $"entries; maximum is " +
                $"{NeonLetterSnapshotProtocol.MaxSnapshotEntries}.");
        }

        var frozenSnapshot = new NeonLetterSnapshotEntry[snapshot.Count];
        for (int index = 0; index < snapshot.Count; index++)
        {
            KeyValuePair<ulong, NeonRgba> entry = snapshot[index];
            frozenSnapshot[index] = new NeonLetterSnapshotEntry(
                entry.Key,
                entry.Value);
        }

        return frozenSnapshot;
    }

    private static void HandleSnapshotFrame(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!ClientSession.IsAccepted || !IsStateFromHost(fromConnection))
        {
            return;
        }

        byte version = packet.ReadByte();
        var kind = (NeonLetterSnapshotSendFrameKind)packet.ReadByte();
        ulong requestId = packet.ReadULong();
        try
        {
            switch (kind)
            {
                case NeonLetterSnapshotSendFrameKind.Begin:
                    if (SnapshotBatchCoordinator.TryBegin(
                        version,
                        requestId,
                        packet.ReadInt()))
                    {
                        SnapshotRequestScheduler.DeferRetryForProgress(
                            Time.realtimeSinceStartupAsDouble);
                    }

                    break;
                case NeonLetterSnapshotSendFrameKind.Entry:
                    int index = packet.ReadInt();
                    NetworkId networkId = packet.ReadNetworkId();
                    uint packedColor = packet.ReadUInt();
                    if (SnapshotBatchCoordinator.TryAcceptEntry(
                        version,
                        requestId,
                        index,
                        networkId.PackedValue,
                        packedColor))
                    {
                        SnapshotRequestScheduler.DeferRetryForProgress(
                            Time.realtimeSinceStartupAsDouble);
                    }

                    break;
                case NeonLetterSnapshotSendFrameKind.Complete:
                    SnapshotBatchCoordinator.TryComplete(
                        version,
                        requestId,
                        packet.ReadInt(),
                        PublishSnapshotBatch,
                        SnapshotRequestScheduler.Complete);
                    break;
                default:
                    SnapshotBatchCoordinator.RejectMalformedFrame(requestId);
                    break;
            }
        }
        catch
        {
            SnapshotBatchCoordinator.RejectMalformedFrame(requestId);
            throw;
        }
    }

    private static void PublishSnapshotBatch(
        IReadOnlyList<NeonLetterSnapshotEntry> entries)
    {
        if (!ClientSession.IsAccepted)
        {
            return;
        }

        foreach (NeonLetterSnapshotEntry entry in entries)
        {
            ClientApplyCoordinator.SeedAuthoritative(
                entry.Identity,
                entry.Color);
        }

        ReplicatedState.ReceiveBatch(
            entries,
            Time.realtimeSinceStartupAsDouble,
            GetSnapshotEntryIdentityCallback,
            GetSnapshotEntryColorCallback,
            IsLiveLetterIdentityCallback,
            ApplyReplicatedColorCallback);
    }

    private static void DrainSnapshotSendJobs()
    {
        if (!BoltNetwork.isRunning ||
            !NetUtils.IsServer ||
            SnapshotSendCoordinator.PendingJobCount == 0)
        {
            return;
        }

        SnapshotSendCoordinator.Advance(
            NeonLetterSnapshotProtocol.MaxSendFramesPerUpdate,
            TrySendSnapshotFrame,
            LogSnapshotFreezeError);
    }

    private static void LogSnapshotFreezeError(
        BoltConnection connection,
        ulong requestId,
        Exception exception)
    {
        RLog.Error(
            $"[SOTFNeonLetters] Failed to freeze {SnapshotFrame.Id} " +
            $"request {requestId} for {connection}: {exception}");
    }

    private static bool TrySendSnapshotFrame(
        BoltConnection connection,
        NeonLetterSnapshotSendFrame frame)
    {
        if (!IsAcceptedClient(connection))
        {
            return false;
        }

        try
        {
            switch (frame.Kind)
            {
                case NeonLetterSnapshotSendFrameKind.Begin:
                    SnapshotFrame.SendBegin(
                        frame.RequestId,
                        frame.Count,
                        connection);
                    break;
                case NeonLetterSnapshotSendFrameKind.Entry:
                    SnapshotFrame.SendEntry(
                        frame.RequestId,
                        frame.Index,
                        new NetworkId(frame.Entry.Identity),
                        frame.Entry.Color,
                        connection);
                    break;
                case NeonLetterSnapshotSendFrameKind.Complete:
                    SnapshotFrame.SendComplete(
                        frame.RequestId,
                        frame.Count,
                        connection);
                    break;
                default:
                    return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send {SnapshotFrame.Id} " +
                $"request {frame.RequestId}: {exception}");
            return false;
        }
    }

    private static void DrainPendingColors()
    {
        if (BoltNetwork.isRunning &&
            NetUtils.IsClient &&
            !NetUtils.IsServer &&
            ClientSession.IsAccepted)
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
        SnapshotRequestScheduler.Rearm();
        SnapshotBatchCoordinator.Reset();
        SnapshotSendCoordinator.Clear();
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
        if (isLive)
        {
            try
            {
                NeonLetterColorRuntime.ApplyEmission(
                    structure.gameObject,
                    definition,
                    color);
            }
            catch (Exception exception)
            {
                isLive = false;
                RLog.Error(
                    $"[SOTFNeonLetters] Failed to apply requested color for " +
                    $"{networkId.PackedValue}: {exception}");
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
                color);
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

        ClientSession.TryRun(() => ReplicatedState.Receive(
            decision.Identity,
            decision.Color,
            Time.realtimeSinceStartupAsDouble,
            IsLiveLetterIdentityCallback,
            ApplyReplicatedColorCallback));
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

        uint packedColor = NeonLetterNetworkProtocol.Pack(color);
        NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            packedColor);
        NeonLetterColorRuntime.ApplyEmission(
            structure.gameObject,
            definition,
            canonicalColor);

        NeonLetterColorAcceptance acceptance = AuthoritativeColors.TryAccept(
            isHost: true,
            identity: networkId.PackedValue,
            isLive: true,
            recipeId: definition.RecipeId,
            canonicalColor);
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
        DeferredDisconnects.Schedule(connection);
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
