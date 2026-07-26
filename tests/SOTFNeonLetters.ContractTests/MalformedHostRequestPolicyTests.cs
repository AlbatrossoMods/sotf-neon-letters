using SOTFNeonLetters;
using Xunit;

public sealed class MalformedHostRequestPolicyTests
{
    [Fact]
    public void AcceptedPeerIsRejectedQuarantinedAndLoggedOnlyOnce()
    {
        var peer = new Peer { IsAccepted = true };
        var disconnects = new NeonLetterDeferredDisconnects<Peer>();
        var operations = new List<string>();
        int rejectionCount = 0;
        int quarantineCount = 0;
        int logCount = 0;

        void Reject(Peer candidate)
        {
            rejectionCount++;
            candidate.IsAccepted = false;
            candidate.Status =
                NeonLetterHandshakeStatus.MalformedRequest;
            operations.Add("reject");
        }

        void Quarantine(Peer candidate)
        {
            quarantineCount++;
            disconnects.Schedule(candidate);
            operations.Add("quarantine");
        }

        void LogFailure()
        {
            logCount++;
            operations.Add("log");
        }

        NeonLetterMalformedHostRequestPolicy.RejectAcceptedPeer(
            peer,
            candidate => candidate.IsAccepted,
            Reject,
            Quarantine,
            LogFailure);
        NeonLetterMalformedHostRequestPolicy.RejectAcceptedPeer(
            peer,
            candidate => candidate.IsAccepted,
            Reject,
            Quarantine,
            LogFailure);

        Assert.Equal(
            (
                IsAccepted: false,
                Status: NeonLetterHandshakeStatus.MalformedRequest,
                IsQuarantined: true,
                DisconnectCount: 1,
                RejectionCount: 1,
                QuarantineCount: 1,
                LogCount: 1,
                Operations: "reject,quarantine,log"),
            (
                IsAccepted: peer.IsAccepted,
                Status: peer.Status,
                IsQuarantined: disconnects.IsQuarantined(peer),
                DisconnectCount: disconnects.Count,
                RejectionCount: rejectionCount,
                QuarantineCount: quarantineCount,
                LogCount: logCount,
                Operations: string.Join(",", operations)));
    }

    [Fact]
    public void NonAcceptedPeerDoesNotRunMalformedRequestActions()
    {
        var peer = new Peer();
        int actionCount = 0;

        NeonLetterMalformedHostRequestPolicy.RejectAcceptedPeer(
            peer,
            candidate => candidate.IsAccepted,
            _ => actionCount++,
            _ => actionCount++,
            () => actionCount++);

        Assert.Equal((false, 0), (peer.IsAccepted, actionCount));
    }

    [Fact]
    public void LoggingFailureDoesNotEscapeAfterPeerIsRejectedAndQuarantined()
    {
        var peer = new Peer { IsAccepted = true };
        var disconnects = new NeonLetterDeferredDisconnects<Peer>();
        int logCount = 0;

        Exception? error = Record.Exception(
            () => NeonLetterMalformedHostRequestPolicy.RejectAcceptedPeer(
                peer,
                candidate => candidate.IsAccepted,
                candidate => candidate.IsAccepted = false,
                disconnects.Schedule,
                () =>
                {
                    logCount++;
                    throw new InvalidOperationException("logging failed");
                }));

        Assert.Equal(
            (Completed: true, Rejected: true, Quarantined: true, LogCount: 1),
            (
                Completed: error is null,
                Rejected: !peer.IsAccepted,
                Quarantined: disconnects.IsQuarantined(peer),
                LogCount: logCount));
    }

    [Fact]
    public void NullAcceptedPredicateIsRejected()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(
            () => NeonLetterMalformedHostRequestPolicy.RejectAcceptedPeer(
                new Peer(),
                isAccepted: null,
                Reject,
                Quarantine,
                LogFailure));

        Assert.Equal("isAccepted", error.ParamName);
    }

    [Fact]
    public void NullRejectActionIsRejected()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(
            () => NeonLetterMalformedHostRequestPolicy.RejectAcceptedPeer(
                new Peer(),
                IsAccepted,
                reject: null,
                Quarantine,
                LogFailure));

        Assert.Equal("reject", error.ParamName);
    }

    [Fact]
    public void NullQuarantineActionIsRejected()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(
            () => NeonLetterMalformedHostRequestPolicy.RejectAcceptedPeer(
                new Peer(),
                IsAccepted,
                Reject,
                quarantine: null,
                LogFailure));

        Assert.Equal("quarantine", error.ParamName);
    }

    [Fact]
    public void NullLogActionIsRejected()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(
            () => NeonLetterMalformedHostRequestPolicy.RejectAcceptedPeer(
                new Peer(),
                IsAccepted,
                Reject,
                Quarantine,
                logFailure: null));

        Assert.Equal("logFailure", error.ParamName);
    }

    private static bool IsAccepted(Peer peer)
    {
        return peer.IsAccepted;
    }

    private static void Reject(Peer peer)
    {
        peer.IsAccepted = false;
    }

    private static void Quarantine(Peer peer)
    {
    }

    private static void LogFailure()
    {
    }

    private sealed class Peer
    {
        internal bool IsAccepted { get; set; }
        internal NeonLetterHandshakeStatus Status { get; set; }
    }
}
