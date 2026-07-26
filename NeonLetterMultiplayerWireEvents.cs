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

    private sealed class ColorPageRequestEvent : Packets.NetEvent
    {
        public override string Id => ColorPageRequestEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleColorPageRequest(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
            }
        }

        public void SendRequest(NeonLetterColorPageRequest request)
        {
            Packets.EventPacket packet = NewPacket(64, GlobalTargets.OnlyServer);
            packet.Packet.WriteByte(request.ProtocolVersion);
            packet.Packet.WriteULong(request.SyncId);
            packet.Packet.WriteULong(request.CursorChangeSerial);
            packet.Packet.WriteULong(request.WatermarkChangeSerial);
            Send(packet);
        }
    }

    private sealed class ColorPageResponseEvent : Packets.NetEvent
    {
        public override string Id => ColorPageResponseEventId;

        public override void Read(UdpPacket packet, BoltConnection fromConnection)
        {
            try
            {
                HandleColorPageResponse(packet, fromConnection);
            }
            catch (Exception exception)
            {
                LogReadFailure(Id, exception);
            }
        }

        public void SendResponse(
            NeonLetterColorPageResponse<ulong> response,
            BoltConnection connection)
        {
            if (response.ProtocolVersion !=
                    NeonLetterColorPageProtocol.ProtocolVersion ||
                response.SyncId == 0 ||
                response.Sequence == 0 ||
                response.Entries == null ||
                response.Entries.Count >
                    NeonLetterColorPageProtocol.MaxPageEntries)
            {
                throw new InvalidOperationException(
                    "The color page response is invalid.");
            }

            Packets.EventPacket packet = NewPacket(
                NeonLetterColorPageProtocol.MaxResponsePacketBytes,
                connection);
            packet.Packet.WriteByte(response.ProtocolVersion);
            packet.Packet.WriteULong(response.SyncId);
            packet.Packet.WriteULong(response.Sequence);
            packet.Packet.WriteULong(response.WatermarkChangeSerial);
            packet.Packet.WriteULong(response.NextCursorChangeSerial);
            packet.Packet.WriteInt(response.Entries.Count);
            packet.Packet.WriteByte(response.Complete ? (byte)1 : (byte)0);
            foreach (NeonLetterColorPageEntry<ulong> entry in response.Entries)
            {
                packet.Packet.WriteNetworkId(new NetworkId(entry.Identity));
                packet.Packet.WriteULong(entry.EntityRevision);
                packet.Packet.WriteUInt(
                    NeonLetterNetworkProtocol.Pack(entry.Color));
            }

            Send(packet);
        }
    }

    private struct ColorPageWireReader :
        INeonLetterColorPageWireReader<ulong>
    {
        private readonly UdpPacket _packet;

        internal ColorPageWireReader(UdpPacket packet)
        {
            ArgumentNullException.ThrowIfNull(packet);
            _packet = packet;
        }

        public bool IsFullyConsumed => _packet.Done;

        public byte ReadByte()
        {
            return _packet.ReadByte();
        }

        public ulong ReadUInt64()
        {
            return _packet.ReadULong();
        }

        public int ReadInt32()
        {
            return _packet.ReadInt();
        }

        public ulong ReadIdentity()
        {
            return _packet.ReadNetworkId().PackedValue;
        }

        public NeonRgba ReadColor(byte protocolVersion)
        {
            return NeonLetterNetworkProtocol.Unpack(
                protocolVersion,
                _packet.ReadUInt());
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
