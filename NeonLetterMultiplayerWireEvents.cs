using Bolt;
using SonsSdk;
using SonsSdk.Networking;
using UdpKit;

namespace SOTFNeonLetters;

internal static partial class NeonLetterMultiplayerRuntime
{
    private sealed class HandshakeHelloEvent : Packets.NetEvent
    {
        public override string Id => HandshakeHelloEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleHandshakeHello(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
                if (fromConnection != null && NetUtils.IsServer)
                {
                    RejectMalformedHello(fromConnection);
                }
            }
        }

        public void SendHello(NeonLetterHandshakeHello hello)
        {
            Packets.EventPacket packet = NewPacket(
                128,
                GlobalTargets.OnlyServer);
            packet.Packet.WriteByte(
                NeonLetterSessionProtocol.HandshakeVersion);
            packet.Packet.WriteULong(hello.HelloId);
            WriteDigest(
                packet.Packet,
                hello.Fingerprint.ReleaseVersionHash);
            packet.Packet.WriteByte(
                hello.Fingerprint.ColorProtocolVersion);
            WriteDigest(packet.Packet, hello.Fingerprint.CatalogHash);
            WriteDigest(packet.Packet, hello.Fingerprint.BundleHash);
            Send(packet);
        }
    }

    private sealed class HandshakeResultEvent : Packets.NetEvent
    {
        public override string Id => HandshakeResultEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleHandshakeResult(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
            }
        }

        public void SendResult(
            ulong helloId,
            NeonLetterHandshakeStatus status,
            BoltConnection connection)
        {
            Packets.EventPacket packet = NewPacket(32, connection);
            packet.Packet.WriteByte(
                NeonLetterSessionProtocol.HandshakeVersion);
            packet.Packet.WriteULong(helloId);
            packet.Packet.WriteByte((byte)status);
            Send(packet);
        }
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

        public void SendRequest(
            ulong requestId,
            NetworkId networkId,
            NeonRgba color)
        {
            Packets.EventPacket packet = NewPacket(64, GlobalTargets.OnlyServer);
            packet.Packet.WriteByte(NeonLetterNetworkProtocol.CurrentVersion);
            packet.Packet.WriteULong(requestId);
            packet.Packet.WriteNetworkId(networkId);
            packet.Packet.WriteUInt(NeonLetterNetworkProtocol.Pack(color));
            Send(packet);
        }
    }

    private sealed class ColorChangeResultEvent : Packets.NetEvent
    {
        public override string Id => ChangeResultEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleColorChangeResult(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
            }
        }

        public void SendResult(
            NeonLetterApplyResult<ulong> result,
            BoltConnection connection)
        {
            Packets.EventPacket packet = NewPacket(64, connection);
            packet.Packet.WriteByte(NeonLetterNetworkProtocol.CurrentVersion);
            packet.Packet.WriteULong(result.RequestId);
            packet.Packet.WriteNetworkId(new NetworkId(result.Identity));
            packet.Packet.WriteByte((byte)result.Status);
            packet.Packet.WriteULong(result.Revision);
            packet.Packet.WriteUInt(
                NeonLetterNetworkProtocol.Pack(
                    result.AuthoritativeColor));
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

        public void SendToClient(
            NetworkId networkId,
            NeonRgba color,
            ulong revision,
            BoltConnection connection)
        {
            Packets.EventPacket packet = NewPacket(64, connection);
            packet.Packet.WriteByte(NeonLetterNetworkProtocol.CurrentVersion);
            packet.Packet.WriteNetworkId(networkId);
            packet.Packet.WriteULong(revision);
            packet.Packet.WriteUInt(NeonLetterNetworkProtocol.Pack(color));
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

    private static void WriteDigest(
        UdpPacket packet,
        NeonLetterSha256Digest digest)
    {
        for (int index = 0;
             index < NeonLetterSha256Digest.WordCount;
             index++)
        {
            packet.WriteUInt(digest.GetWord(index));
        }
    }

    private static NeonLetterSha256Digest ReadDigest(UdpPacket packet)
    {
        return new NeonLetterSha256Digest(
            packet.ReadUInt(),
            packet.ReadUInt(),
            packet.ReadUInt(),
            packet.ReadUInt(),
            packet.ReadUInt(),
            packet.ReadUInt(),
            packet.ReadUInt(),
            packet.ReadUInt());
    }
}
