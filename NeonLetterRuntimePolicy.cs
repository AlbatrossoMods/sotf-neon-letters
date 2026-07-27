#nullable enable

namespace SOTFNeonLetters;

/// <summary>
/// Exposes the native recipe values that control relocation and demolition.
/// </summary>
/// <typeparam name="TRecipe">The native recipe reference type.</typeparam>
public interface IRecipeRelocationTarget<TRecipe>
    where TRecipe : class
{
    int RelocateModeValue { get; set; }
    TRecipe? RelocateRecipeOverride { get; set; }
}

/// <summary>
/// Stores the relocation values captured before a reversible recipe mutation.
/// </summary>
/// <typeparam name="TRecipe">The native recipe reference type.</typeparam>
public readonly record struct RecipeRelocationState<TRecipe>(
    int RelocateModeValue,
    TRecipe? RelocateRecipeOverride)
    where TRecipe : class;

/// <summary>
/// Applies the demolition-only recipe values used by native structure removal.
/// </summary>
public static class RecipeDemolitionApplicator
{
    private const int CollapseModeValue = 1;

    /// <summary>
    /// Captures both relocation values before a reversible recipe mutation.
    /// </summary>
    public static RecipeRelocationState<TRecipe> Capture<TRecipe>(
        IRecipeRelocationTarget<TRecipe> target)
        where TRecipe : class
    {
        ArgumentNullException.ThrowIfNull(target);
        return new RecipeRelocationState<TRecipe>(
            target.RelocateModeValue,
            target.RelocateRecipeOverride);
    }

    /// <summary>
    /// Replaces relocation with native collapse and removes its recipe override.
    /// </summary>
    public static void Apply<TRecipe>(
        IRecipeRelocationTarget<TRecipe> target)
        where TRecipe : class
    {
        ArgumentNullException.ThrowIfNull(target);
        target.RelocateModeValue = CollapseModeValue;
        target.RelocateRecipeOverride = null;
    }

    /// <summary>
    /// Returns whether the target retains the native demolition-only values.
    /// </summary>
    public static bool IsApplied<TRecipe>(
        IRecipeRelocationTarget<TRecipe> target)
        where TRecipe : class
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.RelocateModeValue == CollapseModeValue &&
               target.RelocateRecipeOverride == null;
    }

    /// <summary>
    /// Restores both relocation values captured before the mutation.
    /// </summary>
    public static void Restore<TRecipe>(
        IRecipeRelocationTarget<TRecipe> target,
        RecipeRelocationState<TRecipe> state)
        where TRecipe : class
    {
        ArgumentNullException.ThrowIfNull(target);
        target.RelocateModeValue = state.RelocateModeValue;
        target.RelocateRecipeOverride =
            state.RelocateRecipeOverride;
    }
}

public interface IRecipePlacementTarget
{
    /// <summary>
    /// Gets whether dismantling collapses the structure instead of entering relocation.
    /// </summary>
    bool DemolitionModeEnabled { get; }
    bool GroundPlacementChecksRemoved { get; }
    bool ParentRecipeOverridesCleared { get; }
    NeonLetterASmallDefinition.PlacementDefinition Snapshot { get; }
    NeonLetterASmallDefinition.PlacementAnchor Anchor { set; }
    NeonLetterASmallDefinition.PlacementCastRadiusFormula CastRadiusFormula { set; }
    bool AlignToSurface { set; }
    bool CanBeRotated { set; }
    bool ForceUp { set; }
    bool LockUpwardVector { set; }
    bool AllowsTreePlacement { set; }
    bool AllowsNonTreePlacement { set; }
    float MinimumHeightAboveTree { set; }
    float MaximumHeightAboveTree { set; }
    bool AllowDynamicObjectParenting { set; }
    bool AllowScrewStructureParenting { set; }
    bool AllowFreeFormStructureParenting { set; }
    bool UseFreeFormStructures { set; }
    bool AutoFoundation { set; }
    bool UseOverridePlacementSize { set; }
    float PlacementDepthSizeRatio { set; }

