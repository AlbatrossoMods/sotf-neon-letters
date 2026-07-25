using RedLoader;
using Sons.Crafting;
using Sons.Crafting.Structures;
using Sons.Weapon;
using SonsSdk;
using SonsSdk.Building;
using UnityEngine;
using UnityEngine.Localization.Tables;
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
            SubscribeCallback();
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

        Shader gameShader = null;
        var materialEntries =
            new List<RuntimeMaterialCatalogEntry>(preparedLetters.Count);
        foreach (PreparedLetter preparedLetter in preparedLetters)
        {
            materialEntries.Add(
                CreateRuntimeMaterialEntry(
                    preparedLetter,
                    () => gameShader));
        }

        var materialTransaction = new RuntimeMaterialCatalogTransaction(
            () =>
            {
                gameShader = GameResources.GetShader(ShaderAssetMap.HDRPLit);
                return new UnityRuntimeMaterialFactory(gameShader);
            },
            materialEntries);
        RuntimeMaterialReplacementLease materialLease =
            materialTransaction.Execute();

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

            try
            {
                materialLease.Rollback();
            }
            catch (Exception materialRollbackException)
            {
                cleanupException = cleanupException == null
                    ? materialRollbackException
                    : new AggregateException(
                        cleanupException,
                        materialRollbackException);
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
        materialLease.Retain();
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
            Deinitialize();
            throw new InvalidOperationException(
                "SonsSdk TryRegister failed while registering the Small neon symbol catalog. SonsSdk " +
                "may retain partial registration state; registered recipe and material state was " +
                "preserved, the callback subscription was removed, and a process restart is " +
                "required before another registration attempt.",
                exception);
        }
    }

    internal static void Deinitialize()
    {
        BookPageCoordinator.Clear();
        if (!_callbackSubscribed)
        {
            return;
        }

        try
        {
            CustomBlueprintManager.OnCraftingNodeCreated.Unsubscribe(
                OnCraftingNodeCreated);
        }
        catch (Exception exception)
        {
            try
            {
                RLog.Error(
                    $"[SOTFNeonLetters] Blueprint callback cleanup failed: " +
                    exception);
            }
            catch
            {
                // Callback teardown must remain safe during mod deinitialization.
            }
        }
        finally
        {
            _callbackSubscribed = false;
        }
    }

    private static void SubscribeCallback()
    {
        if (_callbackSubscribed)
        {
            return;
        }

        CustomBlueprintManager.OnCraftingNodeCreated.Subscribe(
            OnCraftingNodeCreated);
        _callbackSubscribed = true;
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

    private static RuntimeMaterialCatalogEntry CreateRuntimeMaterialEntry(
        PreparedLetter preparedLetter,
        Func<Shader> shaderAccessor)
    {
        Renderer[] renderers =
            preparedLetter.Prefab.GetComponentsInChildren<Renderer>(true);
        var rendererHandles = new List<IRuntimeRendererHandle>(renderers.Length);
        foreach (Renderer renderer in renderers)
        {
            rendererHandles.Add(new UnityRuntimeRendererHandle(renderer));
        }

        return new RuntimeMaterialCatalogEntry(
            preparedLetter.Prefab.name,
            rendererHandles,
            () => ValidateRuntimeLetterMaterial(
                preparedLetter.Prefab,
                shaderAccessor(),
                preparedLetter.Definition));
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
        bool wasActive = craftingNodeObject.activeSelf;
        var placementTarget =
            new SonsRecipePlacementTarget(craftingNode, recipe);
        Action restorePlacement =
            CapturePlacementRestoreAction(craftingNode, recipe);
        PreparedColliderMutation builtCollider =
            PrepareBuiltPrefabCollider(recipe, definition);
        PreparedColliderMutation craftingNodeCollider =
            PrepareCraftingNodeCollider(craftingNode, definition);
        Texture2D recipeImage = Assets.GetBookIcon(definition.Symbol);
        if (recipeImage == null)
        {
            throw new InvalidOperationException(
                $"Cannot configure recipe {recipe.Id} because book icon asset " +
                $"'{definition.BookIconAssetName}' is not loaded.");
        }

        Texture previousRecipeImage = recipe._recipeImage;
        AlphabetBookPageCoordinatorPlan<StructureRecipe> coordinatorPlan =
            PrepareCoordinatorPlan(definition, recipe);
        PreparedBookPageBatch preparedBookPages =
            PreparedBookPageBatch.Prepare(coordinatorPlan.ReadyPages);
        NeonLetterCallbackTransaction.Execute(
            transaction =>
            {
                transaction.Apply(
                    () => craftingNodeObject.SetActive(false),
                    () => craftingNodeObject.SetActive(wasActive));
                transaction.Apply(
                    () => RecipePlacementApplicator.Apply(
                        NeonLetterSmallCatalog.Placement,
                        placementTarget),
                    restorePlacement);
                transaction.Apply(
                    builtCollider.Apply,
                    builtCollider.Restore);
                if (craftingNodeCollider != null)
                {
                    transaction.Apply(
                        craftingNodeCollider.Apply,
                        craftingNodeCollider.Restore);
                }

                transaction.Apply(
                    () => recipe._recipeImage = recipeImage,
                    () => recipe._recipeImage = previousRecipeImage);
                transaction.Apply(
                    () => craftingNodeObject.SetActive(true),
                    () => craftingNodeObject.SetActive(false));
                transaction.Apply(
                    () => BookPageCoordinator.Restore(
                        coordinatorPlan.AddedRecipeSnapshot),
                    () => BookPageCoordinator.Restore(
                        coordinatorPlan.OriginalSnapshot));
                if (preparedBookPages.Count > 0)
                {
                    transaction.Apply(
                        preparedBookPages.Apply,
                        preparedBookPages.Restore);
                }
            },
            placementTarget.CommitGroundPlacementRemoval,
            placementTarget.CancelGroundPlacementRemoval);
    }

    private static AlphabetBookPageCoordinatorPlan<StructureRecipe>
        PrepareCoordinatorPlan(
            NeonLetterSmallDefinition definition,
            StructureRecipe recipe)
    {
        AlphabetBookPageCoordinatorPlan<StructureRecipe> plan =
            NeonLetterSmallBlueprint.BookPageCoordinator.PrepareAdd(
                definition,
                recipe);
        var BookPageCoordinator =
            new AlphabetBookPageCoordinator<StructureRecipe>();
        BookPageCoordinator.Restore(plan.AddedRecipeSnapshot);
        ReadyAlphabetBookPage<StructureRecipe> readyPage =
            BookPageCoordinator.GetNextReadyPage();
        int readyPageIndex = 0;
        while (readyPage != null)
        {
            if (readyPageIndex >= plan.ReadyPages.Count ||
                readyPage.PageIndex != plan.ReadyPages[readyPageIndex].PageIndex)
            {
                throw new InvalidOperationException(
                    "Prepared blueprint pages do not match the coordinator state.");
            }

            BookPageCoordinator.MarkCompleted(readyPage.PageIndex);
            readyPageIndex++;
            readyPage = BookPageCoordinator.GetNextReadyPage();
        }

        if (readyPageIndex != plan.ReadyPages.Count)
        {
            throw new InvalidOperationException(
                "Prepared blueprint page count does not match the coordinator state.");
        }

        return plan;
    }

    private static PreparedColliderMutation PrepareBuiltPrefabCollider(
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

        return PrepareBounds(collider, visualBounds, definition);
    }

    private static PreparedColliderMutation PrepareCraftingNodeCollider(
        StructureCraftingNode craftingNode,
        NeonLetterSmallDefinition definition)
    {
        BoxCollider collider = craftingNode.GetComponent<BoxCollider>();
        if (collider == null)
        {
            return null;
        }

        GameObject craftingNodeObject = craftingNode.gameObject;
        Transform visualRoot = FindColliderVisualRoot(craftingNodeObject, definition);
        return PrepareBounds(
            collider,
            CalculateLocalRendererBounds(craftingNodeObject, visualRoot),
            definition);
    }

    private static PreparedColliderMutation PrepareBounds(
        BoxCollider collider,
        Bounds visualBounds,
        NeonLetterSmallDefinition definition)
    {
        return new PreparedColliderMutation(
            collider,
            collider.center,
            collider.size,
            visualBounds.center,
            CreateColliderSize(visualBounds.size, definition));
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

    private static Action CapturePlacementRestoreAction(
        StructureCraftingNode craftingNode,
        StructureRecipe recipe)
    {
        var groundOffsetProvider = craftingNode.GroundOffsetProvider;
        var groundPresenceProvider = craftingNode.GroundPresenceProvider;
        StructureRecipe.AnchorType anchor = recipe._anchor;
        StructureRecipe.CastRadiusFormulas castRadiusFormula =
            recipe._castRadiusFormula;
        bool alignToSurface = recipe._alignToSurface;
        bool canBeRotated = recipe._canBeRotated;
        bool forceUp = recipe._forceUp;
        bool lockUpwardVector = recipe._lockUpwardVector;
        Vector3 initialPlacementRotationOffset =
            recipe._initialPlacementRotationOffset;
        bool allowsTreePlacement = recipe._allowsTreePlacement;
        bool allowsNonTreePlacement = recipe._allowsNonTreePlacement;
        float minimumHeightAboveTree = recipe._minHeightAboveTree;
        float maximumHeightAboveTree = recipe._maxHeightAboveTree;
        bool allowDynamicObjectParenting =
            recipe._allowParentingWithDynamicObjects;
        bool allowScrewStructureParenting =
            recipe._allowParentingWithScrewStructures;
        bool allowFreeFormStructureParenting =
            recipe._allowParentingWithFreeFormStructures;
        bool useOverridePlacementSize = recipe._useOverridePlacementSize;
        float placementDepthSizeRatio = recipe._placementDepthSizeRatio;
        StructureRecipe dynamicParentRecipeOverride =
            recipe._dynamicParentRecipeOverride;
        StructureRecipe screwParentRecipeOverride =
            recipe._screwParentRecipeOverride;
        StructureRecipe freeformParentRecipeOverride =
            recipe._freeformParentRecipeOverride;

        return () =>
        {
            // DestroyImmediate is required by the SDK clone's GetComponent-based validation.
            // A destroyed provider cannot be recreated safely, but every still-live reference
            // and all recipe fields remain reversible.
            craftingNode.GroundOffsetProvider =
                groundOffsetProvider == null ? null : groundOffsetProvider;
            craftingNode.GroundPresenceProvider =
                groundPresenceProvider == null ? null : groundPresenceProvider;
            recipe._anchor = anchor;
            recipe._castRadiusFormula = castRadiusFormula;
            recipe._alignToSurface = alignToSurface;
            recipe._canBeRotated = canBeRotated;
            recipe._forceUp = forceUp;
            recipe._lockUpwardVector = lockUpwardVector;
            recipe._initialPlacementRotationOffset =
                initialPlacementRotationOffset;
            recipe._allowsTreePlacement = allowsTreePlacement;
            recipe._allowsNonTreePlacement = allowsNonTreePlacement;
            recipe._minHeightAboveTree = minimumHeightAboveTree;
            recipe._maxHeightAboveTree = maximumHeightAboveTree;
            recipe._allowParentingWithDynamicObjects =
                allowDynamicObjectParenting;
            recipe._allowParentingWithScrewStructures =
                allowScrewStructureParenting;
            recipe._allowParentingWithFreeFormStructures =
                allowFreeFormStructureParenting;
            recipe._useOverridePlacementSize = useOverridePlacementSize;
            recipe._placementDepthSizeRatio = placementDepthSizeRatio;
            recipe._dynamicParentRecipeOverride =
                dynamicParentRecipeOverride;
            recipe._screwParentRecipeOverride =
                screwParentRecipeOverride;
            recipe._freeformParentRecipeOverride =
                freeformParentRecipeOverride;
        };
    }

    private sealed class PreparedColliderMutation
    {
        private readonly BoxCollider _collider;
        private readonly Vector3 _originalCenter;
        private readonly Vector3 _originalSize;
        private readonly Vector3 _newCenter;
        private readonly Vector3 _newSize;

        public PreparedColliderMutation(
            BoxCollider collider,
            Vector3 originalCenter,
            Vector3 originalSize,
            Vector3 newCenter,
            Vector3 newSize)
        {
            _collider = collider;
            _originalCenter = originalCenter;
            _originalSize = originalSize;
            _newCenter = newCenter;
            _newSize = newSize;
        }

        public void Apply()
        {
            _collider.center = _newCenter;
            _collider.size = _newSize;
        }

        public void Restore()
        {
            _collider.size = _originalSize;
            _collider.center = _originalCenter;
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
        private readonly GroundOffsetProviderBase _originalGroundOffsetProvider;
        private readonly GroundOffsetProvider _originalGroundPresenceProvider;
        private readonly GroundOffsetProvider _providerToDestroy;
        private bool _groundRemovalPending;
        private bool _groundRemovalCommitStarted;
        private bool _groundRemovalCommitted;

        public SonsRecipePlacementTarget(
            StructureCraftingNode craftingNode,
            StructureRecipe recipe)
        {
            _craftingNode = craftingNode;
            _recipe = recipe;
            _originalGroundOffsetProvider =
                craftingNode.GroundOffsetProvider;
            _originalGroundPresenceProvider =
                craftingNode.GroundPresenceProvider;
            _providerToDestroy =
                _originalGroundPresenceProvider ??
                craftingNode.GetComponent<GroundOffsetProvider>();
        }

        public bool GroundPlacementChecksRemoved =>
            _groundRemovalPending ||
            _groundRemovalCommitted ||
            (_craftingNode.GroundOffsetProvider == null &&
             _craftingNode.GroundPresenceProvider == null &&
             _craftingNode.GetComponent<GroundOffsetProvider>() == null);

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
            if (!_groundRemovalCommitted)
            {
                _groundRemovalPending = true;
            }
        }

        public void CancelGroundPlacementRemoval()
        {
            if (_groundRemovalCommitted)
            {
                return;
            }

            if (_groundRemovalCommitStarted)
            {
                _craftingNode.GroundOffsetProvider =
                    _originalGroundOffsetProvider;
                _craftingNode.GroundPresenceProvider =
                    _originalGroundPresenceProvider;
                _groundRemovalCommitStarted = false;
            }

            _groundRemovalPending = false;
        }

        public void CommitGroundPlacementRemoval()
        {
            if (_groundRemovalCommitted)
            {
                return;
            }

            if (!_groundRemovalPending)
            {
                throw new InvalidOperationException(
                    "Ground-placement provider removal was not prepared.");
            }

            bool hasProviderToDestroy = _providerToDestroy != null;
            _groundRemovalCommitStarted = true;
            _craftingNode.GroundOffsetProvider = null;
            _craftingNode.GroundPresenceProvider = null;
            if (hasProviderToDestroy)
            {
                UnityEngine.Object.DestroyImmediate(_providerToDestroy);
            }

            _groundRemovalPending = false;
            _groundRemovalCommitStarted = false;
            _groundRemovalCommitted = true;
        }

        public void SetInitialRotation(float x, float y, float z)
        {
            _recipe._initialPlacementRotationOffset = new Vector3(x, y, z);
        }
    }

    private sealed class UnityRuntimeMaterialFactory :
        IRuntimeMaterialFactory,
        IRuntimeMaterialOwner
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

        public void Release(IRuntimeMaterialHandle material)
        {
            UnityEngine.Object.DestroyImmediate(
                ((UnityRuntimeMaterialHandle)material).Material);
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

    private sealed class PreparedBookPageBatch
    {
        private readonly IReadOnlyList<PreparedBookPage> _preparedPages;
        private readonly PreparedBookPageRegistrationSnapshot _snapshot;

        private PreparedBookPageBatch(
            IReadOnlyList<PreparedBookPage> preparedPages,
            PreparedBookPageRegistrationSnapshot snapshot)
        {
            _preparedPages = preparedPages;
            _snapshot = snapshot;
        }

        public int Count => _preparedPages.Count;

        public static PreparedBookPageBatch Prepare(
            IReadOnlyList<ReadyAlphabetBookPage<StructureRecipe>> readyPages)
        {
            ArgumentNullException.ThrowIfNull(readyPages);
            if (readyPages.Count == 0)
            {
                return new PreparedBookPageBatch(
                    Array.Empty<PreparedBookPage>(),
                    null);
            }

            PreparedBookPageEnvironment environment =
                PreparedBookPageEnvironment.Prepare();
            int initialPageCount = environment.BookPages.Count;
            int plannedPageCount = initialPageCount;
            BlueprintBookPageData plannedLastPage = initialPageCount == 0
                ? null
                : environment.BookPages[initialPageCount - 1];
            var preparedPageDefinitions =
                new List<PreparedBookPageDefinition>(readyPages.Count);
            var localizationSnapshots =
                new List<LocalizationEntrySnapshot>();
            var capturedLocalizationEntries =
                new HashSet<string>(StringComparer.Ordinal);
            var recipeSnapshots =
                new List<RecipeLocalizationSnapshot>();
            var plannedLocalizationIds = new Dictionary<int, string>();

            foreach (ReadyAlphabetBookPage<StructureRecipe> readyPage in readyPages)
            {
                Texture2D background = Assets.GetBookPage(readyPage.PageIndex);
                if (background == null)
                {
                    throw new InvalidOperationException(
                        $"Cannot create the blueprint book page because asset " +
                        $"'{readyPage.TopDefinition.BookPageAssetName}' is not loaded.");
                }

                CaptureRecipe(
                    readyPage.TopRecipe,
                    recipeSnapshots,
                    plannedLocalizationIds);
                CaptureRecipe(
                    readyPage.BottomRecipe,
                    recipeSnapshots,
                    plannedLocalizationIds);
                string topGeneratedKey = environment.ResolveLocalizationKey(
                    readyPage.TopRecipe._displayName);
                string bottomGeneratedKey =
                    environment.ResolveLocalizationKey(
                        readyPage.BottomRecipe._displayName);
                bool shouldCreatePage = !PageMatches(
                    plannedLastPage,
                    readyPage.TopRecipe,
                    readyPage.BottomRecipe,
                    background,
                    readyPage.TopDefinition.BookPageTitleLocalizationKey);
                string topItemsKey =
                    plannedLocalizationIds[readyPage.TopRecipe.Id];
                string bottomItemsKey =
                    plannedLocalizationIds[readyPage.BottomRecipe.Id];
                BlueprintBookPageData pageData = null;
                int expectedPageCountBefore = plannedPageCount;
                if (shouldCreatePage)
                {
                    CaptureLocalization(
                        environment.BlueprintBookTable,
                        topGeneratedKey,
                        "BlueprintBook",
                        capturedLocalizationEntries,
                        localizationSnapshots);
                    CaptureLocalization(
                        environment.BlueprintBookTable,
                        bottomGeneratedKey,
                        "BlueprintBook",
                        capturedLocalizationEntries,
                        localizationSnapshots);
                    topItemsKey = topGeneratedKey;
                    bottomItemsKey = bottomGeneratedKey;
                    plannedLocalizationIds[readyPage.TopRecipe.Id] =
                        topGeneratedKey;
                    plannedLocalizationIds[readyPage.BottomRecipe.Id] =
                        bottomGeneratedKey;
                    pageData = new BlueprintBookPageData
                    {
                        _topRecipe = readyPage.TopRecipe,
                        _bottomRecipe = readyPage.BottomRecipe,
                        _pageImage = background,
                        _pageTitleLocalizationId =
                            readyPage.TopDefinition.BookPageTitleLocalizationKey
                    };
                    plannedLastPage = pageData;
                    plannedPageCount++;
                }

                CaptureLocalization(
                    environment.ItemsTable,
                    readyPage.TopDefinition.BookPageTitleLocalizationKey,
                    "Items",
                    capturedLocalizationEntries,
                    localizationSnapshots);
                CaptureLocalization(
                    environment.ItemsTable,
                    topItemsKey,
                    "Items",
                    capturedLocalizationEntries,
                    localizationSnapshots);
                CaptureLocalization(
                    environment.ItemsTable,
                    bottomItemsKey,
                    "Items",
                    capturedLocalizationEntries,
                    localizationSnapshots);
                preparedPageDefinitions.Add(
                    new PreparedBookPageDefinition(
                        readyPage,
                        background,
                        pageData,
                        topGeneratedKey,
                        bottomGeneratedKey,
                        topItemsKey,
                        bottomItemsKey,
                        expectedPageCountBefore));
            }

            var snapshot = new PreparedBookPageRegistrationSnapshot(
                environment.BookPages,
                initialPageCount,
                localizationSnapshots,
                recipeSnapshots);
            PreparedBookPage[] preparedPages = preparedPageDefinitions
                .Select(definition => definition.Create(environment, snapshot))
                .ToArray();
            return new PreparedBookPageBatch(
                preparedPages,
                snapshot);
        }

        public void Apply()
        {
            foreach (PreparedBookPage preparedPage in _preparedPages)
            {
                preparedPage.Apply();
                BookPageCoordinator.MarkCompleted(preparedPage.PageIndex);
            }
        }

        public void Restore()
        {
            _snapshot.Restore();
        }

        private static void CaptureRecipe(
            StructureRecipe recipe,
            List<RecipeLocalizationSnapshot> snapshots,
            Dictionary<int, string> plannedLocalizationIds)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            if (plannedLocalizationIds.ContainsKey(recipe.Id))
            {
                return;
            }

            plannedLocalizationIds.Add(recipe.Id, recipe._localizationId);
            snapshots.Add(
                new RecipeLocalizationSnapshot(
                    recipe,
                    recipe._localizationId));
        }

        private static void CaptureLocalization(
            StringTable table,
            string key,
            string tableName,
            HashSet<string> capturedEntries,
            List<LocalizationEntrySnapshot> snapshots)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    $"{tableName} localization requires a non-empty key.");
            }

            if (!capturedEntries.Add($"{tableName}\0{key}"))
            {
                return;
            }

            StringTableEntry entry = table.GetEntry(key);
            snapshots.Add(
                new LocalizationEntrySnapshot(
                    table,
                    key,
                    entry != null,
                    entry == null ? null : entry.Value));
        }

        public static bool PageMatches(
            BlueprintBookPageData page,
            StructureRecipe topRecipe,
            StructureRecipe bottomRecipe,
            Texture2D background,
            string titleLocalizationKey)
        {
            return page != null &&
                   page._topRecipe == topRecipe &&
                   page._bottomRecipe == bottomRecipe &&
                   page._pageImage == background &&
                   string.Equals(
                       page._pageTitleLocalizationId,
                       titleLocalizationKey,
                       StringComparison.Ordinal);
        }
    }

    private sealed class PreparedBookPageDefinition
    {
        private readonly ReadyAlphabetBookPage<StructureRecipe> _readyPage;
        private readonly Texture2D _background;
        private readonly BlueprintBookPageData _pageData;
        private readonly string _topGeneratedKey;
        private readonly string _bottomGeneratedKey;
        private readonly string _topItemsKey;
        private readonly string _bottomItemsKey;
        private readonly int _expectedPageCountBefore;

        public PreparedBookPageDefinition(
            ReadyAlphabetBookPage<StructureRecipe> readyPage,
            Texture2D background,
            BlueprintBookPageData pageData,
            string topGeneratedKey,
            string bottomGeneratedKey,
            string topItemsKey,
            string bottomItemsKey,
            int expectedPageCountBefore)
        {
            _readyPage = readyPage;
            _background = background;
            _pageData = pageData;
            _topGeneratedKey = topGeneratedKey;
            _bottomGeneratedKey = bottomGeneratedKey;
            _topItemsKey = topItemsKey;
            _bottomItemsKey = bottomItemsKey;
            _expectedPageCountBefore = expectedPageCountBefore;
        }

        public PreparedBookPage Create(
            PreparedBookPageEnvironment environment,
            PreparedBookPageRegistrationSnapshot snapshot)
        {
            var target = new PreparedBookPageRegistrationTarget(
                _readyPage,
                _background,
                _pageData,
                _topGeneratedKey,
                _bottomGeneratedKey,
                _topItemsKey,
                _bottomItemsKey,
                _expectedPageCountBefore,
                environment,
                snapshot);
            return new PreparedBookPage(
                _readyPage,
                _background,
                target);
        }
    }

    private sealed class PreparedBookPage
    {
        private readonly ReadyAlphabetBookPage<StructureRecipe> _readyPage;
        private readonly Texture2D _background;
        private readonly PreparedBookPageRegistrationTarget _target;

        public PreparedBookPage(
            ReadyAlphabetBookPage<StructureRecipe> readyPage,
            Texture2D background,
            PreparedBookPageRegistrationTarget target)
        {
            _readyPage = readyPage;
            _background = background;
            _target = target;
        }

        public int PageIndex => _readyPage.PageIndex;

        public void Apply()
        {
            BookPageRegistrar.Register(
                _readyPage.TopDefinition.BookPageTitleLocalizationKey,
                NeonLetterSmallCatalog.BookPageTitle,
                _readyPage.TopRecipe,
                _readyPage.TopDefinition.RecipeName,
                _readyPage.BottomRecipe,
                _readyPage.BottomDefinition.RecipeName,
                _background,
                _target);
        }
    }

    private sealed class PreparedBookPageRegistrationTarget :
        ITransactionalBookPageRegistrationTarget<StructureRecipe, Texture2D>
    {
        private readonly ReadyAlphabetBookPage<StructureRecipe> _readyPage;
        private readonly Texture2D _background;
        private readonly BlueprintBookPageData _pageData;
        private readonly string _topGeneratedKey;
        private readonly string _bottomGeneratedKey;
        private readonly string _topItemsKey;
        private readonly string _bottomItemsKey;
        private readonly int _expectedPageCountBefore;
        private readonly PreparedBookPageEnvironment _environment;
        private readonly PreparedBookPageRegistrationSnapshot _snapshot;
        private bool _pageCreated;

        public PreparedBookPageRegistrationTarget(
            ReadyAlphabetBookPage<StructureRecipe> readyPage,
            Texture2D background,
            BlueprintBookPageData pageData,
            string topGeneratedKey,
            string bottomGeneratedKey,
            string topItemsKey,
            string bottomItemsKey,
            int expectedPageCountBefore,
            PreparedBookPageEnvironment environment,
            PreparedBookPageRegistrationSnapshot snapshot)
        {
            _readyPage = readyPage;
            _background = background;
            _pageData = pageData;
            _topGeneratedKey = topGeneratedKey;
            _bottomGeneratedKey = bottomGeneratedKey;
            _topItemsKey = topItemsKey;
            _bottomItemsKey = bottomItemsKey;
            _expectedPageCountBefore = expectedPageCountBefore;
            _environment = environment;
            _snapshot = snapshot;
        }

        public int PageCount => _environment.BookPages.Count;

        public void AddLocalization(string key, string value)
        {
            if (!string.Equals(
                    key,
                    _readyPage.TopDefinition.BookPageTitleLocalizationKey,
                    StringComparison.Ordinal) &&
                !string.Equals(key, _topItemsKey, StringComparison.Ordinal) &&
                !string.Equals(key, _bottomItemsKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Localization key '{key}' was not captured during callback preflight.");
            }

            _environment.ItemsTable.AddEntry(key, value);
        }

        public string GetRecipeLocalizationId(StructureRecipe recipe)
        {
            if (!ReferenceEquals(recipe, _readyPage.TopRecipe) &&
                !ReferenceEquals(recipe, _readyPage.BottomRecipe))
            {
                throw new InvalidOperationException(
                    "An unexpected recipe was supplied to the prepared book page.");
            }

            return recipe._localizationId;
        }

        public void CreatePage(
            StructureRecipe topRecipe,
            StructureRecipe bottomRecipe,
            Texture2D background,
            string titleLocalizationKey)
        {
            ValidatePageInputs(
                topRecipe,
                bottomRecipe,
                background,
                titleLocalizationKey);
            if (_pageData == null)
            {
                throw new InvalidOperationException(
                    "The prepared book page unexpectedly requires creation.");
            }

            if (_environment.BookPages.Count != _expectedPageCountBefore)
            {
                throw new InvalidOperationException(
                    "The blueprint book page collection changed after callback preflight.");
            }

            try
            {
                CustomBlueprintManager.NextLocalizationString =
                    titleLocalizationKey;
                _environment.BlueprintBookTable.AddEntry(
                    _topGeneratedKey,
                    topRecipe._displayName);
                topRecipe._localizationId = _topGeneratedKey;
                _environment.BlueprintBookTable.AddEntry(
                    _bottomGeneratedKey,
                    bottomRecipe._displayName);
                bottomRecipe._localizationId = _bottomGeneratedKey;
                _environment.BookPages.Add(_pageData);
                _pageCreated = true;
            }
            finally
            {
                CustomBlueprintManager.NextLocalizationString =
                    _environment.PreviousLocalizationString;
            }
        }

        public bool LastPageMatches(
            StructureRecipe topRecipe,
            StructureRecipe bottomRecipe,
            Texture2D background,
            string titleLocalizationKey)
        {
            ValidatePageInputs(
                topRecipe,
                bottomRecipe,
                background,
                titleLocalizationKey);
            int expectedPageCount = _expectedPageCountBefore +
                (_pageCreated ? 1 : 0);
            if (_environment.BookPages.Count != expectedPageCount)
            {
                throw new InvalidOperationException(
                    "The blueprint book page collection changed after callback preflight.");
            }

            bool matches = PreparedBookPageBatch.PageMatches(
                _environment.BookPages.Count == 0
                    ? null
                    : _environment.BookPages[_environment.BookPages.Count - 1],
                topRecipe,
                bottomRecipe,
                background,
                titleLocalizationKey);
            bool expectedMatch = _pageData == null || _pageCreated;
            if (matches != expectedMatch)
            {
                throw new InvalidOperationException(
                    "The blueprint book page collection changed after callback preflight.");
            }

            return matches;
        }

        public object CaptureRegistrationState(
            string titleLocalizationKey,
            StructureRecipe topRecipe,
            StructureRecipe bottomRecipe)
        {
            ValidatePageInputs(
                topRecipe,
                bottomRecipe,
                _background,
                titleLocalizationKey);
            return _snapshot;
        }

        public void RestoreRegistrationState(object snapshot)
        {
            if (!ReferenceEquals(snapshot, _snapshot))
            {
                throw new InvalidOperationException(
                    "An unexpected prepared book page snapshot was supplied.");
            }

            _snapshot.Restore();
            _pageCreated = false;
        }

        private void ValidatePageInputs(
            StructureRecipe topRecipe,
            StructureRecipe bottomRecipe,
            Texture2D background,
            string titleLocalizationKey)
        {
            if (!ReferenceEquals(topRecipe, _readyPage.TopRecipe) ||
                !ReferenceEquals(bottomRecipe, _readyPage.BottomRecipe) ||
                !ReferenceEquals(background, _background) ||
                !string.Equals(
                    titleLocalizationKey,
                    _readyPage.TopDefinition.BookPageTitleLocalizationKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Book page registration inputs changed after callback preflight.");
            }
        }
    }

    private sealed class PreparedBookPageEnvironment
    {
        private readonly System.Reflection.MethodInfo _getLocalizationKey;

        private PreparedBookPageEnvironment(
            Il2CppSystem.Collections.Generic.List<BlueprintBookPageData> bookPages,
            StringTable itemsTable,
            StringTable blueprintBookTable,
            string previousLocalizationString,
            System.Reflection.MethodInfo getLocalizationKey)
        {
            BookPages = bookPages;
            ItemsTable = itemsTable;
            BlueprintBookTable = blueprintBookTable;
            PreviousLocalizationString = previousLocalizationString;
            _getLocalizationKey = getLocalizationKey;
        }

        public Il2CppSystem.Collections.Generic.List<BlueprintBookPageData>
            BookPages { get; }
        public StringTable ItemsTable { get; }
        public StringTable BlueprintBookTable { get; }
        public string PreviousLocalizationString { get; }

        public static PreparedBookPageEnvironment Prepare()
        {
            Transform book = ItemTools.GetHeldPrefab(552);
            BlueprintBookController controller =
                book == null ? null : book.GetComponent<BlueprintBookController>();
            if (controller == null ||
                controller._pages == null ||
                controller._pages._pages == null)
            {
                throw new InvalidOperationException(
                    "The blueprint book controller is unavailable while registering Neon Symbols.");
            }

            StringTable itemsTable = SonsSdk.LocalizationTools.ItemsTable;
            if (itemsTable == null)
            {
                throw new InvalidOperationException(
                    "The Items localization table is unavailable while registering Neon Symbols.");
            }

            StringTable blueprintBookTable =
                SonsSdk.LocalizationTools.BlueprintBookTable;
            if (blueprintBookTable == null)
            {
                throw new InvalidOperationException(
                    "The BlueprintBook localization table is unavailable while registering " +
                    "Neon Symbols.");
            }
            Type localizationUtils = Type.GetType(
                "Endnight.Localization.LocalizationUtils, Endnight.Localization",
                throwOnError: true);
            System.Reflection.MethodInfo getLocalizationKey =
                localizationUtils.GetMethod(
                    "GetLocalizationKey",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(string) },
                    modifiers: null)
                ?? throw new InvalidOperationException(
                    "Endnight localization key generation is unavailable while registering Neon " +
                    "Symbols.");
            return new PreparedBookPageEnvironment(
                controller._pages._pages,
                itemsTable,
                blueprintBookTable,
                CustomBlueprintManager.NextLocalizationString,
                getLocalizationKey);
        }

        public string ResolveLocalizationKey(string displayName)
        {
            string localizationKey = (string)_getLocalizationKey.Invoke(
                obj: null,
                parameters: new object[] { displayName });
            if (string.IsNullOrWhiteSpace(localizationKey))
            {
                throw new InvalidOperationException(
                    "Endnight localization key generation returned an empty key.");
            }

            return localizationKey;
        }
    }

    private sealed class PreparedBookPageRegistrationSnapshot
    {
        private readonly
            Il2CppSystem.Collections.Generic.List<BlueprintBookPageData> _bookPages;
        private readonly int _initialPageCount;
        private readonly IReadOnlyList<LocalizationEntrySnapshot>
            _localizationSnapshots;
        private readonly IReadOnlyList<RecipeLocalizationSnapshot>
            _recipeSnapshots;
        private bool _restored;

        public PreparedBookPageRegistrationSnapshot(
            Il2CppSystem.Collections.Generic.List<BlueprintBookPageData> bookPages,
            int initialPageCount,
            IReadOnlyList<LocalizationEntrySnapshot> localizationSnapshots,
            IReadOnlyList<RecipeLocalizationSnapshot> recipeSnapshots)
        {
            _bookPages = bookPages;
            _initialPageCount = initialPageCount;
            _localizationSnapshots = localizationSnapshots;
            _recipeSnapshots = recipeSnapshots;
        }

        public void Restore()
        {
            if (_restored)
            {
                return;
            }

            List<Exception> restoreExceptions = null;
            try
            {
                if (_bookPages.Count < _initialPageCount)
                {
                    throw new InvalidOperationException(
                        "Blueprint book pages were removed outside the Neon Symbols callback while " +
                        "rolling back registration.");
                }

                while (_bookPages.Count > _initialPageCount)
                {
                    _bookPages.RemoveAt(_bookPages.Count - 1);
                }
            }
            catch (Exception exception)
            {
                (restoreExceptions ??= new List<Exception>()).Add(exception);
            }

            for (int index = _localizationSnapshots.Count - 1;
                 index >= 0;
                 index--)
            {
                try
                {
                    _localizationSnapshots[index].Restore();
                }
                catch (Exception exception)
                {
                    (restoreExceptions ??= new List<Exception>()).Add(exception);
                }
            }

            for (int index = _recipeSnapshots.Count - 1;
                 index >= 0;
                 index--)
            {
                try
                {
                    _recipeSnapshots[index].Restore();
                }
                catch (Exception exception)
                {
                    (restoreExceptions ??= new List<Exception>()).Add(exception);
                }
            }

            if (restoreExceptions != null)
            {
                throw new AggregateException(
                    "Blueprint book page rollback did not complete cleanly.",
                    restoreExceptions);
            }

            _restored = true;
        }
    }

    private sealed class RecipeLocalizationSnapshot
    {
        private readonly StructureRecipe _recipe;
        private readonly string _localizationId;

        public RecipeLocalizationSnapshot(
            StructureRecipe recipe,
            string localizationId)
        {
            _recipe = recipe;
            _localizationId = localizationId;
        }

        public void Restore()
        {
            _recipe._localizationId = _localizationId;
        }
    }

    private sealed class LocalizationEntrySnapshot
    {
        private readonly StringTable _table;
        private readonly string _key;
        private readonly bool _existed;
        private readonly string _value;

        public LocalizationEntrySnapshot(
            StringTable table,
            string key,
            bool existed,
            string value)
        {
            _table = table;
            _key = key;
            _existed = existed;
            _value = value;
        }

        public void Restore()
        {
            if (_existed)
            {
                _table.AddEntry(_key, _value);
                return;
            }

            StringTableEntry entry = _table.GetEntry(_key);
            if (entry != null)
            {
                entry.RemoveFromTable();
            }
        }
    }
}
