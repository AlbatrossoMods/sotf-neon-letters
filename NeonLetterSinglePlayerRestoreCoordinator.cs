#nullable enable

namespace SOTFNeonLetters;

internal enum NeonLetterSinglePlayerRestoreAttemptResult
{
    Applied,
    TargetUnavailable,
    Terminal
}

internal enum NeonLetterSinglePlayerRestoreTargetObservationKind
{
    ManagerUnavailable,
    TargetUnavailable,
    Resolved
}

internal readonly record struct NeonLetterSinglePlayerRestoreTargetObservation(
    NeonLetterSinglePlayerRestoreTargetObservationKind Kind,
    INeonLetterColorRestoreTarget? Target = null,
    int? ResolvedRecipeId = null);

internal static class NeonLetterSinglePlayerRestoreAttemptPolicy
{
    internal static NeonLetterSinglePlayerRestoreAttemptResult TryApply(
        NeonLetterColorSaveEntry entry,
        NeonLetterSinglePlayerRestoreTargetObservation observation,
        Action<Exception> onApplyError)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(onApplyError);

        switch (observation.Kind)
        {
            case NeonLetterSinglePlayerRestoreTargetObservationKind
                .ManagerUnavailable:
            case NeonLetterSinglePlayerRestoreTargetObservationKind
                .TargetUnavailable:
                return NeonLetterSinglePlayerRestoreAttemptResult
                    .TargetUnavailable;

            case NeonLetterSinglePlayerRestoreTargetObservationKind.Resolved:
                try
                {
                    if (observation.Target == null ||
                        !observation.ResolvedRecipeId.HasValue ||
                        observation.ResolvedRecipeId.Value != entry.RecipeId ||
                        observation.Target.RecipeId != entry.RecipeId)
                    {
                        return NeonLetterSinglePlayerRestoreAttemptResult
                            .Terminal;
                    }

                    observation.Target.Apply(entry.Color);
                    return NeonLetterSinglePlayerRestoreAttemptResult.Applied;
                }
                catch (Exception exception)
                {
                    ReportError(onApplyError, exception);
                    return NeonLetterSinglePlayerRestoreAttemptResult.Terminal;
                }

            default:
                ReportError(
                    onApplyError,
                    new InvalidOperationException(
                        $"Unsupported single-player restore target observation " +
                        $"{observation.Kind}."));
                return NeonLetterSinglePlayerRestoreAttemptResult.Terminal;
        }
    }

    private static void ReportError(
        Action<Exception> onApplyError,
        Exception exception)
    {
        try
        {
            onApplyError(exception);
        }
        catch
        {
            // A reporting failure cannot make a terminal restore safe to retry.
        }
    }
}

internal sealed class NeonLetterSinglePlayerRestoreCoordinator
{
    internal const int MaxAttemptsPerTick = 16;

    private static readonly IReadOnlySet<int> KnownRecipeIds =
        NeonLetterSmallCatalog.All
            .Select(definition => definition.RecipeId)
            .ToHashSet();

    private readonly LinkedList<NeonLetterColorSaveEntry> _pending = new();
    private readonly Dictionary<
        int,
        LinkedListNode<NeonLetterColorSaveEntry>> _pendingBySaveId = new();
    private LinkedListNode<NeonLetterColorSaveEntry>? _nextPending;
    private double _startedAtSeconds;
    private long _epoch;
    private ulong _readinessToken;
    private int _readinessWaveAttemptsRemaining;

    internal int PendingCount => _pending.Count;