    /// <summary>
    /// Configures dismantling to collapse the structure instead of relocating it.
    /// </summary>
    void EnableDemolitionMode();
    void RemoveGroundPlacementChecks();
    void SetInitialRotation(float x, float y, float z);
}

public static class RecipePlacementApplicator
{
    public static void Apply(
        NeonLetterASmallDefinition.PlacementDefinition placement,
        IRecipePlacementTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // SonsSdk 0.8.6 creates custom recipes by cloning ground recipe 25.
        // Replace the complete placement contract so none of its floor-only
        // fields or providers can leak into the wall-mounted blueprint.
        target.EnableDemolitionMode();
        target.RemoveGroundPlacementChecks();
        target.Anchor = placement.Anchor;
        target.CastRadiusFormula = placement.CastRadiusFormula;
        target.AlignToSurface = placement.AlignToSurface;
        target.CanBeRotated = placement.CanBeRotated;
        target.ForceUp = placement.ForceUp;
        target.LockUpwardVector = placement.LockUpwardVector;
        target.SetInitialRotation(
            placement.InitialRotationX,
            placement.InitialRotationY,
            placement.InitialRotationZ);
        target.AllowsTreePlacement = placement.AllowsTreePlacement;
        target.AllowsNonTreePlacement = placement.AllowsNonTreePlacement;
        target.MinimumHeightAboveTree = placement.MinimumHeightAboveTree;
        target.MaximumHeightAboveTree = placement.MaximumHeightAboveTree;
        target.AllowDynamicObjectParenting = placement.AllowDynamicObjectParenting;
        target.AllowScrewStructureParenting = placement.AllowScrewStructureParenting;
        target.AllowFreeFormStructureParenting = placement.AllowFreeFormStructureParenting;
        target.UseFreeFormStructures = placement.UseFreeFormStructures;
        target.AutoFoundation = placement.AutoFoundation;
        target.UseOverridePlacementSize = placement.UseOverridePlacementSize;
        target.PlacementDepthSizeRatio = placement.PlacementDepthSizeRatio;

        if (!target.DemolitionModeEnabled ||
            !target.GroundPlacementChecksRemoved ||
            !target.ParentRecipeOverridesCleared ||
            target.Snapshot != placement)
        {
            throw new InvalidOperationException(
                "The complete wall-placement contract was not retained by the runtime recipe.");
        }
    }
}

public static class NeonLetterColliderPolicy
{
    public static NeonLetterSmallDefinition.ColliderSize Resolve(
        NeonLetterSmallDefinition definition,
        float visualWidth,
        float visualHeight,
        float visualDepth)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.ResolveColliderSize(
            visualWidth,
            visualHeight,
            visualDepth);
    }
}

public interface IRuntimeMaterialHandle
{
    string Name { get; set; }
    string ShaderName { get; }
    int RenderQueue { get; set; }
    object ShaderKeywords { get; set; }

    void CopyPropertiesFrom(IRuntimeMaterialHandle source);
}

public interface IRuntimeRendererHandle
{
    string Name { get; }
    IReadOnlyList<IRuntimeMaterialHandle> Materials { get; }

    void SetMaterials(IReadOnlyList<IRuntimeMaterialHandle> materials);
}

public interface IRuntimeMaterialFactory
{
    string ShaderName { get; }
    bool IsShaderSupported { get; }

    IRuntimeMaterialHandle Create();
}

internal static class NeonLetterRuntimeShaderSelection
{
    internal const string ShaderName = "Sons/HDRPLit";

    public static TShader Resolve<TShader>(
        Func<string, TShader> shaderResolver)
    {
        ArgumentNullException.ThrowIfNull(shaderResolver);
        return shaderResolver(ShaderName);
    }
}

public interface IRuntimeMaterialOwner
{
    void Release(IRuntimeMaterialHandle material);
}

internal sealed class RuntimeMaterialCatalogEntry
{
    public RuntimeMaterialCatalogEntry(
        string prefabName,
        IReadOnlyList<IRuntimeRendererHandle> renderers,
        Action? validateAssignments = null)
    {
        PrefabName = prefabName;
        Renderers = renderers;
        ValidateAssignments = validateAssignments;
    }

