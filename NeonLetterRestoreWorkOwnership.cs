#nullable enable

namespace SOTFNeonLetters;

internal readonly record struct NeonLetterRestoreUpdateOwnership(
    object? Token,
    ulong Generation);

internal readonly record struct NeonLetterRestoreResetOwnership(
    object? Token);

internal readonly record struct NeonLetterRestoreResetRequest(
    bool RollbackOwnedFallbacks,
    ulong Version);

internal readonly record struct NeonLetterRestoreResetCompletion(
    bool ResumeLoads,
    ulong QueueSuspensionGeneration);

internal sealed class NeonLetterRestoreWorkOwnership
{
    private readonly object _sync = new();
    private readonly NeonLetterMonotonicSequence _generations = new();
    private OwnershipKind _owner;
    private object? _ownerToken;
    private bool _resetRequested;
    private bool _rollbackOwnedFallbacks;
    private bool _keepLoadsSuspended;
    private ulong _queueSuspensionGeneration;

    internal bool TryBeginUpdate(
        out NeonLetterRestoreUpdateOwnership ownership)
    {
        lock (_sync)
        {
            if (_owner != OwnershipKind.None || _resetRequested)
            {
                ownership = default;
                return false;
            }

            _owner = OwnershipKind.Update;
            _ownerToken = new object();
            ownership = new NeonLetterRestoreUpdateOwnership(
                _ownerToken,
                _generations.Current);
            return true;
        }
    }

    internal bool IsUpdateCurrent(
        NeonLetterRestoreUpdateOwnership ownership)
    {
        lock (_sync)
        {
            return IsUpdateOwner(ownership) &&
                   !_resetRequested &&
                   ownership.Generation == _generations.Current;
        }
    }

    internal bool RequestReset(
        bool rollbackOwnedFallbacks,
        bool resumeLoads,
        ulong queueSuspensionGeneration,
        out NeonLetterRestoreResetOwnership ownership)
    {
        if (queueSuspensionGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(queueSuspensionGeneration),
                queueSuspensionGeneration,
                "The queue suspension generation must be nonzero.");
        }

        lock (_sync)
        {
            _resetRequested = true;
            _rollbackOwnedFallbacks |= rollbackOwnedFallbacks;
            _keepLoadsSuspended |= !resumeLoads;
            _queueSuspensionGeneration = queueSuspensionGeneration;
            _generations.Advance();
            if (_owner != OwnershipKind.None)
            {
                ownership = default;
                return false;
            }

            ownership = TransferToResetOwner();
            return true;
        }
    }

    internal bool TryGetPendingResetRequest(
        NeonLetterRestoreUpdateOwnership updateOwnership,
        out NeonLetterRestoreResetRequest request)
    {
        lock (_sync)
        {
            if (!IsUpdateOwner(updateOwnership) || !_resetRequested)
            {
                request = default;
                return false;
            }

            request = new NeonLetterRestoreResetRequest(
                _rollbackOwnedFallbacks,
                _generations.Current);
            return true;
        }
    }

    internal bool CompleteUpdate(
        NeonLetterRestoreUpdateOwnership updateOwnership,
        out NeonLetterRestoreResetOwnership resetOwnership)
    {
        lock (_sync)
        {
            if (!IsUpdateOwner(updateOwnership))
            {
                resetOwnership = default;
                return false;
            }

            if (_resetRequested)
            {
                resetOwnership = TransferToResetOwner();
                return true;
            }

            _owner = OwnershipKind.None;
            _ownerToken = null;
            resetOwnership = default;
            return false;
        }
    }

    internal NeonLetterRestoreResetRequest GetResetRequest(
        NeonLetterRestoreResetOwnership ownership)
    {
        lock (_sync)
        {
            EnsureResetOwner(ownership);
            return new NeonLetterRestoreResetRequest(
                _rollbackOwnedFallbacks,
                _generations.Current);
        }
    }

    internal bool TryCompleteReset(
        NeonLetterRestoreResetOwnership ownership,
        ulong satisfiedVersion,
        bool rollbackSatisfied,
        out NeonLetterRestoreResetRequest pendingRequest,
        out NeonLetterRestoreResetCompletion completion)
    {
        lock (_sync)
        {
            EnsureResetOwner(ownership);
            ulong currentVersion = _generations.Current;
            if (satisfiedVersion > currentVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(satisfiedVersion),
                    satisfiedVersion,
                    "The satisfied reset version cannot be newer than the " +
                    "current reset request.");
            }

            if (satisfiedVersion != currentVersion ||
                (_rollbackOwnedFallbacks && !rollbackSatisfied))
            {
                pendingRequest = new NeonLetterRestoreResetRequest(
                    _rollbackOwnedFallbacks,
                    currentVersion);
                completion = default;
                return false;
            }

            completion = new NeonLetterRestoreResetCompletion(
                ResumeLoads: !_keepLoadsSuspended,
                QueueSuspensionGeneration: _queueSuspensionGeneration);
            _owner = OwnershipKind.None;
            _ownerToken = null;
            _resetRequested = false;
            _rollbackOwnedFallbacks = false;
            _keepLoadsSuspended = false;
            _queueSuspensionGeneration = 0;
            pendingRequest = default;
            return true;
        }
    }

    internal ulong RecordSignal(NeonLetterMonotonicSequence signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        lock (_sync)
        {
            return signals.Advance();
        }
    }

    internal ulong ReadSignal(NeonLetterMonotonicSequence signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        lock (_sync)
        {
            return signals.Current;
        }
    }

    private bool IsUpdateOwner(
        NeonLetterRestoreUpdateOwnership ownership)
    {
        return _owner == OwnershipKind.Update &&
               ownership.Token != null &&
               ReferenceEquals(_ownerToken, ownership.Token);
    }

    private NeonLetterRestoreResetOwnership TransferToResetOwner()
    {
        _owner = OwnershipKind.Reset;
        _ownerToken = new object();
        return new NeonLetterRestoreResetOwnership(_ownerToken);
    }

    private void EnsureResetOwner(
        NeonLetterRestoreResetOwnership ownership)
    {
        if (_owner != OwnershipKind.Reset ||
            ownership.Token == null ||
            !ReferenceEquals(_ownerToken, ownership.Token))
        {
            throw new InvalidOperationException(
                "The caller does not own restore reset work.");
        }
    }

    private enum OwnershipKind
    {
        None,
        Update,
        Reset
    }
}
