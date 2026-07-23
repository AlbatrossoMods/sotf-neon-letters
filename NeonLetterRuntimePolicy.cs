#nullable enable

namespace SOTFNeonLetters;

public interface IRecipePlacementTarget
{
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
    bool UseOverridePlacementSize { set; }
    float PlacementDepthSizeRatio { set; }

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
        target.UseOverridePlacementSize = placement.UseOverridePlacementSize;
        target.PlacementDepthSizeRatio = placement.PlacementDepthSizeRatio;

        if (!target.GroundPlacementChecksRemoved ||
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

        if (!materialFactory.IsShaderSupported)
        {
            throw new InvalidOperationException(
                $"The game shader '{materialFactory.ShaderName}' is unavailable or unsupported; " +
                $"cannot replace stripped shaders in prefab '{prefabName}'.");
        }

        if (renderers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Prefab '{prefabName}' has no renderers whose bundle shaders can be replaced.");
        }

        foreach (IRuntimeRendererHandle renderer in renderers)
        {
            IReadOnlyList<IRuntimeMaterialHandle> sourceMaterials = renderer.Materials;
            if (sourceMaterials == null || sourceMaterials.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.Name}' in prefab '{prefabName}' has no source materials.");
            }

            var runtimeMaterials = new IRuntimeMaterialHandle[sourceMaterials.Count];
            for (int materialIndex = 0; materialIndex < sourceMaterials.Count; materialIndex++)
            {
                IRuntimeMaterialHandle sourceMaterial = sourceMaterials[materialIndex];
                if (sourceMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.Name}' in prefab '{prefabName}' has a null " +
                        $"material at index {materialIndex}.");
                }

                IRuntimeMaterialHandle runtimeMaterial = materialFactory.Create();
                runtimeMaterial.CopyPropertiesFrom(sourceMaterial);
                runtimeMaterial.Name = $"{sourceMaterial.Name}_Runtime";
                runtimeMaterial.ShaderKeywords = sourceMaterial.ShaderKeywords;
                runtimeMaterial.RenderQueue = sourceMaterial.RenderQueue;
                runtimeMaterials[materialIndex] = runtimeMaterial;
            }

            renderer.SetMaterials(runtimeMaterials);

            IReadOnlyList<IRuntimeMaterialHandle> assignedMaterials = renderer.Materials;
            if (assignedMaterials == null || assignedMaterials.Count != runtimeMaterials.Length)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.Name}' in prefab '{prefabName}' did not retain all " +
                    "runtime material assignments.");
            }

            for (int materialIndex = 0; materialIndex < assignedMaterials.Count; materialIndex++)
            {
                IRuntimeMaterialHandle assigned = assignedMaterials[materialIndex];
                IRuntimeMaterialHandle expected = runtimeMaterials[materialIndex];
                if (assigned == null ||
                    !string.Equals(
                        assigned.ShaderName,
                        materialFactory.ShaderName,
                        StringComparison.Ordinal) ||
                    !string.Equals(assigned.Name, expected.Name, StringComparison.Ordinal) ||
                    assigned.RenderQueue != expected.RenderQueue)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.Name}' in prefab '{prefabName}' did not retain " +
                        $"runtime material slot {materialIndex} with shader " +
                        $"'{materialFactory.ShaderName}'.");
                }
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
