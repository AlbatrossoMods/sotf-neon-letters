#nullable enable

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SOTFNeonLetters;

internal static class NeonLetterSessionProtocol
{
    public const byte HandshakeVersion = 1;
    public const double HelloResendIntervalSeconds = 1d;
    public const double NegotiationTimeoutSeconds = 5d;
}

internal readonly record struct NeonLetterSha256Digest(
    uint Word0,
    uint Word1,
    uint Word2,
    uint Word3,
    uint Word4,
    uint Word5,
    uint Word6,
    uint Word7)
{
    public const int ByteCount = 32;
    public const int WordCount = 8;

    public static NeonLetterSha256Digest FromBytes(
        ReadOnlySpan<byte> digest)
    {
        if (digest.Length != ByteCount)
        {
            throw new ArgumentException(
                "A SHA-256 digest must contain exactly 32 bytes.",
                nameof(digest));
        }

        return new NeonLetterSha256Digest(
            ReadWord(digest, 0),
            ReadWord(digest, 1),
            ReadWord(digest, 2),
            ReadWord(digest, 3),
            ReadWord(digest, 4),
            ReadWord(digest, 5),
            ReadWord(digest, 6),
            ReadWord(digest, 7));
    }

    public uint GetWord(int index)
    {
        return index switch
        {
            0 => Word0,
            1 => Word1,
            2 => Word2,
            3 => Word3,
            4 => Word4,
            5 => Word5,
            6 => Word6,
            7 => Word7,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    public byte[] ToByteArray()
    {
        var bytes = new byte[ByteCount];
        for (int index = 0; index < WordCount; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                GetWord(index));
        }

        return bytes;
    }

    public override string ToString()
    {
        return Convert.ToHexString(ToByteArray());
    }

    private static uint ReadWord(ReadOnlySpan<byte> digest, int index)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(
            digest.Slice(index * sizeof(uint), sizeof(uint)));
    }
}

internal readonly record struct NeonLetterCatalogIdentityEntry(
    int CatalogIndex,
    int RecipeId,
    int CraftingNodeId,
    string AssetKey,
    string PrefabAssetName);

internal readonly record struct NeonLetterSessionIdentity(
    string ReleaseVersion,
    byte ColorProtocolVersion,
    NeonLetterSha256Digest CatalogHash,
    NeonLetterSha256Digest BundleHash)
{
    public NeonLetterSessionFingerprint ToFingerprint()
    {
        if (string.IsNullOrWhiteSpace(ReleaseVersion))
        {
            throw new InvalidOperationException(
                "The release version must not be empty.");
        }

        return new NeonLetterSessionFingerprint(
            NeonLetterSessionIdentityHasher.ComputeReleaseVersionHash(
                ReleaseVersion),
            ColorProtocolVersion,
            CatalogHash,
            BundleHash);
    }
}

internal readonly record struct NeonLetterSessionFingerprint(
    NeonLetterSha256Digest ReleaseVersionHash,
    byte ColorProtocolVersion,
    NeonLetterSha256Digest CatalogHash,
    NeonLetterSha256Digest BundleHash);

internal static class NeonLetterSessionIdentityHasher
{
    public static NeonLetterSha256Digest ComputeReleaseVersionHash(
        string releaseVersion)
    {
        if (string.IsNullOrWhiteSpace(releaseVersion))
        {
            throw new ArgumentException(
                "The release version must not be empty.",
                nameof(releaseVersion));
        }

        return NeonLetterSha256Digest.FromBytes(
            SHA256.HashData(Encoding.UTF8.GetBytes(releaseVersion)));
    }

    public static NeonLetterSha256Digest ComputeCatalogHash(
        IReadOnlyList<NeonLetterCatalogIdentityEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        using IncrementalHash hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        AppendInt32(hash, entries.Count);
        foreach (NeonLetterCatalogIdentityEntry entry in entries)
        {
            AppendInt32(hash, entry.CatalogIndex);
            AppendInt32(hash, entry.RecipeId);
            AppendInt32(hash, entry.CraftingNodeId);
            AppendString(hash, entry.AssetKey);
            AppendString(hash, entry.PrefabAssetName);
        }

        return NeonLetterSha256Digest.FromBytes(hash.GetHashAndReset());
    }

    public static NeonLetterSha256Digest ComputeBundleHash(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException(
                "The bundle stream must be readable.",
                nameof(stream));
        }