    internal long Stage(
        NeonLetterColorSaveEnvelope? envelope,
        double nowSeconds)
    {
        ValidateNowSeconds(nowSeconds);
        BeginNextEpoch();
        _startedAtSeconds = nowSeconds;
        if (envelope == null ||
            envelope.Version != NeonLetterColorSaveEnvelope.CurrentVersion ||
            envelope.Entries == null)
        {
            return _epoch;
        }

        foreach (NeonLetterColorSaveEntry? entry in envelope.Entries)
        {
            if (!CanStage(entry))
            {
                continue;
            }

            var stagedEntry = new NeonLetterColorSaveEntry(
                entry.SaveId,
                entry.RecipeId,
                entry.Color);
            if (_pendingBySaveId.TryGetValue(
                    stagedEntry.SaveId,
                    out LinkedListNode<NeonLetterColorSaveEntry>? existing))
            {
                existing.Value = stagedEntry;
                continue;
            }

            LinkedListNode<NeonLetterColorSaveEntry> node =
                _pending.AddLast(stagedEntry);
            _pendingBySaveId.Add(stagedEntry.SaveId, node);
        }

        _nextPending = _pending.First;
        return _epoch;
    }

    internal void Cancel()
    {
        BeginNextEpoch();
        _startedAtSeconds = 0d;
    }

    internal bool HasWorkForToken(long epoch, ulong readinessToken)
    {
        ValidateReadinessToken(readinessToken);
        return epoch == _epoch &&
               _pending.Count > 0 &&
               (readinessToken != _readinessToken ||
                _readinessWaveAttemptsRemaining > 0);
    }

    internal int Advance(
        long epoch,
        ulong readinessToken,
        Func<
            NeonLetterColorSaveEntry,
            NeonLetterSinglePlayerRestoreAttemptResult> attempt)
    {
        ValidateReadinessToken(readinessToken);
        ArgumentNullException.ThrowIfNull(attempt);

        if (epoch != _epoch || _pending.Count == 0)
        {
            return 0;
        }

        if (readinessToken != _readinessToken)
        {
            _readinessToken = readinessToken;
            _readinessWaveAttemptsRemaining = _pending.Count;
        }

        int attemptsRemaining = Math.Min(
            MaxAttemptsPerTick,
            Math.Min(_readinessWaveAttemptsRemaining, _pending.Count));
        if (attemptsRemaining == 0)
        {
            return 0;
        }

        int appliedCount = 0;
        while (attemptsRemaining > 0 &&
               _pending.Count > 0 &&
               _readinessWaveAttemptsRemaining > 0)
        {
            LinkedListNode<NeonLetterColorSaveEntry> current =
                _nextPending?.List == _pending
                    ? _nextPending
                    : _pending.First!;
            LinkedListNode<NeonLetterColorSaveEntry>? next =
                current.Next ?? _pending.First;
            long attemptedEpoch = _epoch;
            _nextPending = next;
            attemptsRemaining--;
            _readinessWaveAttemptsRemaining--;
            NeonLetterSinglePlayerRestoreAttemptResult result =
                attempt(current.Value);

            if (attemptedEpoch != _epoch ||
                epoch != _epoch ||
                readinessToken != _readinessToken ||
                current.List != _pending)
            {
                return appliedCount;
            }

            switch (result)
            {
                case NeonLetterSinglePlayerRestoreAttemptResult.Applied:
                    appliedCount++;
                    RemovePending(current, next);
                    break;

                case NeonLetterSinglePlayerRestoreAttemptResult.Terminal:
                    RemovePending(current, next);
                    break;

                case NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable:
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported single-player restore result {result}.");
            }
        }

        return appliedCount;
    }

    internal int Advance(
        long epoch,
        double nowSeconds,
        Func<
            NeonLetterColorSaveEntry,
            NeonLetterSinglePlayerRestoreAttemptResult> attempt)
    {
        ValidateNowSeconds(nowSeconds);
        ArgumentNullException.ThrowIfNull(attempt);

        if (epoch != _epoch || _pending.Count == 0)
        {
            return 0;
        }

        if (nowSeconds < _startedAtSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "Restore time cannot precede the staged start time.");
        }