    public string PrefabName { get; }
    public IReadOnlyList<IRuntimeRendererHandle> Renderers { get; }
    public Action? ValidateAssignments { get; }
}

internal sealed class RuntimeMaterialCatalogTransaction
{
    private readonly Func<IRuntimeMaterialFactory> _materialFactoryResolver;
    private readonly IReadOnlyList<RuntimeMaterialCatalogEntry> _entries;
    private IRuntimeMaterialFactory? _materialFactory;
    private RuntimeMaterialReplacementLease? _lease;

    public RuntimeMaterialCatalogTransaction(
        Func<IRuntimeMaterialFactory> materialFactoryResolver,
        IReadOnlyList<RuntimeMaterialCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(materialFactoryResolver);
        ArgumentNullException.ThrowIfNull(entries);
        _materialFactoryResolver = materialFactoryResolver;
        _entries = entries;
    }

    public RuntimeMaterialReplacementLease Execute()
    {
        if (_lease != null && !_lease.IsRolledBack)
        {
            return _lease;
        }

        _materialFactory ??= _materialFactoryResolver()
            ?? throw new InvalidOperationException(
                "The runtime material factory resolver returned no factory.");
        _lease = RuntimeMaterialReplacement.PrepareAndAssign(
            _entries,
            _materialFactory);
        return _lease;
    }
}

internal sealed class RuntimeMaterialReplacementLease
{
    private readonly IReadOnlyList<RuntimeMaterialAssignment> _assignments;
    private readonly IReadOnlyList<IRuntimeMaterialHandle> _ownedMaterials;
    private readonly IRuntimeMaterialOwner? _owner;
    private bool _retained;

    internal RuntimeMaterialReplacementLease(
        IReadOnlyList<RuntimeMaterialAssignment> assignments,
        IReadOnlyList<IRuntimeMaterialHandle> ownedMaterials,
        IRuntimeMaterialOwner? owner)
    {
        _assignments = assignments;
        _ownedMaterials = ownedMaterials;
        _owner = owner;
    }

    internal bool IsRolledBack { get; private set; }

    public void Retain()
    {
        if (IsRolledBack)
        {
            throw new InvalidOperationException(
                "Rolled-back runtime materials cannot be retained.");
        }

        _retained = true;
    }

    public void Rollback()
    {
        if (_retained || IsRolledBack)
        {
            return;
        }

        IsRolledBack = true;
        List<Exception>? cleanupExceptions = null;
        for (int assignmentIndex = _assignments.Count - 1;
             assignmentIndex >= 0;
             assignmentIndex--)
        {
            RuntimeMaterialAssignment assignment = _assignments[assignmentIndex];
            try
            {
                assignment.Renderer.SetMaterials(assignment.OriginalMaterials);
            }
            catch (Exception exception)
            {
                (cleanupExceptions ??= new List<Exception>()).Add(exception);
            }
        }

        if (_owner != null)
        {
            for (int materialIndex = _ownedMaterials.Count - 1;
                 materialIndex >= 0;
                 materialIndex--)
            {
                try
                {
                    _owner.Release(_ownedMaterials[materialIndex]);
                }
                catch (Exception exception)
                {
                    (cleanupExceptions ??= new List<Exception>()).Add(exception);
                }
            }
        }

        if (cleanupExceptions != null)
        {
            throw new AggregateException(
                "Runtime material rollback did not complete cleanly.",
                cleanupExceptions);
        }
    }
}

internal sealed record RuntimeMaterialAssignment(
    IRuntimeRendererHandle Renderer,
    IReadOnlyList<IRuntimeMaterialHandle> OriginalMaterials,
    IReadOnlyList<IRuntimeMaterialHandle> RuntimeMaterials);