        using var sha256 = SHA256.Create();
        return NeonLetterSha256Digest.FromBytes(sha256.ComputeHash(stream));
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount = Encoding.UTF8.GetByteCount(value);
        AppendInt32(hash, byteCount);
        if (byteCount == 0)
        {
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(bytes);
    }
}

[Flags]
internal enum NeonLetterHandshakeStatus : byte
{
    Accepted = 0,
    ReleaseVersionMismatch = 1 << 0,
    ColorProtocolMismatch = 1 << 1,
    CatalogMismatch = 1 << 2,
    BundleMismatch = 1 << 3,
    MissingHello = 1 << 4,
    MalformedHello = 1 << 5
}

internal enum NeonLetterPeerState : byte
{
    Unknown,
    Accepted,
    Rejected
}

internal sealed class NeonLetterClientSessionGate
{
    public ulong Epoch { get; private set; }
    public bool IsAccepted { get; private set; }

    public void BeginSession(
        Action clearReplicatedState,
        Action clearApplyState)
    {
        ArgumentNullException.ThrowIfNull(clearReplicatedState);
        ArgumentNullException.ThrowIfNull(clearApplyState);
        if (Epoch == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The client multiplayer session epoch space is exhausted.");
        }

        Epoch++;
        Clear(clearReplicatedState, clearApplyState);
    }

    public void Clear(
        Action clearReplicatedState,
        Action clearApplyState)
    {
        ArgumentNullException.ThrowIfNull(clearReplicatedState);
        ArgumentNullException.ThrowIfNull(clearApplyState);
        IsAccepted = false;
        try
        {
            clearReplicatedState();
        }
        finally
        {
            clearApplyState();
        }
    }

    public void Accept()
    {
        IsAccepted = true;
    }

    public void Reject()
    {
        IsAccepted = false;
    }

    public bool TryRun(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsAccepted)
        {
            return false;
        }

        action();
        return true;
    }
}

internal readonly record struct NeonLetterHandshakeHello(
    ulong HelloId,
    NeonLetterSessionFingerprint Fingerprint)
{
    public static NeonLetterHandshakeHello Create(
        ulong helloId,
        NeonLetterSessionIdentity identity)
    {
        return new NeonLetterHandshakeHello(
            helloId,
            identity.ToFingerprint());
    }
}