        int appliedCount = 0;
        int attemptsRemaining = Math.Min(MaxAttemptsPerTick, _pending.Count);
        while (attemptsRemaining > 0 && _pending.Count > 0)
        {
            LinkedListNode<NeonLetterColorSaveEntry> current =
                _nextPending?.List == _pending
                    ? _nextPending
                    : _pending.First!;
            LinkedListNode<NeonLetterColorSaveEntry>? next =
                current.Next ?? _pending.First;
            long attemptedEpoch = _epoch;
            NeonLetterSinglePlayerRestoreAttemptResult result =
                attempt(current.Value);
            attemptsRemaining--;

            if (attemptedEpoch != _epoch ||
                epoch != _epoch ||
                current.List != _pending)
            {
                return appliedCount;
            }

            switch (result)
            {
                case NeonLetterSinglePlayerRestoreAttemptResult.Applied:
                    appliedCount++;
                    RemovePending(current, next);
                    break;

                case NeonLetterSinglePlayerRestoreAttemptResult.Terminal:
                    RemovePending(current, next);
                    break;

                case NeonLetterSinglePlayerRestoreAttemptResult.TargetUnavailable:
                    _nextPending = next;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported single-player restore result {result}.");
            }
        }

        return appliedCount;
    }

    private void BeginNextEpoch()
    {
        unchecked
        {
            _epoch++;
        }

        ClearPending();
    }

    private void ClearPending()
    {
        _pending.Clear();
        _pendingBySaveId.Clear();
        _nextPending = null;
        _readinessToken = 0;
        _readinessWaveAttemptsRemaining = 0;
    }

    private void RemovePending(
        LinkedListNode<NeonLetterColorSaveEntry> node,
        LinkedListNode<NeonLetterColorSaveEntry>? next)
    {
        _pendingBySaveId.Remove(node.Value.SaveId);
        _pending.Remove(node);
        _nextPending = next?.List == _pending
            ? next
            : _pending.First;
    }

    private static bool CanStage(NeonLetterColorSaveEntry? entry)
    {
        return entry != null &&
               KnownRecipeIds.Contains(entry.RecipeId) &&
               float.IsFinite(entry.Color.Red) &&
               float.IsFinite(entry.Color.Green) &&
               float.IsFinite(entry.Color.Blue) &&
               float.IsFinite(entry.Color.Alpha);
    }

    private static void ValidateNowSeconds(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "Restore time must be finite and non-negative.");
        }
    }

    private static void ValidateReadinessToken(ulong readinessToken)
    {
        if (readinessToken == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(readinessToken),
                readinessToken,
                "The readiness token must be non-zero.");
        }
    }
}

internal sealed class NeonLetterSinglePlayerRestoreLifecycle
{
    private readonly NeonLetterSinglePlayerRestoreCoordinator _coordinator =
        new();
    private bool _isSinglePlayer;
    private long _epoch;

    internal int PendingCount => _coordinator.PendingCount;

    internal void SetSinglePlayerRole(bool isSinglePlayer)
    {
        if (_isSinglePlayer == isSinglePlayer)
        {
            return;
        }

        _isSinglePlayer = isSinglePlayer;
        if (!isSinglePlayer)
        {
            _coordinator.Cancel();
        }
    }

    internal void Stage(
        NeonLetterColorSaveEnvelope? envelope,
        double nowSeconds)
    {
        if (!_isSinglePlayer)
        {
            return;
        }

        _epoch = _coordinator.Stage(envelope, nowSeconds);
    }

    internal int Advance(
        double nowSeconds,
        Func<
            NeonLetterColorSaveEntry,
            NeonLetterSinglePlayerRestoreAttemptResult> attempt)
    {
        if (!_isSinglePlayer)
        {
            return 0;
        }

        return _coordinator.Advance(_epoch, nowSeconds, attempt);
    }

    internal bool HasWorkForToken(ulong readinessToken)
    {
        return _isSinglePlayer &&
               _coordinator.HasWorkForToken(_epoch, readinessToken);
    }

    internal int Advance(
        ulong readinessToken,
        Func<
            NeonLetterColorSaveEntry,
            NeonLetterSinglePlayerRestoreAttemptResult> attempt)
    {
        if (!_isSinglePlayer)
        {
            return 0;
        }

        return _coordinator.Advance(_epoch, readinessToken, attempt);
    }

    internal void OnWorldExited()
    {
        Cancel();
    }

    internal void Deinitialize()
    {
        Cancel();
    }

    private void Cancel()
    {
        _isSinglePlayer = false;
        _coordinator.Cancel();
    }
}
