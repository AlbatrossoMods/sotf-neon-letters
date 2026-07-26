#nullable enable

namespace SOTFNeonLetters;

internal sealed class NeonLetterFallbackRollbackAdapter<TTarget>
    : IDisposable
    where TTarget : class?
{
    private readonly TTarget _target;
    private readonly Func<TTarget, bool> _isTargetAlive;
    private readonly Func<TTarget, bool> _isTargetAttached;
    private readonly Func<TTarget, bool> _hasNetworkIdentity;
    private readonly Action<TTarget> _destroyLocally;
    private Action<TTarget>? _destroyOverNetwork;

    internal NeonLetterFallbackRollbackAdapter(
        TTarget target,
        Func<TTarget, bool> isTargetAlive,
        Func<TTarget, bool> isTargetAttached,
        Func<TTarget, bool> hasNetworkIdentity,
        Action<TTarget> destroyOverNetwork,
        Action<TTarget> destroyLocally)
    {
        ArgumentNullException.ThrowIfNull(isTargetAlive);
        ArgumentNullException.ThrowIfNull(isTargetAttached);
        ArgumentNullException.ThrowIfNull(hasNetworkIdentity);
        ArgumentNullException.ThrowIfNull(destroyOverNetwork);
        ArgumentNullException.ThrowIfNull(destroyLocally);

        _target = target;
        _isTargetAlive = isTargetAlive;
        _isTargetAttached = isTargetAttached;
        _hasNetworkIdentity = hasNetworkIdentity;
        _destroyOverNetwork = destroyOverNetwork;
        _destroyLocally = destroyLocally;
    }

    public void Dispose()
    {
        Action<TTarget>? destroyOverNetwork =
            Interlocked.Exchange(ref _destroyOverNetwork, null);
        if (destroyOverNetwork is null || !_isTargetAlive(_target))
        {
            return;
        }

        if (_isTargetAttached(_target) &&
            _hasNetworkIdentity(_target))
        {
            destroyOverNetwork(_target);
            return;
        }

        _destroyLocally(_target);
    }
}