public static class RuntimeMaterialReplacement
{
    public static void ReplaceAll(
        string prefabName,
        IReadOnlyList<IRuntimeRendererHandle> renderers,
        IRuntimeMaterialFactory materialFactory)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            throw new ArgumentException("Prefab name is required.", nameof(prefabName));
        }
        ArgumentNullException.ThrowIfNull(renderers);
        ArgumentNullException.ThrowIfNull(materialFactory);

        var transaction = new RuntimeMaterialCatalogTransaction(
            () => materialFactory,
            new[]
            {
                new RuntimeMaterialCatalogEntry(prefabName, renderers)
            });
        RuntimeMaterialReplacementLease lease = transaction.Execute();
        lease.Retain();
    }

    internal static RuntimeMaterialReplacementLease PrepareAndAssign(
        IReadOnlyList<RuntimeMaterialCatalogEntry> entries,
        IRuntimeMaterialFactory materialFactory)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(materialFactory);

        if (!materialFactory.IsShaderSupported)
        {
            throw new InvalidOperationException(
                $"The game shader '{materialFactory.ShaderName}' is unavailable or unsupported; " +
                "cannot replace stripped shaders in the neon symbol catalog.");
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                "The neon symbol catalog has no prefabs whose bundle shaders can be replaced.");
        }

        var sourceAssignments = new List<RuntimeMaterialAssignment>();
        foreach (RuntimeMaterialCatalogEntry entry in entries)
        {
            if (entry == null)
            {
                throw new InvalidOperationException(
                    "The neon symbol catalog contains a null material entry.");
            }

            if (string.IsNullOrWhiteSpace(entry.PrefabName))
            {
                throw new InvalidOperationException(
                    "Every runtime material catalog entry requires a prefab name.");
            }

            if (entry.Renderers == null || entry.Renderers.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Prefab '{entry.PrefabName}' has no renderers whose bundle shaders can be " +
                    "replaced.");
            }

            foreach (IRuntimeRendererHandle renderer in entry.Renderers)
            {
                if (renderer == null)
                {
                    throw new InvalidOperationException(
                        $"Prefab '{entry.PrefabName}' contains a null renderer.");
                }

                IReadOnlyList<IRuntimeMaterialHandle> sourceMaterials = renderer.Materials;
                if (sourceMaterials == null || sourceMaterials.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.Name}' in prefab '{entry.PrefabName}' has no source " +
                        "materials.");
                }

                var originalMaterials = sourceMaterials.ToArray();
                for (int materialIndex = 0;
                     materialIndex < originalMaterials.Length;
                     materialIndex++)
                {
                    if (originalMaterials[materialIndex] == null)
                    {
                        throw new InvalidOperationException(
                            $"Renderer '{renderer.Name}' in prefab '{entry.PrefabName}' has a null " +
                            $"material at index {materialIndex}.");
                    }
                }

                sourceAssignments.Add(
                    new RuntimeMaterialAssignment(
                        renderer,
                        originalMaterials,
                        Array.Empty<IRuntimeMaterialHandle>()));
            }
        }

        var ownedMaterials = new List<IRuntimeMaterialHandle>();
        var preparedAssignments =
            new List<RuntimeMaterialAssignment>(sourceAssignments.Count);
        var committedAssignments =
            new List<RuntimeMaterialAssignment>(sourceAssignments.Count);
        var lease = new RuntimeMaterialReplacementLease(
            committedAssignments,
            ownedMaterials,
            materialFactory as IRuntimeMaterialOwner);
        try
        {
            foreach (RuntimeMaterialAssignment sourceAssignment in sourceAssignments)
            {
                IReadOnlyList<IRuntimeMaterialHandle> sourceMaterials =
                    sourceAssignment.OriginalMaterials;
                var runtimeMaterials =
                    new IRuntimeMaterialHandle[sourceMaterials.Count];
                for (int materialIndex = 0;
                     materialIndex < sourceMaterials.Count;
                     materialIndex++)
                {
                    IRuntimeMaterialHandle sourceMaterial = sourceMaterials[materialIndex];
                    IRuntimeMaterialHandle runtimeMaterial = materialFactory.Create()
                        ?? throw new InvalidOperationException(
                            "The runtime material factory returned a null material.");
                    ownedMaterials.Add(runtimeMaterial);
                    runtimeMaterial.CopyPropertiesFrom(sourceMaterial);
                    runtimeMaterial.Name = $"{sourceMaterial.Name}_Runtime";
                    runtimeMaterial.ShaderKeywords = sourceMaterial.ShaderKeywords;
                    runtimeMaterial.RenderQueue = sourceMaterial.RenderQueue;
                    runtimeMaterials[materialIndex] = runtimeMaterial;
                    ValidatePreparedMaterial(
                        sourceAssignment.Renderer,
                        materialIndex,
                        runtimeMaterial,
                        sourceMaterial,
                        materialFactory);
                }

                preparedAssignments.Add(
                    sourceAssignment with
                    {
                        RuntimeMaterials = runtimeMaterials
                    });
            }

            foreach (RuntimeMaterialAssignment assignment in preparedAssignments)
            {
                committedAssignments.Add(assignment);
                assignment.Renderer.SetMaterials(assignment.RuntimeMaterials);
                ValidateAssignedMaterials(assignment, materialFactory);
            }

            foreach (RuntimeMaterialCatalogEntry entry in entries)
            {
                entry.ValidateAssignments?.Invoke();
            }

            return lease;
        }
        catch (Exception exception)
        {
            try
            {
                lease.Rollback();
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(exception, rollbackException);
            }

            throw;
        }
    }

    private static void ValidatePreparedMaterial(
        IRuntimeRendererHandle renderer,
        int materialIndex,
        IRuntimeMaterialHandle runtimeMaterial,
        IRuntimeMaterialHandle sourceMaterial,
        IRuntimeMaterialFactory materialFactory)
    {
        if (!string.Equals(
                runtimeMaterial.ShaderName,
                materialFactory.ShaderName,
                StringComparison.Ordinal) ||
            !string.Equals(
                runtimeMaterial.Name,
                $"{sourceMaterial.Name}_Runtime",
                StringComparison.Ordinal) ||
            runtimeMaterial.RenderQueue != sourceMaterial.RenderQueue)
        {
            throw new InvalidOperationException(
                $"Renderer '{renderer.Name}' did not prepare runtime material slot " +
                $"{materialIndex} with shader '{materialFactory.ShaderName}'.");
        }
    }

    private static void ValidateAssignedMaterials(
        RuntimeMaterialAssignment assignment,
        IRuntimeMaterialFactory materialFactory)
    {
        IReadOnlyList<IRuntimeMaterialHandle> assignedMaterials =
            assignment.Renderer.Materials;
        if (assignedMaterials == null ||
            assignedMaterials.Count != assignment.RuntimeMaterials.Count)
        {
            throw new InvalidOperationException(
                $"Renderer '{assignment.Renderer.Name}' did not retain all runtime material " +
                "assignments.");
        }

        for (int materialIndex = 0;
             materialIndex < assignedMaterials.Count;
             materialIndex++)
        {
            IRuntimeMaterialHandle assigned = assignedMaterials[materialIndex];
            IRuntimeMaterialHandle expected =
                assignment.RuntimeMaterials[materialIndex];
            if (assigned == null ||
                !string.Equals(
                    assigned.ShaderName,
                    materialFactory.ShaderName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    assigned.Name,
                    expected.Name,
                    StringComparison.Ordinal) ||
                assigned.RenderQueue != expected.RenderQueue)
            {
                throw new InvalidOperationException(
                    $"Renderer '{assignment.Renderer.Name}' did not retain runtime material " +
                    $"slot {materialIndex} with shader '{materialFactory.ShaderName}'.");
            }
        }
    }
}

