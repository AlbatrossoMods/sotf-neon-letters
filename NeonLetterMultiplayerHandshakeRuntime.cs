using Bolt;
using Endnight.Extensions;
using RedLoader;
using SonsSdk;
using SonsSdk.Networking;
using System.Reflection;
using UdpKit;
using UnityEngine;

namespace SOTFNeonLetters;

internal static partial class NeonLetterMultiplayerRuntime
{
    private static void AdvanceSession()
    {
        if (!BoltNetwork.isRunning)
        {
            if (_roleReady || RoleSetupGate.IsFailed)
            {
                UnregisterRoleHandlers();
                ClearSessionState();
            }

            return;
        }

        if (!_roleReady)
        {
            RegisterHandlersForCurrentRole();
            if (!_roleReady)
            {
                return;
            }
        }

        DrainDeferredDisconnects();
        double nowSeconds = Time.realtimeSinceStartupAsDouble;
        if (NetUtils.IsServer)
        {
            AdvanceHostHandshakes(nowSeconds);
        }
        else if (NetUtils.IsClient)
        {
            AdvanceClientHandshake(nowSeconds);
        }
    }

    private static void AdvanceHostHandshakes(double nowSeconds)
    {
        if (_hostHandshakes == null)
        {
            return;
        }

        var currentConnections = new HashSet<BoltConnection>();
        BoltNetwork.clients.ForEach((Action<BoltConnection>)(connection =>
        {
            currentConnections.Add(connection);
            HostConnections.Add(connection);
            _hostHandshakes.Observe(connection, nowSeconds);
        }));

        foreach (BoltConnection connection in HostConnections.ToArray())
        {
            if (currentConnections.Contains(connection))
            {
                continue;
            }

            HostConnections.Remove(connection);
            DeferredDisconnects.Remove(connection);
            _hostHandshakes.Remove(connection);
            HostApplyCoordinator.Remove(connection);
        }

        foreach (BoltConnection connection in
            _hostHandshakes.RejectExpiredUnknown(nowSeconds))
        {
            if (!HostConnections.Contains(connection))
            {
                continue;
            }

            TrySendHandshakeResult(
                helloId: 0,
                NeonLetterHandshakeStatus.MissingHello,
                connection);
            DeferredDisconnects.Schedule(connection);
            RLog.Error(
                $"[SOTFNeonLetters] Rejected multiplayer client " +
                $"{connection.ConnectionId}: handshake hello was not received within " +
                $"{NeonLetterSessionProtocol.NegotiationTimeoutSeconds:0} seconds.");
        }
    }

