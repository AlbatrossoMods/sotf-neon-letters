#nullable enable

namespace SOTFNeonLetters;

internal delegate void NeonLetterMaterialColorApplyCallback<TState>(
    ref TState state);

internal sealed class NeonLetterMaterialInitializationLifecycle<TRoot>
    where TRoot : class
{
    private readonly Func<TRoot, bool> _isRootAlive;
    private readonly Dictionary<int, WeakReference<TRoot>>
        _initializedRoots = new();

    internal NeonLetterMaterialInitializationLifecycle(
        Func<TRoot, bool> isRootAlive)
    {
        ArgumentNullException.ThrowIfNull(isRootAlive);
        _isRootAlive = isRootAlive;
    }

    internal bool TryApply<TState>(
        int instanceId,
        TRoot root,
        bool isKnownCompletedStructure,
        ref TState state,
        NeonLetterMaterialColorApplyCallback<TState> apply)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(apply);

        if (!isKnownCompletedStructure)
        {
            return false;
        }

        if (!_isRootAlive(root))
        {
            throw new InvalidOperationException(
                "Cannot initialize material color for a destroyed structure.");
        }

        if (_initializedRoots.TryGetValue(
                instanceId,
                out WeakReference<TRoot>? initializedRoot) &&
            initializedRoot.TryGetTarget(out TRoot? existingRoot) &&
            ReferenceEquals(existingRoot, root) &&
            _isRootAlive(existingRoot))
        {
            return false;
        }

        apply(ref state);
        _initializedRoots[instanceId] = new WeakReference<TRoot>(root);
        return true;
    }

    internal bool Remove(int instanceId, TRoot expectedRoot)
    {
        ArgumentNullException.ThrowIfNull(expectedRoot);

        if (!_initializedRoots.TryGetValue(
                instanceId,
                out WeakReference<TRoot>? initializedRoot))
        {
            return false;
        }

        if (initializedRoot.TryGetTarget(out TRoot? existingRoot) &&
            !ReferenceEquals(existingRoot, expectedRoot))
        {
            return false;
        }

        return _initializedRoots.Remove(instanceId);
    }

    internal void Clear()
    {
        _initializedRoots.Clear();
    }
}