public interface IBookPageRegistrationTarget<TRecipe, TTexture>
    where TRecipe : class
    where TTexture : class
{
    int PageCount { get; }

    void AddLocalization(string key, string value);
    string GetRecipeLocalizationId(TRecipe recipe);
    void CreatePage(
        TRecipe topRecipe,
        TRecipe? bottomRecipe,
        TTexture background,
        string titleLocalizationKey);
    bool LastPageMatches(
        TRecipe topRecipe,
        TRecipe? bottomRecipe,
        TTexture background,
        string titleLocalizationKey);
}

public interface ITransactionalBookPageRegistrationTarget<TRecipe, TTexture> :
    IBookPageRegistrationTarget<TRecipe, TTexture>
    where TRecipe : class
    where TTexture : class
{
    object CaptureRegistrationState(
        string titleLocalizationKey,
        TRecipe topRecipe,
        TRecipe? bottomRecipe);
    void RestoreRegistrationState(object snapshot);
}

public sealed class ReadyAlphabetBookPage<TRecipe>
    where TRecipe : class
{
    internal ReadyAlphabetBookPage(
        int pageIndex,
        NeonLetterSmallDefinition topDefinition,
        TRecipe topRecipe,
        NeonLetterSmallDefinition bottomDefinition,
        TRecipe bottomRecipe)
    {
        PageIndex = pageIndex;
        TopDefinition = topDefinition;
        TopRecipe = topRecipe;
        BottomDefinition = bottomDefinition;
        BottomRecipe = bottomRecipe;
    }

    public int PageIndex { get; }
    public NeonLetterSmallDefinition TopDefinition { get; }
    public TRecipe TopRecipe { get; }
    public NeonLetterSmallDefinition BottomDefinition { get; }
    public TRecipe BottomRecipe { get; }
}