internal sealed class NeonLetterHandshakeRegistry<TPeer>
    where TPeer : notnull
{
    private readonly NeonLetterSessionFingerprint _expected;
    private readonly double _timeoutSeconds;
    private readonly Dictionary<TPeer, PeerEntry> _peers = new();

    public NeonLetterHandshakeRegistry(
        NeonLetterSessionIdentity expected,
        double timeoutSeconds)
    {
        ValidatePositiveFinite(timeoutSeconds, nameof(timeoutSeconds));
        _expected = expected.ToFingerprint();
        _timeoutSeconds = timeoutSeconds;
    }

    public int Count => _peers.Count;

    public void Observe(TPeer peer, double nowSeconds)
    {
        ValidateNow(nowSeconds);
        _peers.TryAdd(
            peer,
            new PeerEntry(
                nowSeconds,
                NeonLetterPeerState.Unknown,
                NeonLetterHandshakeStatus.MissingHello));
    }

    public NeonLetterHandshakeStatus AcceptHello(
        TPeer peer,
        NeonLetterHandshakeHello hello)
    {
        if (!_peers.TryGetValue(peer, out PeerEntry entry))
        {
            entry = new PeerEntry(
                0d,
                NeonLetterPeerState.Unknown,
                NeonLetterHandshakeStatus.MissingHello);
        }

        if (entry.State != NeonLetterPeerState.Unknown)
        {
            return entry.Status;
        }

        NeonLetterHandshakeStatus status = hello.HelloId == 0
            ? NeonLetterHandshakeStatus.MalformedHello
            : Compare(hello.Fingerprint);
        NeonLetterPeerState state =
            status == NeonLetterHandshakeStatus.Accepted
                ? NeonLetterPeerState.Accepted
                : NeonLetterPeerState.Rejected;
        _peers[peer] = entry with { State = state, Status = status };
        return status;
    }

    public IReadOnlyList<TPeer> RejectExpiredUnknown(double nowSeconds)
    {
        ValidateNow(nowSeconds);

        var expired = new List<TPeer>();
        foreach ((TPeer peer, PeerEntry entry) in _peers)
        {
            if (entry.State != NeonLetterPeerState.Unknown ||
                nowSeconds - entry.ObservedAtSeconds < _timeoutSeconds)
            {
                continue;
            }

            _peers[peer] = entry with
            {
                State = NeonLetterPeerState.Rejected,
                Status = NeonLetterHandshakeStatus.MissingHello
            };
            expired.Add(peer);
        }

        return expired;
    }

    public void Reject(
        TPeer peer,
        NeonLetterHandshakeStatus status)
    {
        if (status == NeonLetterHandshakeStatus.Accepted)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                "A rejection must include a failure status.");
        }

        PeerEntry entry = _peers.TryGetValue(peer, out PeerEntry existing)
            ? existing
            : new PeerEntry(
                ObservedAtSeconds: 0d,
                NeonLetterPeerState.Unknown,
                NeonLetterHandshakeStatus.MissingHello);
        _peers[peer] = entry with
        {
            State = NeonLetterPeerState.Rejected,
            Status = status
        };
    }

    public NeonLetterPeerState GetState(TPeer peer)
    {
        return _peers.TryGetValue(peer, out PeerEntry entry)
            ? entry.State
            : NeonLetterPeerState.Unknown;
    }

    public NeonLetterHandshakeStatus GetStatus(TPeer peer)
    {
        return _peers.TryGetValue(peer, out PeerEntry entry)
            ? entry.Status
            : NeonLetterHandshakeStatus.MissingHello;
    }

    public bool IsAccepted(TPeer peer)
    {
        return GetState(peer) == NeonLetterPeerState.Accepted;
    }

    public void Remove(TPeer peer)
    {
        _peers.Remove(peer);
    }

    public void Clear()
    {
        _peers.Clear();
    }

    private NeonLetterHandshakeStatus Compare(
        NeonLetterSessionFingerprint actual)
    {
        NeonLetterHandshakeStatus status = NeonLetterHandshakeStatus.Accepted;
        if (actual.ReleaseVersionHash != _expected.ReleaseVersionHash)
        {
            status |= NeonLetterHandshakeStatus.ReleaseVersionMismatch;
        }

        if (actual.ColorProtocolVersion != _expected.ColorProtocolVersion)
        {
            status |= NeonLetterHandshakeStatus.ColorProtocolMismatch;
        }

        if (actual.CatalogHash != _expected.CatalogHash)
        {
            status |= NeonLetterHandshakeStatus.CatalogMismatch;
        }

        if (actual.BundleHash != _expected.BundleHash)
        {
            status |= NeonLetterHandshakeStatus.BundleMismatch;
        }

        return status;
    }

    private static void ValidateNow(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }
    }

    private static void ValidatePositiveFinite(double value, string paramName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    private readonly record struct PeerEntry(
        double ObservedAtSeconds,
        NeonLetterPeerState State,
        NeonLetterHandshakeStatus Status);
}

internal sealed class NeonLetterHelloScheduler
{
    private readonly double _resendIntervalSeconds;
    private readonly double _timeoutSeconds;
    private double _startedAtSeconds;
    private double _lastSentAtSeconds;
    private bool _started;
    private bool _hasSent;

    public NeonLetterHelloScheduler(
        double resendIntervalSeconds,
        double timeoutSeconds)
    {
        ValidatePositiveFinite(
            resendIntervalSeconds,
            nameof(resendIntervalSeconds));
        ValidatePositiveFinite(timeoutSeconds, nameof(timeoutSeconds));
        if (resendIntervalSeconds >= timeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resendIntervalSeconds),
                "The resend interval must be shorter than the timeout.");
        }

        _resendIntervalSeconds = resendIntervalSeconds;
        _timeoutSeconds = timeoutSeconds;
    }

    public void Start(double nowSeconds)
    {
        ValidateNow(nowSeconds);
        _startedAtSeconds = nowSeconds;
        _lastSentAtSeconds = nowSeconds;
        _started = true;
        _hasSent = false;
    }

    public bool ShouldSend(double nowSeconds)
    {
        ValidateNow(nowSeconds);
        return _started &&
            !HasTimedOut(nowSeconds) &&
            (!_hasSent ||
                nowSeconds - _lastSentAtSeconds >= _resendIntervalSeconds);
    }

    public void MarkSent(double nowSeconds)
    {
        ValidateNow(nowSeconds);
        if (!_started)
        {
            throw new InvalidOperationException(
                "The hello scheduler has not started.");
        }

        _lastSentAtSeconds = nowSeconds;
        _hasSent = true;
    }

    public bool HasTimedOut(double nowSeconds)
    {
        ValidateNow(nowSeconds);
        return _started &&
            nowSeconds - _startedAtSeconds >= _timeoutSeconds;
    }

    public void Clear()
    {
        _started = false;
        _hasSent = false;
        _startedAtSeconds = 0d;
        _lastSentAtSeconds = 0d;
    }

    private static void ValidateNow(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }
    }

    private static void ValidatePositiveFinite(double value, string paramName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }
}

