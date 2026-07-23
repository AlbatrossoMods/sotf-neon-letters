using Sons.Crafting;
using Sons.Crafting.Structures;
using Sons.Weapon;
using SonsSdk;
using SonsSdk.Building;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SOTFNeonLetters;

public static class NeonLetterSmallBlueprint
{
    private static readonly IReadOnlyDictionary<int, NeonLetterSmallDefinition>
        DefinitionsByRecipeId = NeonLetterSmallCatalog.All.ToDictionary(
            definition => definition.RecipeId);
    private static readonly AlphabetBookPageCoordinator<StructureRecipe>
        BookPageCoordinator = new();
    private static bool _callbackSubscribed;
    private static bool _registrationAttempted;
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        if (_registrationAttempted)
        {
            throw new InvalidOperationException(
                "A previous SonsSdk registration attempt for the Small neon symbol catalog failed " +
                "after entering TryRegister. SonsSdk may retain registration state; process " +
                "restart is required.");
        }

        var preparedLetters = new List<PreparedLetter>(NeonLetterSmallCatalog.All.Count);
        ScrewStructureRegistration[] registrations =
            CustomBlueprintManager.GetRegistrations().ToArray();
        foreach (NeonLetterSmallDefinition definition in NeonLetterSmallCatalog.All)
        {
            GameObject prefab = ValidateAssets(definition);
            List<(NeonLetterSmallDefinition.IngredientDefinition Definition, Transform Transform)>
                ingredientTargets = ValidateIngredientChildren(prefab, definition);
            ScrewStructureRegistration collision = registrations.FirstOrDefault(
                registration => registration.recipeId == definition.RecipeId);
            if (collision != null)
            {
                throw new InvalidOperationException(
                    $"Cannot register {definition.PrefabAssetName}: recipe ID " +
                    $"{definition.RecipeId} (crafting-node ID {definition.CraftingNodeId}) " +
                    $"is already registered by '{collision.recipeName}'.");
            }

            preparedLetters.Add(new PreparedLetter(definition, prefab, ingredientTargets));
        }

        foreach (PreparedLetter preparedLetter in preparedLetters)
        {
            ReplaceBundleShaders(preparedLetter.Prefab, preparedLetter.Definition);
        }