public sealed class AlphabetBookPageCoordinator<TRecipe>
    where TRecipe : class
{
    private readonly Dictionary<char, TRecipe> _recipesBySymbol = new();
    private readonly HashSet<int> _completedPageIndexes = new();

    public ReadyAlphabetBookPage<TRecipe>? Add(
        NeonLetterSmallDefinition definition,
        TRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(recipe);

        _recipesBySymbol[definition.Symbol] = recipe;
        return GetNextReadyPage();
    }

    public ReadyAlphabetBookPage<TRecipe>? GetNextReadyPage()
    {
        int pageCount = NeonLetterSmallCatalog.All.Count / 2;
        int pageIndex = 0;
        while (pageIndex < pageCount && _completedPageIndexes.Contains(pageIndex))
        {
            pageIndex++;
        }

        if (pageIndex >= pageCount)
        {
            return null;
        }

        NeonLetterSmallDefinition topDefinition =
            NeonLetterSmallCatalog.All[pageIndex * 2];
        NeonLetterSmallDefinition bottomDefinition =
            NeonLetterSmallCatalog.All[pageIndex * 2 + 1];
        if (!_recipesBySymbol.TryGetValue(topDefinition.Symbol, out TRecipe? topRecipe) ||
            !_recipesBySymbol.TryGetValue(bottomDefinition.Symbol, out TRecipe? bottomRecipe))
        {
            return null;
        }

        return new ReadyAlphabetBookPage<TRecipe>(
            pageIndex,
            topDefinition,
            topRecipe,
            bottomDefinition,
            bottomRecipe);
    }

    public void MarkCompleted(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= NeonLetterSmallCatalog.All.Count / 2)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        _completedPageIndexes.Add(pageIndex);
    }

    public void Clear()
    {
        _recipesBySymbol.Clear();
        _completedPageIndexes.Clear();
    }

    internal AlphabetBookPageCoordinatorSnapshot<TRecipe> CaptureSnapshot()
    {
        return new AlphabetBookPageCoordinatorSnapshot<TRecipe>(
            new Dictionary<char, TRecipe>(_recipesBySymbol),
            new HashSet<int>(_completedPageIndexes));
    }

    internal AlphabetBookPageCoordinatorPlan<TRecipe> PrepareAdd(
        NeonLetterSmallDefinition definition,
        TRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(recipe);
        AlphabetBookPageCoordinatorSnapshot<TRecipe> originalSnapshot =
            CaptureSnapshot();
        var preparedCoordinator =
            new AlphabetBookPageCoordinator<TRecipe>();
        preparedCoordinator.Restore(originalSnapshot);
        ReadyAlphabetBookPage<TRecipe>? readyPage =
            preparedCoordinator.Add(definition, recipe);
        AlphabetBookPageCoordinatorSnapshot<TRecipe> addedRecipeSnapshot =
            preparedCoordinator.CaptureSnapshot();
        var readyPages = new List<ReadyAlphabetBookPage<TRecipe>>();
        while (readyPage != null)
        {
            readyPages.Add(readyPage);
            preparedCoordinator.MarkCompleted(readyPage.PageIndex);
            readyPage = preparedCoordinator.GetNextReadyPage();
        }

        return new AlphabetBookPageCoordinatorPlan<TRecipe>(
            originalSnapshot,
            addedRecipeSnapshot,
            readyPages);
    }

    internal void Restore(AlphabetBookPageCoordinatorSnapshot<TRecipe> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _recipesBySymbol.Clear();
        foreach ((char symbol, TRecipe recipe) in snapshot.RecipesBySymbol)
        {
            _recipesBySymbol.Add(symbol, recipe);
        }

        _completedPageIndexes.Clear();
        foreach (int pageIndex in snapshot.CompletedPageIndexes)
        {
            _completedPageIndexes.Add(pageIndex);
        }
    }
}

