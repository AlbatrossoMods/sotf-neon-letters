using Bolt;
using RedLoader;
using Sons.Crafting.Structures;
using SonsSdk;
using SonsSdk.Networking;
using UdpKit;
using UnityEngine;

namespace SOTFNeonLetters;

internal static class NeonLetterMultiplayerRuntime
{
    private const string ChangeRequestEventId =
        "SOTFNeonLetters.ColorChangeRequest.v1";
    private const string ColorStateEventId =
        "SOTFNeonLetters.ColorState.v1";
    private const string SnapshotRequestEventId =
        "SOTFNeonLetters.ColorSnapshotRequest.v2";
    private const string SnapshotFrameEventId =
        "SOTFNeonLetters.ColorSnapshotFrame.v1";
    private const int MaxPendingColorItemsPerTick = 16;
    private const double PendingColorLifetimeSeconds = 15d;
    private static readonly NeonLetterAuthoritativeColors<ulong> AuthoritativeColors =
        new();
    private static readonly NeonLetterReplicatedColorState<ulong> ReplicatedState =
        new(
            NeonLetterSnapshotProtocol.MaxSnapshotEntries,
            PendingColorLifetimeSeconds);
    private static readonly ColorChangeRequestEvent ChangeRequest = new();
    private static readonly ColorStateEvent ColorState = new();
    private static readonly ColorSnapshotRequestEvent SnapshotRequest = new();
    private static readonly ColorSnapshotFrameEvent SnapshotFrame = new();
    private static readonly NeonLetterSnapshotRequestScheduler
        SnapshotRequestScheduler = new();
    private static readonly NeonLetterSnapshotBatchCoordinator
        SnapshotBatchCoordinator = new();
    private static readonly NeonLetterSnapshotSendCoordinator<BoltConnection>
        SnapshotSendCoordinator = new();
    private static readonly NeonLetterLifecycleCoordinator Lifecycle = new();
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
    private static bool _changeRequestRegistered;
    private static bool _colorStateRegistered;
    private static bool _snapshotRequestRegistered;
    private static bool _snapshotFrameRegistered;
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
        ReplicatedState.Clear();
        SnapshotRequestScheduler.Rearm();
        SnapshotBatchCoordinator.Reset();
        SnapshotSendCoordinator.Clear();
    }

    private static void RegisterHandlersForCurrentRole()
    {
        if (!BoltNetwork.isRunning)
        {
            UnregisterRoleHandlers();
            return;
        }

        if (NetUtils.IsServer)
        {
            // Unknown event IDs are dropped before Packets.HandlePacket relays
            // them, so client-bound events stay unregistered on the host.
            UnregisterColorState();
            UnregisterSnapshotFrame();
            try
            {
                RegisterChangeRequest();
                RegisterSnapshotRequest();
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
            UnregisterChangeRequest();
            UnregisterSnapshotRequest();
            try
            {
                RegisterColorState();
                RegisterSnapshotFrame();
                RequestSnapshot();
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
        UnregisterRoleHandler(UnregisterChangeRequest);
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

            ChangeRequest.SendRequest(networkId, color);
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
            ? ReplicatedState.Resolve(identity)
            : NeonRgba.ProjectCyan;
    }

    internal static void RemoveDismantledColor(ulong networkIdentity)
    {
        AuthoritativeColors.Remove(networkIdentity);
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
        if (!BoltNetwork.isRunning || !NetUtils.IsServer || fromConnection == null)
        {
            return;
        }

        byte version = packet.ReadByte();
        NetworkId networkId = packet.ReadNetworkId();
        NeonRgba color = NeonLetterNetworkProtocol.Unpack(
            version,
            packet.ReadUInt());
        AcceptHostColor(networkId, color);
    }

    private static void HandleColorState(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!IsStateFromHost(fromConnection))
        {
            return;
        }

        byte version = packet.ReadByte();
        NetworkId networkId = packet.ReadNetworkId();
        NeonRgba color = NeonLetterNetworkProtocol.Unpack(
            version,
            packet.ReadUInt());
        if (networkId.IsZero)
        {
            throw new InvalidOperationException(
                "A neon letter color state cannot use a zero network identity.");
        }

        SnapshotBatchCoordinator.RecordLiveColor(networkId.PackedValue);
        ReplicatedState.Receive(
            networkId.PackedValue,
            color,
            Time.realtimeSinceStartupAsDouble,
            IsLiveLetterIdentityCallback,
            ApplyReplicatedColorCallback);
    }

    private static void HandleSnapshotRequest(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!BoltNetwork.isRunning ||
            !NetUtils.IsServer ||
            fromConnection == null)
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
        if (!IsStateFromHost(fromConnection))
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
        if (!BoltNetwork.isRunning ||
            !NetUtils.IsClient ||
            NetUtils.IsServer ||
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
        ReplicatedState.Clear();
        SnapshotRequestScheduler.Rearm();
        SnapshotBatchCoordinator.Reset();
        SnapshotSendCoordinator.Clear();
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
            ColorState.SendToClients(networkId, acceptance.AuthoritativeColor);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to broadcast {ColorState.Id}: {exception}");
        }

        return true;
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

    private sealed class ColorChangeRequestEvent : Packets.NetEvent
    {
        public override string Id => ChangeRequestEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleColorChangeRequest(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
            }
        }

        public void SendRequest(NetworkId networkId, NeonRgba color)
        {
            Packets.EventPacket packet = NewPacket(64, GlobalTargets.OnlyServer);
            WriteColorPayload(packet.Packet, networkId, color);
            Send(packet);
        }
    }

    private sealed class ColorStateEvent : Packets.NetEvent
    {
        public override string Id => ColorStateEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleColorState(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
            }
        }

        public void SendToClients(NetworkId networkId, NeonRgba color)
        {
            Packets.EventPacket packet = NewPacket(64, GlobalTargets.AllClients);
            WriteColorPayload(packet.Packet, networkId, color);
            Send(packet);
        }

    }

    private sealed class ColorSnapshotRequestEvent : Packets.NetEvent
    {
        public override string Id => SnapshotRequestEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleSnapshotRequest(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
            }
        }

        public void SendRequest(ulong requestId)
        {
            Packets.EventPacket packet = NewPacket(64, GlobalTargets.OnlyServer);
            packet.Packet.WriteByte(
                NeonLetterSnapshotProtocol.ProtocolVersion);
            packet.Packet.WriteULong(requestId);
            Send(packet);
        }
    }

    private sealed class ColorSnapshotFrameEvent : Packets.NetEvent
    {
        public override string Id => SnapshotFrameEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleSnapshotFrame(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
            }
        }

        public void SendBegin(
            ulong requestId,
            int count,
            BoltConnection connection)
        {
            Packets.EventPacket packet = NewFramePacket(
                NeonLetterSnapshotSendFrameKind.Begin,
                requestId,
                connection);
            packet.Packet.WriteInt(count);
            Send(packet);
        }

        public void SendEntry(
            ulong requestId,
            int index,
            NetworkId networkId,
            NeonRgba color,
            BoltConnection connection)
        {
            Packets.EventPacket packet = NewFramePacket(
                NeonLetterSnapshotSendFrameKind.Entry,
                requestId,
                connection);
            packet.Packet.WriteInt(index);
            packet.Packet.WriteNetworkId(networkId);
            packet.Packet.WriteUInt(NeonLetterNetworkProtocol.Pack(color));
            Send(packet);
        }

        public void SendComplete(
            ulong requestId,
            int count,
            BoltConnection connection)
        {
            Packets.EventPacket packet = NewFramePacket(
                NeonLetterSnapshotSendFrameKind.Complete,
                requestId,
                connection);
            packet.Packet.WriteInt(count);
            Send(packet);
        }

        private Packets.EventPacket NewFramePacket(
            NeonLetterSnapshotSendFrameKind kind,
            ulong requestId,
            BoltConnection connection)
        {
            Packets.EventPacket packet = NewPacket(64, connection);
            packet.Packet.WriteByte(
                NeonLetterSnapshotProtocol.ProtocolVersion);
            packet.Packet.WriteByte((byte)kind);
            packet.Packet.WriteULong(requestId);
            return packet;
        }
    }

    private static void WriteColorPayload(
        UdpPacket packet,
        NetworkId networkId,
        NeonRgba color)
    {
        packet.WriteByte(NeonLetterNetworkProtocol.CurrentVersion);
        packet.WriteNetworkId(networkId);
        packet.WriteUInt(NeonLetterNetworkProtocol.Pack(color));
    }
}