        var addedIngredients = new List<StructureCraftingNodeIngredient>();
        bool callbackSubscribedByThisCall = false;
        try
        {
            foreach (PreparedLetter preparedLetter in preparedLetters)
            {
                foreach ((NeonLetterSmallDefinition.IngredientDefinition ingredientDefinition,
                             Transform target) in preparedLetter.IngredientTargets)
                {
                    StructureCraftingNodeIngredient ingredient =
                        target.gameObject.AddComponent<StructureCraftingNodeIngredient>();
                    addedIngredients.Add(ingredient);
                    ingredient.SetId(ingredientDefinition.ItemId);
                }
            }

            if (!_callbackSubscribed)
            {
                CustomBlueprintManager.OnCraftingNodeCreated.Subscribe(OnCraftingNodeCreated);
                callbackSubscribedByThisCall = true;
                _callbackSubscribed = true;
            }
        }
        catch (Exception exception)
        {
            Exception cleanupException = null;
            if (callbackSubscribedByThisCall)
            {
                try
                {
                    CustomBlueprintManager.OnCraftingNodeCreated.Unsubscribe(OnCraftingNodeCreated);
                }
                catch (Exception unsubscribeException)
                {
                    cleanupException = unsubscribeException;
                }
                finally
                {
                    _callbackSubscribed = false;
                }
            }

            _registered = false;
            BookPageCoordinator.Clear();

            for (int ingredientIndex = addedIngredients.Count - 1;
                 ingredientIndex >= 0;
                 ingredientIndex--)
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(addedIngredients[ingredientIndex]);
                }
                catch (Exception destroyException)
                {
                    cleanupException = cleanupException == null
                        ? destroyException
                        : new AggregateException(cleanupException, destroyException);
                }
            }

            Exception registrationException = cleanupException == null
                ? exception
                : new AggregateException(exception, cleanupException);
            throw new InvalidOperationException(
                "Pre-SDK registration setup failed for the Small neon symbol catalog; ingredient " +
                "components and the callback subscription from this call were rolled back.",
                registrationException);
        }

        _registrationAttempted = true;
        try
        {
            foreach (PreparedLetter preparedLetter in preparedLetters)
            {
                CustomBlueprintManager.TryRegister(
                    new ScrewStructureRegistration(
                        preparedLetter.Prefab,
                        preparedLetter.Definition.RecipeId,
                        preparedLetter.Definition.RecipeName));
            }

            _registered = true;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "SonsSdk TryRegister failed while registering the Small neon symbol catalog. SonsSdk " +
                "may retain partial registration state; ingredient components and the callback " +
                "subscription were preserved, and a process restart is required before another " +
                "registration attempt.",
                exception);
        }
    }

    private static GameObject ValidateAssets(NeonLetterSmallDefinition definition)
    {
        GameObject prefab = Assets.GetPrefab(definition.Symbol);
        if (prefab == null)
        {
            throw new InvalidOperationException(
                $"Asset '{definition.PrefabAssetName}' was not loaded from bundle " +
                $"'{NeonLetterSmallCatalog.BundleName}'.");
        }

        if (!string.Equals(
                prefab.name,
                definition.PrefabAssetName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Loaded prefab name '{prefab.name}' does not match required asset name " +
                $"'{definition.PrefabAssetName}'.");
        }

        Texture2D bookPage = Assets.GetBookPage(definition.BookPageIndex);
        if (bookPage == null)
        {
            throw new InvalidOperationException(
                $"Asset '{definition.BookPageAssetName}' was not loaded from bundle " +
                $"'{NeonLetterSmallCatalog.BundleName}'.");
        }

        if (bookPage.width != NeonLetterSmallCatalog.BookPageWidth ||
            bookPage.height != NeonLetterSmallCatalog.BookPageHeight ||
            bookPage.mipmapCount != NeonLetterSmallCatalog.BookPageMipCount)
        {
            throw new InvalidOperationException(
                $"Book page '{definition.BookPageAssetName}' must be " +
                $"{NeonLetterSmallCatalog.BookPageWidth}x" +
                $"{NeonLetterSmallCatalog.BookPageHeight} with " +
                $"{NeonLetterSmallCatalog.BookPageMipCount} mip levels, but is " +
                $"{bookPage.width}x{bookPage.height} with {bookPage.mipmapCount} mip levels.");
        }

        Texture2D bookIcon = Assets.GetBookIcon(definition.Symbol);
        if (bookIcon == null)
        {
            throw new InvalidOperationException(
                $"Asset '{definition.BookIconAssetName}' was not loaded from bundle " +
                $"'{NeonLetterSmallCatalog.BundleName}'.");
        }

        if (bookIcon.width != NeonLetterSmallCatalog.BookIconSize ||
            bookIcon.height != NeonLetterSmallCatalog.BookIconSize ||
            bookIcon.mipmapCount != NeonLetterSmallCatalog.BookIconMipCount)
        {
            throw new InvalidOperationException(
                $"Book icon '{definition.BookIconAssetName}' must be " +
                $"{NeonLetterSmallCatalog.BookIconSize}x" +
                $"{NeonLetterSmallCatalog.BookIconSize} with " +
                $"{NeonLetterSmallCatalog.BookIconMipCount} mip levels, but is " +
                $"{bookIcon.width}x{bookIcon.height} with {bookIcon.mipmapCount} mip levels.");
        }

        return prefab;
    }

    private static void ReplaceBundleShaders(
        GameObject prefab,
        NeonLetterSmallDefinition definition)
    {
        Shader gameShader = Shader.Find(ShaderAssetMap.HDRPLit);
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        var rendererHandles = new List<IRuntimeRendererHandle>(renderers.Length);
        foreach (Renderer renderer in renderers)
        {
            rendererHandles.Add(new UnityRuntimeRendererHandle(renderer));
        }

        RuntimeMaterialReplacement.ReplaceAll(
            prefab.name,
            rendererHandles,
            new UnityRuntimeMaterialFactory(gameShader));
        ValidateRuntimeLetterMaterial(prefab, gameShader, definition);
    }

    private static List<(
        NeonLetterSmallDefinition.IngredientDefinition Definition,
        Transform Transform)> ValidateIngredientChildren(
            GameObject prefab,
            NeonLetterSmallDefinition letterDefinition)
    {
        var targets = new List<(
            NeonLetterSmallDefinition.IngredientDefinition Definition,
            Transform Transform)>();

        foreach (NeonLetterSmallDefinition.IngredientDefinition ingredientDefinition
                 in letterDefinition.Ingredients)
        {
            Transform matchingChild = null;
            int matchCount = 0;
            for (int childIndex = 0; childIndex < prefab.transform.childCount; childIndex++)
            {
                Transform child = prefab.transform.GetChild(childIndex);
                if (!string.Equals(
                        child.name,
                        ingredientDefinition.ChildName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                matchingChild = child;
                matchCount++;
            }

            if (matchCount != 1)
            {
                throw new InvalidOperationException(
                    $"Prefab '{letterDefinition.PrefabAssetName}' must contain exactly one " +
                    $"top-level child named '{ingredientDefinition.ChildName}', but found " +
                    $"{matchCount}.");
            }

            if (matchingChild.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                throw new InvalidOperationException(
                    $"Ingredient child '{ingredientDefinition.ChildName}' must contain a " +
                    "visible renderer.");
            }

            if (matchingChild.GetComponent<StructureCraftingNodeIngredient>() != null)
            {
                throw new InvalidOperationException(
                    $"Ingredient child '{ingredientDefinition.ChildName}' already has a " +
                    $"{nameof(StructureCraftingNodeIngredient)} before registration.");
            }

            targets.Add((ingredientDefinition, matchingChild));
        }

        return targets;
    }

    private static void OnCraftingNodeCreated(StructureCraftingNode craftingNode)
    {
        StructureRecipe recipe = craftingNode?.Recipe;
        if (recipe == null ||
            !DefinitionsByRecipeId.TryGetValue(
                recipe.Id,
                out NeonLetterSmallDefinition definition))
        {
            return;
        }

        GameObject craftingNodeObject = craftingNode.gameObject;
        craftingNodeObject.SetActive(false);
        try
        {
            RecipePlacementApplicator.Apply(
                NeonLetterSmallCatalog.Placement,
                new SonsRecipePlacementTarget(craftingNode, recipe));

            FitBuiltPrefabCollider(recipe, definition);
            FitCraftingNodeCollider(craftingNode, definition);
            recipe._recipeImage = Assets.GetBookIcon(definition.Symbol);
        }
        finally
        {
            craftingNodeObject.SetActive(true);
        }

        ReadyAlphabetBookPage<StructureRecipe> readyPage =
            BookPageCoordinator.Add(definition, recipe);
        while (readyPage != null)
        {
            CreateBookPage(readyPage);
            BookPageCoordinator.MarkCompleted(readyPage.PageIndex);
            readyPage = BookPageCoordinator.GetNextReadyPage();
        }
    }

    private static void CreateBookPage(
        ReadyAlphabetBookPage<StructureRecipe> readyPage)
    {
        Texture2D bookPage = Assets.GetBookPage(readyPage.PageIndex);
        if (bookPage == null)
        {
            throw new InvalidOperationException(
                $"Cannot create the blueprint book page because asset " +
                $"'{readyPage.TopDefinition.BookPageAssetName}' is not loaded.");
        }

        BookPageRegistrar.Register(
            readyPage.TopDefinition.BookPageTitleLocalizationKey,
            NeonLetterSmallCatalog.BookPageTitle,
            readyPage.TopRecipe,
            readyPage.TopDefinition.RecipeName,
            readyPage.BottomRecipe,
            readyPage.BottomDefinition.RecipeName,
            bookPage,
            new SonsBookPageRegistrationTarget());
    }

    private static void FitBuiltPrefabCollider(
        StructureRecipe recipe,
        NeonLetterSmallDefinition definition)
    {
        GameObject builtPrefab = recipe._builtPrefab;
        if (builtPrefab == null)
        {
            throw new InvalidOperationException(
                $"Recipe {recipe.Id} has no built prefab to size from its visible geometry.");
        }

        Transform visualRoot = FindColliderVisualRoot(builtPrefab, definition);
        Bounds visualBounds = CalculateLocalRendererBounds(builtPrefab, visualRoot);
        BoxCollider collider = builtPrefab.GetComponent<BoxCollider>();
        if (collider == null)
        {
            throw new InvalidOperationException(
                $"Built prefab '{builtPrefab.name}' has no SonsSdk root BoxCollider to resize.");
        }

        ApplyBounds(collider, visualBounds, definition);
    }

    private static void FitCraftingNodeCollider(
        StructureCraftingNode craftingNode,
        NeonLetterSmallDefinition definition)
    {
        BoxCollider collider = craftingNode.GetComponent<BoxCollider>();
        if (collider == null)
        {
            return;
        }

        GameObject craftingNodeObject = craftingNode.gameObject;
        Transform visualRoot = FindColliderVisualRoot(craftingNodeObject, definition);
        ApplyBounds(
            collider,
            CalculateLocalRendererBounds(craftingNodeObject, visualRoot),
            definition);
    }

    private static void ApplyBounds(
        BoxCollider collider,
        Bounds visualBounds,
        NeonLetterSmallDefinition definition)
    {
        collider.center = visualBounds.center;
        collider.size = CreateColliderSize(visualBounds.size, definition);
    }

    private static Vector3 CreateColliderSize(
        Vector3 visualSize,
        NeonLetterSmallDefinition definition)
    {
        NeonLetterSmallDefinition.ColliderSize colliderSize =
            NeonLetterColliderPolicy.Resolve(
                definition,
                visualSize.x,
                visualSize.y,
                visualSize.z);
        return new Vector3(colliderSize.Width, colliderSize.Height, colliderSize.Depth);
    }

    private static Transform FindColliderVisualRoot(
        GameObject root,
        NeonLetterSmallDefinition definition)
    {
        Transform matchingTransform = null;
        int matchCount = 0;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            if (!definition.IsColliderVisualChild(transform.name))
            {
                continue;
            }

            matchingTransform = transform;
            matchCount++;
        }

        if (matchCount != 1)
        {
            throw new InvalidOperationException(
                $"GameObject '{root.name}' must contain exactly one collider visual child named " +
                $"'{definition.ColliderVisualChildName}', but found " +
                $"{matchCount}.");
        }

        return matchingTransform;
    }

    private static Bounds CalculateLocalRendererBounds(GameObject root, Transform visualRoot)
    {
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            throw new InvalidOperationException(
                $"Collider visual child '{visualRoot.name}' under '{root.name}' has no renderers " +
                "to define placement bounds.");
        }

        bool hasPoint = false;
        Bounds localBounds = default;
        foreach (Renderer renderer in renderers)
        {
            Bounds worldBounds = renderer.bounds;
            Vector3 minimum = worldBounds.min;
            Vector3 maximum = worldBounds.max;

            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 worldPoint = new(
                            x == 0 ? minimum.x : maximum.x,
                            y == 0 ? minimum.y : maximum.y,
                            z == 0 ? minimum.z : maximum.z);
                        Vector3 localPoint = root.transform.InverseTransformPoint(worldPoint);
                        if (!hasPoint)
                        {
                            localBounds = new Bounds(localPoint, Vector3.zero);
                            hasPoint = true;
                        }
                        else
                        {
                            localBounds.Encapsulate(localPoint);
                        }
                    }
                }
            }
        }

        return localBounds;
    }

    private static void ValidateRuntimeLetterMaterial(
        GameObject prefab,
        Shader expectedShader,
        NeonLetterSmallDefinition definition)
    {
        Transform letter = FindColliderVisualRoot(prefab, definition);
        Renderer[] renderers = letter.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Il2CppReferenceArray<Material> materials = renderer.sharedMaterials;
            foreach (Material material in materials)
            {
                if (material == null || material.shader != expectedShader)
                {
                    throw new InvalidOperationException(
                        $"Runtime letter renderer '{renderer.name}' did not retain the game " +
                        $"shader '{expectedShader.name}'.");
                }

                if (!material.HasProperty("_EmissiveColorMap") ||
                    material.GetTexture("_EmissiveColorMap") == null ||
                    !material.HasProperty("_EmissiveIntensity") ||
                    material.GetFloat("_EmissiveIntensity") <= 0f ||
                    !material.HasProperty("_DoubleSidedEnable") ||
                    material.GetFloat("_DoubleSidedEnable") < 0.5f)
                {
                    throw new InvalidOperationException(
                        $"Runtime letter material '{material.name}' lost its visible emissive " +
                        "or double-sided properties during shader replacement.");
                }
            }
        }
    }

    private sealed class PreparedLetter
    {
        public PreparedLetter(
            NeonLetterSmallDefinition definition,
            GameObject prefab,
            List<(
                NeonLetterSmallDefinition.IngredientDefinition Definition,
                Transform Transform)> ingredientTargets)
        {
            Definition = definition;
            Prefab = prefab;
            IngredientTargets = ingredientTargets;
        }

        public NeonLetterSmallDefinition Definition { get; }
        public GameObject Prefab { get; }
        public List<(
            NeonLetterSmallDefinition.IngredientDefinition Definition,
            Transform Transform)> IngredientTargets { get; }
    }

    private sealed class SonsRecipePlacementTarget : IRecipePlacementTarget
    {
        private readonly StructureCraftingNode _craftingNode;
        private readonly StructureRecipe _recipe;

        public SonsRecipePlacementTarget(
            StructureCraftingNode craftingNode,
            StructureRecipe recipe)
        {
            _craftingNode = craftingNode;
            _recipe = recipe;
        }

        public bool GroundPlacementChecksRemoved =>
            _craftingNode.GroundOffsetProvider == null &&
            _craftingNode.GroundPresenceProvider == null &&
            _craftingNode.GetComponent<GroundOffsetProvider>() == null;

        public bool ParentRecipeOverridesCleared =>
            _recipe._dynamicParentRecipeOverride == null &&
            _recipe._screwParentRecipeOverride == null &&
            _recipe._freeformParentRecipeOverride == null;

        public NeonLetterASmallDefinition.PlacementDefinition Snapshot => new(
            _recipe._anchor switch
            {
                StructureRecipe.AnchorType.Back =>
                    NeonLetterASmallDefinition.PlacementAnchor.Back,
                _ => throw new InvalidOperationException(
                    $"Unsupported applied placement anchor '{_recipe._anchor}'.")
            },
            _recipe._castRadiusFormula switch
            {
                StructureRecipe.CastRadiusFormulas.Z =>
                    NeonLetterASmallDefinition.PlacementCastRadiusFormula.Z,
                _ => throw new InvalidOperationException(
                    $"Unsupported applied cast-radius formula " +
                    $"'{_recipe._castRadiusFormula}'.")
            },
            _recipe._alignToSurface,
            _recipe._canBeRotated,
            _recipe._forceUp,
            _recipe._lockUpwardVector,
            _recipe._initialPlacementRotationOffset.x,
            _recipe._initialPlacementRotationOffset.y,
            _recipe._initialPlacementRotationOffset.z,
            _recipe._allowsTreePlacement,
            _recipe._allowsNonTreePlacement,
            _recipe._minHeightAboveTree,
            _recipe._maxHeightAboveTree,
            _recipe._allowParentingWithDynamicObjects,
            _recipe._allowParentingWithScrewStructures,
            _recipe._allowParentingWithFreeFormStructures,
            _recipe._useOverridePlacementSize,
            _recipe._placementDepthSizeRatio);

        public NeonLetterASmallDefinition.PlacementAnchor Anchor
        {
            set => _recipe._anchor = value switch
            {
                NeonLetterASmallDefinition.PlacementAnchor.Back =>
                    StructureRecipe.AnchorType.Back,
                _ => throw new InvalidOperationException(
                    $"Unsupported placement anchor '{value}'.")
            };
        }

        public NeonLetterASmallDefinition.PlacementCastRadiusFormula CastRadiusFormula
        {
            set => _recipe._castRadiusFormula = value switch
            {
                NeonLetterASmallDefinition.PlacementCastRadiusFormula.Z =>
                    StructureRecipe.CastRadiusFormulas.Z,
                _ => throw new InvalidOperationException(
                    $"Unsupported cast-radius formula '{value}'.")
            };
        }

        public bool AlignToSurface { set => _recipe._alignToSurface = value; }
        public bool CanBeRotated { set => _recipe._canBeRotated = value; }
        public bool ForceUp { set => _recipe._forceUp = value; }
        public bool LockUpwardVector { set => _recipe._lockUpwardVector = value; }
        public bool AllowsTreePlacement { set => _recipe._allowsTreePlacement = value; }
        public bool AllowsNonTreePlacement { set => _recipe._allowsNonTreePlacement = value; }
        public float MinimumHeightAboveTree { set => _recipe._minHeightAboveTree = value; }
        public float MaximumHeightAboveTree { set => _recipe._maxHeightAboveTree = value; }

        public bool AllowDynamicObjectParenting
        {
            set
            {
                _recipe._allowParentingWithDynamicObjects = value;
                _recipe._dynamicParentRecipeOverride = null;
            }
        }

        public bool AllowScrewStructureParenting
        {
            set
            {
                _recipe._allowParentingWithScrewStructures = value;
                _recipe._screwParentRecipeOverride = null;
            }
        }

        public bool AllowFreeFormStructureParenting
        {
            set
            {
                _recipe._allowParentingWithFreeFormStructures = value;
                _recipe._freeformParentRecipeOverride = null;
            }
        }

        public bool UseOverridePlacementSize { set => _recipe._useOverridePlacementSize = value; }
        public float PlacementDepthSizeRatio { set => _recipe._placementDepthSizeRatio = value; }

        public void RemoveGroundPlacementChecks()
        {
            GroundOffsetProvider groundProvider =
                _craftingNode.GroundPresenceProvider ??
                _craftingNode.GetComponent<GroundOffsetProvider>();
            _craftingNode.GroundOffsetProvider = null;
            _craftingNode.GroundPresenceProvider = null;
            if (groundProvider != null)
            {
                UnityEngine.Object.DestroyImmediate(groundProvider);
            }
        }

        public void SetInitialRotation(float x, float y, float z)
        {
            _recipe._initialPlacementRotationOffset = new Vector3(x, y, z);
        }
    }

    private sealed class UnityRuntimeMaterialFactory : IRuntimeMaterialFactory
    {
        private readonly Shader _shader;

        public UnityRuntimeMaterialFactory(Shader shader)
        {
            _shader = shader;
        }

        public string ShaderName => _shader == null ? ShaderAssetMap.HDRPLit : _shader.name;
        public bool IsShaderSupported => _shader != null && _shader.isSupported;

        public IRuntimeMaterialHandle Create()
        {
            return new UnityRuntimeMaterialHandle(new Material(_shader), _shader);
        }
    }

    private sealed class UnityRuntimeRendererHandle : IRuntimeRendererHandle
    {
        private readonly Renderer _renderer;

        public UnityRuntimeRendererHandle(Renderer renderer)
        {
            _renderer = renderer;
        }

        public string Name => _renderer.name;

        public IReadOnlyList<IRuntimeMaterialHandle> Materials
        {
            get
            {
                Il2CppReferenceArray<Material> materials = _renderer.sharedMaterials;
                if (materials == null)
                {
                    return Array.Empty<IRuntimeMaterialHandle>();
                }

                var handles = new IRuntimeMaterialHandle[materials.Length];
                for (int index = 0; index < materials.Length; index++)
                {
                    Material material = materials[index];
                    handles[index] = material == null
                        ? null
                        : new UnityRuntimeMaterialHandle(material, null);
                }

                return handles;
            }
        }

        public void SetMaterials(IReadOnlyList<IRuntimeMaterialHandle> materials)
        {
            var runtimeMaterials = new Il2CppReferenceArray<Material>(materials.Count);
            for (int index = 0; index < materials.Count; index++)
            {
                runtimeMaterials[index] = ((UnityRuntimeMaterialHandle)materials[index]).Material;
            }

            _renderer.sharedMaterials = runtimeMaterials;
        }
    }

    private sealed class UnityRuntimeMaterialHandle : IRuntimeMaterialHandle
    {
        private readonly Shader _runtimeShader;

        public UnityRuntimeMaterialHandle(Material material, Shader runtimeShader)
        {
            Material = material;
            _runtimeShader = runtimeShader;
        }

        public Material Material { get; }
        public string Name { get => Material.name; set => Material.name = value; }
        public string ShaderName => Material.shader == null ? string.Empty : Material.shader.name;
        public int RenderQueue { get => Material.renderQueue; set => Material.renderQueue = value; }

        public object ShaderKeywords
        {
            get => new UnityShaderKeywordSnapshot(Material);
            set => ((UnityShaderKeywordSnapshot)value).ApplyTo(Material);
        }

        public void CopyPropertiesFrom(IRuntimeMaterialHandle source)
        {
            Material.CopyPropertiesFromMaterial(((UnityRuntimeMaterialHandle)source).Material);
            if (_runtimeShader != null)
            {
                Material.shader = _runtimeShader;
            }
        }
    }

    private sealed class UnityShaderKeywordSnapshot
    {
        private readonly Material _source;

        public UnityShaderKeywordSnapshot(Material source)
        {
            _source = source;
        }

        public void ApplyTo(Material target)
        {
            target.shaderKeywords = _source.shaderKeywords;
        }
    }

    private sealed class SonsBookPageRegistrationTarget :
        IBookPageRegistrationTarget<StructureRecipe, Texture2D>
    {
        public int PageCount => GetController()._pages._pages.Count;

        public void AddLocalization(string key, string value)
        {
            SonsSdk.LocalizationTools.ItemsTable.AddEntry(key, value);
        }

        public string GetRecipeLocalizationId(StructureRecipe recipe)
        {
            return recipe._localizationId;
        }

        public void CreatePage(
            StructureRecipe topRecipe,
            StructureRecipe bottomRecipe,
            Texture2D background,
            string titleLocalizationKey)
        {
            string previousLocalizationString = CustomBlueprintManager.NextLocalizationString;
            try
            {
                CustomBlueprintManager.NextLocalizationString = titleLocalizationKey;
                CustomBlueprintManager.CreateBookPage(topRecipe, bottomRecipe, background);
            }
            finally
            {
                CustomBlueprintManager.NextLocalizationString = previousLocalizationString;
            }
        }

        public bool LastPageMatches(
            StructureRecipe topRecipe,
            StructureRecipe bottomRecipe,
            Texture2D background,
            string titleLocalizationKey)
        {
            BlueprintBookController controller = GetController();
            if (controller._pages._pages.Count == 0)
            {
                return false;
            }

            BlueprintBookPageData page =
                controller._pages._pages[controller._pages._pages.Count - 1];
            return page._topRecipe == topRecipe &&
                   page._bottomRecipe == bottomRecipe &&
                   page._pageImage == background &&
                   string.Equals(
                       page._pageTitleLocalizationId,
                       titleLocalizationKey,
                       StringComparison.Ordinal);
        }

        private static BlueprintBookController GetController()
        {
            Transform book = ItemTools.GetHeldPrefab(552);
            BlueprintBookController controller =
                book == null ? null : book.GetComponent<BlueprintBookController>();
            if (controller == null || controller._pages == null || controller._pages._pages == null)
            {
                throw new InvalidOperationException(
                    "The blueprint book controller is unavailable while registering Neon Symbols.");
            }

            return controller;
        }
    }
}
