#nullable enable

namespace SOTFNeonLetters;

public sealed class NeonLetterSnapshotRequestScheduler
{
    public const int MaximumRetries = 2;
    public const int MaximumAttempts = 1 + MaximumRetries;
    public const int MaximumSuccessfulSends = MaximumAttempts;
    public const double RetryIntervalSeconds = 2d;

    private int _totalAttemptCount;
    private int _successfulSendCount;
    private double _nextAttemptAtSeconds;
    private bool _isComplete;

    public int TotalAttemptCount => _totalAttemptCount;
    public int SuccessfulSendCount => _successfulSendCount;
    public bool CanAttempt =>
        !_isComplete && _totalAttemptCount < MaximumAttempts;

    /// <summary>
    /// Returns whether an initial snapshot request or bounded retry is due.
    /// </summary>
    public bool IsDue(double nowSeconds)
    {
        ValidateNowSeconds(nowSeconds);
        return CanAttempt && nowSeconds >= _nextAttemptAtSeconds;
    }

    /// <summary>
    /// Records a request only after the packet send completed successfully.
    /// </summary>
    public void RecordSuccessfulSend(double nowSeconds)
    {
        ValidateNowSeconds(nowSeconds);
        if (!RecordAttempt(nowSeconds))
        {
            return;
        }

        _successfulSendCount++;
    }

    /// <summary>
    /// Records a locally failed send and delays the next bounded retry.
    /// </summary>
    public void DeferRetry(double nowSeconds)
    {
        ValidateNowSeconds(nowSeconds);
        RecordAttempt(nowSeconds);
    }

    /// <summary>
    /// Re-arms immediate snapshot synchronization for a new client session.
    /// </summary>
    public void Rearm()
    {
        _totalAttemptCount = 0;
        _successfulSendCount = 0;
        _nextAttemptAtSeconds = 0d;
        _isComplete = false;
    }

    /// <summary>
    /// Stops snapshot attempts after authoritative state is received.
    /// </summary>
    public void Complete()
    {
        _isComplete = true;
    }

    private bool RecordAttempt(double nowSeconds)
    {
        if (!CanAttempt)
        {
            return false;
        }

        _totalAttemptCount++;
        if (_totalAttemptCount < MaximumAttempts)
        {
            ScheduleNextAttempt(nowSeconds);
        }

        return true;
    }

    private void ScheduleNextAttempt(double nowSeconds)
    {
        _nextAttemptAtSeconds = nowSeconds + RetryIntervalSeconds;
    }

    private static void ValidateNowSeconds(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "Snapshot request time must be finite and non-negative.");
        }
    }
}