internal sealed class AlphabetBookPageCoordinatorSnapshot<TRecipe>
    where TRecipe : class
{
    public AlphabetBookPageCoordinatorSnapshot(
        IReadOnlyDictionary<char, TRecipe> recipesBySymbol,
        IReadOnlySet<int> completedPageIndexes)
    {
        RecipesBySymbol = recipesBySymbol;
        CompletedPageIndexes = completedPageIndexes;
    }

    public IReadOnlyDictionary<char, TRecipe> RecipesBySymbol { get; }
    public IReadOnlySet<int> CompletedPageIndexes { get; }
}

internal sealed class AlphabetBookPageCoordinatorPlan<TRecipe>
    where TRecipe : class
{
    public AlphabetBookPageCoordinatorPlan(
        AlphabetBookPageCoordinatorSnapshot<TRecipe> originalSnapshot,
        AlphabetBookPageCoordinatorSnapshot<TRecipe> addedRecipeSnapshot,
        IReadOnlyList<ReadyAlphabetBookPage<TRecipe>> readyPages)
    {
        OriginalSnapshot = originalSnapshot;
        AddedRecipeSnapshot = addedRecipeSnapshot;
        ReadyPages = readyPages;
    }

    public AlphabetBookPageCoordinatorSnapshot<TRecipe> OriginalSnapshot { get; }
    public AlphabetBookPageCoordinatorSnapshot<TRecipe> AddedRecipeSnapshot { get; }
    public IReadOnlyList<ReadyAlphabetBookPage<TRecipe>> ReadyPages { get; }
}

internal sealed class NeonLetterCallbackTransaction
{
    private readonly Stack<Action> _rollbacks = new();

    public static void Execute(Action<NeonLetterCallbackTransaction> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var transaction = new NeonLetterCallbackTransaction();
        try
        {
            callback(transaction);
        }
        catch (Exception exception)
        {
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(exception, rollbackException);
            }

            throw;
        }
    }

    public static void Execute(
        Action<NeonLetterCallbackTransaction> callback,
        Action finalCommit,
        Action cancelFinalCommit)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(finalCommit);
        ArgumentNullException.ThrowIfNull(cancelFinalCommit);
        var transaction = new NeonLetterCallbackTransaction();
        try
        {
            callback(transaction);
            finalCommit();
        }
        catch (Exception exception)
        {
            List<Exception>? rollbackExceptions = null;
            try
            {
                cancelFinalCommit();
            }
            catch (Exception cancelException)
            {
                (rollbackExceptions ??= new List<Exception>()).Add(
                    cancelException);
            }

            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackException)
            {
                (rollbackExceptions ??= new List<Exception>()).Add(
                    rollbackException);
            }

            if (rollbackExceptions != null)
            {
                rollbackExceptions.Insert(0, exception);
                throw new AggregateException(
                    "Blueprint callback and rollback did not complete cleanly.",
                    rollbackExceptions);
            }

            throw;
        }
    }

    public void Apply(Action mutation, Action rollback)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(rollback);
        _rollbacks.Push(rollback);
        mutation();
    }

    private void Rollback()
    {
        List<Exception>? rollbackExceptions = null;
        while (_rollbacks.TryPop(out Action? rollback))
        {
            try
            {
                rollback();
            }
            catch (Exception exception)
            {
                (rollbackExceptions ??= new List<Exception>()).Add(exception);
            }
        }

        if (rollbackExceptions != null)
        {
            throw new AggregateException(
                "Blueprint callback rollback did not complete cleanly.",
                rollbackExceptions);
        }
    }
}

