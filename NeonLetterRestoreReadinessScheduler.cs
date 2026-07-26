#nullable enable

namespace SOTFNeonLetters;

internal sealed class NeonLetterRestoreReadinessScheduler<TProgress>
    where TProgress : notnull
{
    internal const int InitialSafetyProbeIntervalUpdates = 8;
    internal const int MaximumSafetyProbeIntervalUpdates = 1_024;

    private readonly EqualityComparer<TProgress> _comparer =
        EqualityComparer<TProgress>.Default;
    private TProgress _observedProgress = default!;
    private bool _hasObservedProgress;
    private long _lastUpdateTick = -1;
    private long _nextSafetyProbeTick;
    private int _safetyProbeIntervalUpdates =
        InitialSafetyProbeIntervalUpdates;
    private readonly NeonLetterMonotonicSequence _tokens;

    internal NeonLetterRestoreReadinessScheduler()
        : this(new NeonLetterMonotonicSequence())
    {
    }

    internal NeonLetterRestoreReadinessScheduler(
        NeonLetterMonotonicSequence tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _tokens = tokens;
    }

    internal ulong CurrentToken { get; private set; }

    internal bool TryGetDueToken(
        TProgress observedProgress,
        long updateTick,
        bool waveActive,
        out ulong token)
    {
        if (updateTick < 0 || updateTick < _lastUpdateTick)
        {
            throw new ArgumentOutOfRangeException(nameof(updateTick));
        }

        bool repeatedUpdateTick = updateTick == _lastUpdateTick;
        _lastUpdateTick = updateTick;
        if (!_hasObservedProgress ||
            !_comparer.Equals(_observedProgress, observedProgress))
        {
            _observedProgress = observedProgress;
            _hasObservedProgress = true;
            _safetyProbeIntervalUpdates =
                InitialSafetyProbeIntervalUpdates;
            IssueToken();
            ScheduleSafetyProbe(updateTick);
            token = CurrentToken;
            return true;
        }

        if (repeatedUpdateTick)
        {
            token = CurrentToken;
            return false;
        }

        if (waveActive)
        {
            if (updateTick >= _nextSafetyProbeTick)
            {
                ScheduleSafetyProbe(updateTick);
            }

            token = CurrentToken;
            return false;
        }

        if (updateTick < _nextSafetyProbeTick)
        {
            token = CurrentToken;
            return false;
        }

        IssueToken();
        _safetyProbeIntervalUpdates = Math.Min(
            _safetyProbeIntervalUpdates * 2,
            MaximumSafetyProbeIntervalUpdates);
        ScheduleSafetyProbe(updateTick);
        token = CurrentToken;
        return true;
    }

    internal void Reset()
    {
        _observedProgress = default!;
        _hasObservedProgress = false;
        _lastUpdateTick = -1;
        _nextSafetyProbeTick = 0;
        _safetyProbeIntervalUpdates =
            InitialSafetyProbeIntervalUpdates;
        CurrentToken = 0;
    }

    private void IssueToken()
    {
        CurrentToken = _tokens.Advance();
    }

    private void ScheduleSafetyProbe(long updateTick)
    {
        _nextSafetyProbeTick =
            updateTick >
                long.MaxValue - _safetyProbeIntervalUpdates
                ? long.MaxValue
                : updateTick + _safetyProbeIntervalUpdates;
    }
}
