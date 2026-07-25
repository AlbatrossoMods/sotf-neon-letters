#nullable enable

namespace SOTFNeonLetters;

/// <summary>
/// Main-thread-only binding cache. The liveness and factory callbacks must not
/// mutate this cache; same-identity recursive creation is rejected fail-fast.
/// </summary>
internal sealed class NeonLetterEmissionBindingCache<
    TRoot,
    TDefinition,
    TBinding>
    where TRoot : class
    where TDefinition : class
    where TBinding : class
{
    private readonly Func<TRoot, bool> _isRootAlive;
    private readonly Func<TRoot, TDefinition, TBinding> _createBinding;
    private readonly Dictionary<int, CacheEntry> _entries = new();
    private readonly Dictionary<int, bool> _creatingInstanceIds = new();

    public NeonLetterEmissionBindingCache(
        Func<TRoot, bool> isRootAlive,
        Func<TRoot, TDefinition, TBinding> createBinding)
    {
        ArgumentNullException.ThrowIfNull(isRootAlive);
        ArgumentNullException.ThrowIfNull(createBinding);

        _isRootAlive = isRootAlive;
        _createBinding = createBinding;
    }

    public int Count => _entries.Count;

    public TBinding GetOrCreate(
        int instanceId,
        TRoot root,
        TDefinition definition,
        int recipeId)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(definition);

        if (!_isRootAlive(root))
        {
            Remove(instanceId, root);
            throw new InvalidOperationException(
                "Cannot create an emission binding for a destroyed structure.");
        }

        if (_entries.TryGetValue(instanceId, out CacheEntry? entry))
        {
            if (entry.IsReusable(root, definition, recipeId) &&
                _isRootAlive(entry.Root))
            {
                return entry.Binding;
            }

            _entries.Remove(instanceId);
        }

        if (_creatingInstanceIds.ContainsKey(instanceId))
        {
            _creatingInstanceIds[instanceId] = true;
            throw new InvalidOperationException(
                "Recursive emission binding creation is not supported.");
        }

        _creatingInstanceIds.Add(instanceId, false);
        try
        {
            TBinding binding = _createBinding(root, definition);
            if (_creatingInstanceIds[instanceId])
            {
                throw new InvalidOperationException(
                    "Recursive emission binding creation is not supported.");
            }

            if (binding == null)
            {
                throw new InvalidOperationException(
                    "The emission binding factory returned no binding.");
            }

            _entries.Add(
                instanceId,
                new CacheEntry(root, definition, recipeId, binding));
            return binding;
        }
        finally
        {
            _creatingInstanceIds.Remove(instanceId);
        }
    }

    public bool Remove(int instanceId, TRoot expectedRoot)
    {
        ArgumentNullException.ThrowIfNull(expectedRoot);

        if (!_entries.TryGetValue(instanceId, out CacheEntry? entry) ||
            !ReferenceEquals(entry.Root, expectedRoot))
        {
            return false;
        }

        return _entries.Remove(instanceId);
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private sealed class CacheEntry
    {
        public CacheEntry(
            TRoot root,
            TDefinition definition,
            int recipeId,
            TBinding binding)
        {
            Root = root;
            Definition = definition;
            RecipeId = recipeId;
            Binding = binding;
        }

        public TRoot Root { get; }
        public TDefinition Definition { get; }
        public int RecipeId { get; }
        public TBinding Binding { get; }

        public bool IsReusable(
            TRoot root,
            TDefinition definition,
            int recipeId)
        {
            return ReferenceEquals(Root, root) &&
                   ReferenceEquals(Definition, definition) &&
                   RecipeId == recipeId;
        }
    }
}
