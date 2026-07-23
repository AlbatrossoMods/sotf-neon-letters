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
        "SOTFNeonLetters.ColorSnapshotRequest.v1";
    private const int PendingColorCapacity = 128;
    private const double PendingColorLifetimeSeconds = 15d;
    private static readonly NeonLetterAuthoritativeColors<ulong> AuthoritativeColors =
        new();
    private static readonly NeonLetterReplicatedColorState<ulong> ReplicatedState =
        new(PendingColorCapacity, PendingColorLifetimeSeconds);
    private static readonly ColorChangeRequestEvent ChangeRequest = new();
    private static readonly ColorStateEvent ColorState = new();
    private static readonly ColorSnapshotRequestEvent SnapshotRequest = new();
    private static bool _changeRequestRegistered;
    private static bool _colorStateRegistered;
    private static bool _snapshotRequestRegistered;
    private static bool _snapshotRequested;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        SdkEvents.OnAfterSpawn.Subscribe(RegisterHandlersForCurrentRole);
        SdkEvents.OnInWorldUpdate.Subscribe(DrainPendingColors);
        SdkEvents.OnWorldExited.Subscribe(OnWorldExited);
        _initialized = true;
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
            // them, so ColorState must remain unregistered on the host.
            UnregisterColorState();
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
        UnregisterChangeRequest();
        UnregisterColorState();
        UnregisterSnapshotRequest();
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

    private static void UnregisterChangeRequest()
    {
        if (!_changeRequestRegistered)
        {
            return;
        }

        Packets.Unregister(ChangeRequest);
        _changeRequestRegistered = false;
    }

    private static void UnregisterColorState()
    {
        if (_colorStateRegistered)
        {
            Packets.Unregister(ColorState);
            _colorStateRegistered = false;
        }

        _snapshotRequested = false;
    }

    private static void UnregisterSnapshotRequest()
    {
        if (!_snapshotRequestRegistered)
        {
            return;
        }

        Packets.Unregister(SnapshotRequest);
        _snapshotRequestRegistered = false;
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
        if (_snapshotRequested ||
            !_colorStateRegistered ||
            !BoltNetwork.isRunning ||
            !NetUtils.IsClient ||
            NetUtils.IsServer)
        {
            return;
        }

        _snapshotRequested = true;
        try
        {
            SnapshotRequest.SendRequest();
        }
        catch (Exception exception)
        {
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

        ReplicatedState.Receive(
            networkId.PackedValue,
            color,
            Time.realtimeSinceStartupAsDouble,
            IsLiveLetterIdentity,
            ApplyReplicatedColor);
    }

    private static void HandleSnapshotRequest(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!BoltNetwork.isRunning || !NetUtils.IsServer || fromConnection == null)
        {
            return;
        }

        EnsureCurrentVersion(packet.ReadByte());
        var liveNetworkIds = new Dictionary<ulong, NetworkId>();
        IReadOnlyList<KeyValuePair<ulong, NeonRgba>> snapshot =
            AuthoritativeColors.Snapshot(identity =>
            {
                if (!TryResolveLiveLetterIdentity(
                        identity,
                        out NetworkId networkId,
                        out _,
                        out _))
                {
                    return false;
                }

                liveNetworkIds[identity] = networkId;
                return true;
            });
        foreach (KeyValuePair<ulong, NeonRgba> entry in snapshot)
        {
            try
            {
                ColorState.SendToConnection(
                    liveNetworkIds[entry.Key],
                    entry.Value,
                    fromConnection);
            }
            catch (Exception exception)
            {
                RLog.Error(
                    $"[SOTFNeonLetters] Failed to send {ColorState.Id} " +
                    $"snapshot entry {entry.Key}: {exception}");
            }
        }
    }

    private static void DrainPendingColors()
    {
        if (!BoltNetwork.isRunning ||
            !NetUtils.IsClient ||
            NetUtils.IsServer)
        {
            return;
        }

        try
        {
            ReplicatedState.DrainReady(
                Time.realtimeSinceStartupAsDouble,
                IsLiveLetterIdentity,
                ApplyReplicatedColor,
                (identity, exception) =>
                    RLog.Error(
                        $"[SOTFNeonLetters] Failed to apply pending " +
                        $"{ColorState.Id} for network identity {identity}: " +
                        exception));
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
        _snapshotRequested = false;
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

    private static void EnsureCurrentVersion(byte version)
    {
        if (version != NeonLetterNetworkProtocol.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported neon letter multiplayer protocol version {version}.");
        }
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

        public void SendToConnection(
            NetworkId networkId,
            NeonRgba color,
            BoltConnection connection)
        {
            Packets.EventPacket packet = NewPacket(64, connection);
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

        public void SendRequest()
        {
            Packets.EventPacket packet = NewPacket(64, GlobalTargets.OnlyServer);
            packet.Packet.WriteByte(NeonLetterNetworkProtocol.CurrentVersion);
            Send(packet);
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