public static class BookPageRegistrar
{
    public static void Register<TRecipe, TTexture>(
        string titleLocalizationKey,
        string title,
        TRecipe topRecipe,
        string topRecipeTitle,
        TRecipe? bottomRecipe,
        string? bottomRecipeTitle,
        TTexture background,
        IBookPageRegistrationTarget<TRecipe, TTexture> target)
        where TRecipe : class
        where TTexture : class
    {
        RequireText(titleLocalizationKey, nameof(titleLocalizationKey));
        RequireText(title, nameof(title));
        ArgumentNullException.ThrowIfNull(topRecipe);
        RequireText(topRecipeTitle, nameof(topRecipeTitle));
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(target);

        if (bottomRecipe != null && string.IsNullOrWhiteSpace(bottomRecipeTitle))
        {
            throw new ArgumentException(
                "A bottom recipe title is required when a bottom recipe is supplied.",
                nameof(bottomRecipeTitle));
        }

        if (target is
            ITransactionalBookPageRegistrationTarget<TRecipe, TTexture>
            transactionalTarget)
        {
            object snapshot = transactionalTarget.CaptureRegistrationState(
                titleLocalizationKey,
                topRecipe,
                bottomRecipe);
            try
            {
                RegisterCore(
                    titleLocalizationKey,
                    title,
                    topRecipe,
                    topRecipeTitle,
                    bottomRecipe,
                    bottomRecipeTitle,
                    background,
                    target);
            }
            catch (Exception exception)
            {
                try
                {
                    transactionalTarget.RestoreRegistrationState(snapshot);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(exception, rollbackException);
                }

                throw;
            }

            return;
        }

        RegisterCore(
            titleLocalizationKey,
            title,
            topRecipe,
            topRecipeTitle,
            bottomRecipe,
            bottomRecipeTitle,
            background,
            target);
    }

    private static void RegisterCore<TRecipe, TTexture>(
        string titleLocalizationKey,
        string title,
        TRecipe topRecipe,
        string topRecipeTitle,
        TRecipe? bottomRecipe,
        string? bottomRecipeTitle,
        TTexture background,
        IBookPageRegistrationTarget<TRecipe, TTexture> target)
        where TRecipe : class
        where TTexture : class
    {
        bool matchingPageAlreadyExists = target.LastPageMatches(
            topRecipe,
            bottomRecipe,
            background,
            titleLocalizationKey);
        if (!matchingPageAlreadyExists)
        {
            int previousPageCount = target.PageCount;
            target.CreatePage(topRecipe, bottomRecipe, background, titleLocalizationKey);

            if (target.PageCount != previousPageCount + 1 ||
                !target.LastPageMatches(
                    topRecipe,
                    bottomRecipe,
                    background,
                    titleLocalizationKey))
            {
                throw new InvalidOperationException(
                    "Blueprint book page was not registered as requested.");
            }
        }

        target.AddLocalization(titleLocalizationKey, title);
        target.AddLocalization(target.GetRecipeLocalizationId(topRecipe), topRecipeTitle);
        if (bottomRecipe != null)
        {
            target.AddLocalization(
                target.GetRecipeLocalizationId(bottomRecipe),
                bottomRecipeTitle!);
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }
}