internal static class NeonLetterPeerDelivery
{
    public static void Deliver<TPeer>(
        IEnumerable<TPeer> peers,
        Func<TPeer, bool> isAccepted,
        Action<TPeer> send,
        Action<TPeer, Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(peers);
        ArgumentNullException.ThrowIfNull(isAccepted);
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(onFailure);

        foreach (TPeer peer in peers)
        {
            if (!isAccepted(peer))
            {
                continue;
            }

            try
            {
                send(peer);
            }
            catch (Exception exception)
            {
                onFailure(peer, exception);
            }
        }
    }
}

internal sealed class NeonLetterDeferredDisconnects<TKey>
    where TKey : notnull
{
    public const int InitialRetryDelayUpdates = 1;
    public const int MaxRetryDelayUpdates = 64;

    private readonly Dictionary<
        TKey,
        LinkedListNode<PendingDisconnect>> _byKey = new();
    private readonly LinkedList<PendingDisconnect> _pending = new();

    public int Count => _pending.Count;

    public void Schedule(TKey key)
    {
        if (_byKey.ContainsKey(key))
        {
            return;
        }

        LinkedListNode<PendingDisconnect> node =
            _pending.AddLast(new PendingDisconnect(key));
        _byKey.Add(key, node);
    }

    public bool IsQuarantined(TKey key)
    {
        return _byKey.ContainsKey(key);
    }

    public bool AllowsAcceptedTraffic(
        TKey key,
        Func<TKey, bool> isAccepted)
    {
        ArgumentNullException.ThrowIfNull(isAccepted);
        return !IsQuarantined(key) && isAccepted(key);
    }

    public void Remove(TKey key)
    {
        if (_byKey.TryGetValue(
                key,
                out LinkedListNode<PendingDisconnect>? node))
        {
            RemoveNode(node);
        }
    }

    public void Clear()
    {
        _byKey.Clear();
        _pending.Clear();
    }

    public void Drain(
        Func<TKey, bool> exists,
        Action<TKey> execute,
        Action<TKey, Exception> onFirstFailure)
    {
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(onFirstFailure);

        LinkedListNode<PendingDisconnect>? node = _pending.First;
        while (node != null)
        {
            LinkedListNode<PendingDisconnect>? next = node.Next;
            PendingDisconnect pending = node.Value;
            if (!exists(pending.Key))
            {
                RemoveNode(node);
                node = next;
                continue;
            }

            if (pending.UpdatesUntilAttempt > 0)
            {
                pending.UpdatesUntilAttempt--;
                node.Value = pending;
                node = next;
                continue;
            }

            try
            {
                execute(pending.Key);
                RemoveNode(node);
            }
            catch (Exception exception)
            {
                pending.UpdatesUntilAttempt =
                    pending.RetryDelayUpdates - 1;
                pending.RetryDelayUpdates = Math.Min(
                    pending.RetryDelayUpdates * 2,
                    MaxRetryDelayUpdates);
                bool reportFailure = !pending.FailureReported;
                pending.FailureReported = true;
                node.Value = pending;
                if (reportFailure)
                {
                    onFirstFailure(pending.Key, exception);
                }
            }

            node = next;
        }
    }

    private void RemoveNode(LinkedListNode<PendingDisconnect> node)
    {
        _pending.Remove(node);
        _byKey.Remove(node.Value.Key);
    }

    private struct PendingDisconnect
    {
        public PendingDisconnect(TKey key)
        {
            Key = key;
            RetryDelayUpdates = InitialRetryDelayUpdates;
            UpdatesUntilAttempt = 0;
            FailureReported = false;
        }

        public TKey Key { get; }
        public int RetryDelayUpdates { get; set; }
        public int UpdatesUntilAttempt { get; set; }
        public bool FailureReported { get; set; }
    }
}

internal sealed class NeonLetterRoleSetupGate
{
    public bool IsFailed { get; private set; }

    public Exception? TryRun(Action setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (IsFailed)
        {
            return null;
        }

        try
        {
            setup();
            return null;
        }
        catch (Exception exception)
        {
            IsFailed = true;
            return exception;
        }
    }

    public void Reset()
    {
        IsFailed = false;
    }
}