    private static void AdvanceClientHandshake(double nowSeconds)
    {
        if (ClientSession.IsAccepted ||
            DeferredClientDisconnects.IsQuarantined(ClientDisconnectKey))
        {
            return;
        }

        if (HelloScheduler.HasTimedOut(nowSeconds))
        {
            RLog.Error(
                $"[SOTFNeonLetters] Multiplayer handshake timed out after " +
                $"{NeonLetterSessionProtocol.NegotiationTimeoutSeconds:0} seconds.");
            DeferredClientDisconnects.Schedule(ClientDisconnectKey);
            return;
        }

        if (!HelloScheduler.ShouldSend(nowSeconds) ||
            _sessionIdentity is not NeonLetterSessionIdentity identity)
        {
            return;
        }

        try
        {
            HandshakeHello.SendHello(
                NeonLetterHandshakeHello.Create(_clientHelloId, identity));
            HelloScheduler.MarkSent(nowSeconds);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send {HandshakeHello.Id}: " +
                exception);
        }
    }

    private static void DrainDeferredDisconnects()
    {
        DeferredClientDisconnects.Drain(
            ClientDisconnectExistsCallback,
            DisconnectClientCallback,
            LogClientDisconnectFailureCallback);
        DeferredDisconnects.Drain(
            HostConnectionExistsCallback,
            DisconnectHostConnectionCallback,
            LogHostDisconnectFailureCallback);
    }

    private static bool ClientDisconnectExists(byte _)
    {
        return BoltNetwork.server != null;
    }

    private static void DisconnectClient(byte _)
    {
        BoltNetwork.server.Disconnect();
    }

    private static void LogClientDisconnectFailure(
        byte _,
        Exception exception)
    {
        try
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to disconnect rejected " +
                $"multiplayer session; retry scheduled: {exception}");
        }
        catch
        {
            // A logging failure must not cancel the deferred retry.
        }
    }

    private static void LogHostDisconnectFailure(
        BoltConnection connection,
        Exception exception)
    {
        try
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to disconnect rejected client " +
                $"{connection.ConnectionId}; retry scheduled: {exception}");
        }
        catch
        {
            // A logging failure must not block retries for later peers.
        }
    }

    private static void HandleHandshakeHello(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!BoltNetwork.isRunning ||
            !NetUtils.IsServer ||
            fromConnection == null ||
            DeferredDisconnects.IsQuarantined(fromConnection) ||
            _hostHandshakes == null)
        {
            return;
        }

        uint connectionId = fromConnection.ConnectionId;
        HostConnections.Add(fromConnection);
        _hostHandshakes.Observe(
            fromConnection,
            Time.realtimeSinceStartupAsDouble);

        byte handshakeVersion = packet.ReadByte();
        if (handshakeVersion != NeonLetterSessionProtocol.HandshakeVersion)
        {
            RejectMalformedHello(fromConnection);
            return;
        }

        ulong helloId = packet.ReadULong();
        var fingerprint = new NeonLetterSessionFingerprint(
            ReadDigest(packet),
            packet.ReadByte(),
            ReadDigest(packet),
            ReadDigest(packet));
        NeonLetterHandshakeStatus status = _hostHandshakes.AcceptHello(
            fromConnection,
            new NeonLetterHandshakeHello(helloId, fingerprint));
        TrySendHandshakeResult(helloId, status, fromConnection);
        if (status == NeonLetterHandshakeStatus.Accepted)
        {
            RLog.Msg(
                $"[SOTFNeonLetters] Accepted multiplayer client " +
                $"{connectionId} protocol handshake.");
            return;
        }

        DeferredDisconnects.Schedule(fromConnection);
        RLog.Error(
            $"[SOTFNeonLetters] Rejected multiplayer client {connectionId}: " +
            $"{FormatHandshakeStatus(status)}.");
    }

    private static void RejectMalformedHello(BoltConnection fromConnection)
    {
        uint connectionId = fromConnection.ConnectionId;
        _hostHandshakes?.Reject(
            fromConnection,
            NeonLetterHandshakeStatus.MalformedHello);
        TrySendHandshakeResult(
            helloId: 0,
            NeonLetterHandshakeStatus.MalformedHello,
            fromConnection);
        DeferredDisconnects.Schedule(fromConnection);
        RLog.Error(
            $"[SOTFNeonLetters] Rejected multiplayer client {connectionId}: " +
            "malformed handshake hello.");
    }

    private static void HandleHandshakeResult(
        UdpPacket packet,
        BoltConnection fromConnection)
    {
        if (!IsStateFromHost(fromConnection))
        {
            return;
        }

        byte version = packet.ReadByte();
        ulong helloId = packet.ReadULong();
        var status = (NeonLetterHandshakeStatus)packet.ReadByte();
        if (version != NeonLetterSessionProtocol.HandshakeVersion ||
            helloId != _clientHelloId ||
            !IsValidHandshakeStatus(status))
        {
            return;
        }

        if (status == NeonLetterHandshakeStatus.Accepted)
        {
            ClientSession.Accept();
            HelloScheduler.Clear();
            RLog.Msg(
                "[SOTFNeonLetters] Multiplayer protocol handshake accepted.");
            RequestSnapshot();
            return;
        }

        ClientSession.Reject();
        HelloScheduler.Clear();
        DeferredClientDisconnects.Schedule(ClientDisconnectKey);
        RLog.Error(
            $"[SOTFNeonLetters] Multiplayer protocol handshake rejected: " +
            $"{FormatHandshakeStatus(status)}.");
    }

    private static bool IsValidHandshakeStatus(
        NeonLetterHandshakeStatus status)
    {
        const NeonLetterHandshakeStatus allKnown =
            NeonLetterHandshakeStatus.ReleaseVersionMismatch |
            NeonLetterHandshakeStatus.ColorProtocolMismatch |
            NeonLetterHandshakeStatus.CatalogMismatch |
            NeonLetterHandshakeStatus.BundleMismatch |
            NeonLetterHandshakeStatus.MissingHello |
            NeonLetterHandshakeStatus.MalformedHello;
        return (status & ~allKnown) == 0;
    }

    private static void TrySendHandshakeResult(
        ulong helloId,
        NeonLetterHandshakeStatus status,
        BoltConnection connection)
    {
        try
        {
            HandshakeResult.SendResult(helloId, status, connection);
        }
        catch (Exception exception)
        {
            RLog.Error(
                $"[SOTFNeonLetters] Failed to send {HandshakeResult.Id} to " +
                $"{connection.ConnectionId}: {exception}");
        }
    }

    private static bool IsAcceptedClient(BoltConnection fromConnection)
    {
        return BoltNetwork.isRunning &&
            NetUtils.IsServer &&
            fromConnection != null &&
            IsAcceptedForModTraffic(fromConnection);
    }

    private static bool IsAcceptedForModTraffic(BoltConnection connection)
    {
        return DeferredDisconnects.AllowsAcceptedTraffic(
            connection,
            IsHostHandshakeAcceptedCallback);
    }

    private static bool IsHostHandshakeAccepted(BoltConnection connection)
    {
        return _hostHandshakes?.IsAccepted(connection) == true;
    }

    private static void StartClientHandshake()
    {
        if (_nextHelloId == 0)
        {
            throw new InvalidOperationException(
                "The handshake identifier space is exhausted.");
        }

        _clientHelloId = _nextHelloId++;
        HelloScheduler.Start(Time.realtimeSinceStartupAsDouble);
    }

    private static void EnsureSessionIdentity()
    {
        if (_sessionIdentity.HasValue)
        {
            return;
        }

        Assembly assembly =
            typeof(global::SOTFNeonLetters.SOTFNeonLetters).Assembly;
        string releaseVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ??
            throw new InvalidOperationException(
                "The mod release version is unavailable.");
        string assemblyDirectory = Path.GetDirectoryName(assembly.Location) ??
            throw new InvalidOperationException(
                "The installed mod directory is unavailable.");
        string bundlePath = Path.Combine(
            assemblyDirectory,
            nameof(SOTFNeonLetters),
            NeonLetterSmallCatalog.BundleName);

        var catalogEntries =
            new NeonLetterCatalogIdentityEntry[
                NeonLetterSmallCatalog.All.Count];
        for (int index = 0; index < catalogEntries.Length; index++)
        {
            NeonLetterSmallDefinition definition =
                NeonLetterSmallCatalog.All[index];
            catalogEntries[index] = new NeonLetterCatalogIdentityEntry(
                index,
                definition.RecipeId,
                definition.CraftingNodeId,
                definition.AssetKey,
                definition.PrefabAssetName);
        }

        using FileStream bundleStream = File.OpenRead(bundlePath);
        _sessionIdentity = new NeonLetterSessionIdentity(
            releaseVersion,
            NeonLetterNetworkProtocol.CurrentVersion,
            NeonLetterSessionIdentityHasher.ComputeCatalogHash(
                catalogEntries),
            NeonLetterSessionIdentityHasher.ComputeBundleHash(bundleStream));
        _hostHandshakes =
            new NeonLetterHandshakeRegistry<BoltConnection>(
            _sessionIdentity.Value,
            NeonLetterSessionProtocol.NegotiationTimeoutSeconds);
    }

    private static void BeginRoleSession()
    {
        ResetRoleSession(beginClientSession: true);
    }

    private static void ResetRoleSession()
    {
        ResetRoleSession(beginClientSession: false);
    }

    private static void ResetRoleSession(bool beginClientSession)
    {
        _hostHandshakes?.Clear();
        HostApplyCoordinator.Clear();
        if (beginClientSession)
        {
            ClientSession.BeginSession(
                ReplicatedState.Clear,
                ClientApplyCoordinator.Clear);
        }
        else
        {
            ClientSession.Clear(
                ReplicatedState.Clear,
                ClientApplyCoordinator.Clear);
        }

        HostConnections.Clear();
        DeferredDisconnects.Clear();
        DeferredClientDisconnects.Clear();
        HelloScheduler.Clear();
        _clientHelloId = 0;
        _roleReady = false;
        SnapshotRequestScheduler.Rearm();
        SnapshotBatchCoordinator.Reset();
        SnapshotSendCoordinator.Clear();
    }

    private static void ClearSessionState()
    {
        ResetRoleSession();
        _hostHandshakes = null;
        _sessionIdentity = null;
        RoleSetupGate.Reset();
    }

    private static string FormatHandshakeStatus(
        NeonLetterHandshakeStatus status)
    {
        return status switch
        {
            NeonLetterHandshakeStatus.Accepted => "accepted",
            _ => status.ToString()
        };
    }

}
