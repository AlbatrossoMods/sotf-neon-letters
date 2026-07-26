using SOTFNeonLetters;
using RedLoader;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

public sealed class ContractTests
{
    private readonly List<string> failures = new();

    [Fact]
    public void BehaviorContractsAreSatisfied()
    {
        CheckAssetContract();
        CheckExtensionSourceContract();
        CheckCraftingContract();
        CheckDismantleRefundContract();
        CheckWallPlacementContract();
        CheckColliderContract();
        CheckRuntimeColliderPolicyContract();
        CheckBookContract();
        CheckPlacementApplication();
        CheckRuntimeMaterialReplacement();
        CheckBookPageRegistration();
        CheckAlphabetCatalog();
        CheckExpandedSymbolCatalogContract();
        CheckAlphabetRuntimeBindings();
        CheckBookPageCoordinator();
        CheckColorEditingContract();
        CheckMultiplayerProtocolContract();
        CheckMultiplayerStateContract();
        CheckMultiplayerPersistenceContract();
        CheckMultiplayerRestoreCoordinatorContract();
        CheckMultiplayerNativeRestoreCoordinatorContract();
        CheckMultiplayerRestoreFailureIsolationContract();
        CheckMultiplayerRestoreRoleContract();
        CheckMultiplayerRestoreReadinessContract();
        CheckColorPersistenceContract();
        CheckExtendedSymbolRuntimePolicyContract();
        CheckColorRestoreContract();
        CheckEmissionApplicationContract();
        CheckColorInteractionContract();
        CheckColorRuntimeSafetyContract();
        CheckColorPickerLayoutContract();

        Assert.True(
            failures.Count == 0,
            $"Contract tests failed: {failures.Count}{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", failures)}");
    }

void CheckAssetContract()
{
    CheckEqual(
        "sotfneonletters",
        NeonLetterASmallDefinition.BundleName,
        "Small A uses the expected asset bundle");
    CheckEqual(
        "NeonLetter_A_Small",
        NeonLetterASmallDefinition.PrefabAssetName,
        "Small A uses the expected prefab address");
    CheckEqual(
        "NeonLetters_Small_Page_01",
        NeonLetterASmallDefinition.BookPageAssetName,
        "Small A uses the expected book page address");
    CheckEqual(
        "NeonLetter_A_Small_Icon",
        NeonLetterASmallDefinition.BookIconAssetName,
        "Small A uses the expected book icon address");
    CheckEqual(
        "HDRP/Lit",
        NeonLetterASmallDefinition.LetterShaderName,
        "Small A requires the visible HDRP letter shader");
    CheckEqual(
        "HDRP/Lit",
        NeonLetterASmallDefinition.WireShaderName,
        "Small A requires the visible HDRP wire shader");
}

void CheckExtensionSourceContract()
{
    const string expectedSha256 =
        "02f9f0fa2d0195824b9f767bc98d1010793475624ed29e893436689ff57679c4";
    const string sourceRelativePath =
        "assets/source/neon-symbols/neon_letters_extended_game_ready.glb";
    const string inventoryRelativePath =
        "assets/source/neon-symbols/symbol-inventory.json";

    string? sourcePath = FindRepositoryFile(sourceRelativePath);
    string? inventoryPath = FindRepositoryFile(inventoryRelativePath);

    CheckEqual(false, sourcePath == null, "the approved extension GLB is tracked in the repository");
    CheckEqual(false, inventoryPath == null, "the extension symbol inventory is tracked in the repository");
    if (sourcePath == null || inventoryPath == null)
    {
        return;
    }

    using (FileStream source = File.OpenRead(sourcePath))
    using (SHA256 sha256 = SHA256.Create())
    {
        string actualSha256 = Convert.ToHexString(sha256.ComputeHash(source)).ToLowerInvariant();
        CheckEqual(
            expectedSha256,
            actualSha256,
            "the tracked extension GLB is the approved source asset");
    }

    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(inventoryPath));
    JsonElement[] entries = document.RootElement.EnumerateArray().ToArray();
    string[] symbols = entries
        .Select(entry => entry.GetProperty("symbol").GetString()!)
        .ToArray();
    string[] unicodeCodes = entries
        .Select(entry => entry.GetProperty("unicode").GetString()!)
        .ToArray();
    string[] assetKeys = entries
        .Select(entry => entry.GetProperty("assetKey").GetString()!)
        .ToArray();
    string[] sourceRoots = entries
        .Select(entry => entry.GetProperty("sourceRoot").GetString()!)
        .ToArray();

    CheckEqual(54, entries.Length, "the extension inventory exposes every supplied symbol exactly once");
    CheckEqual(54, symbols.Distinct(StringComparer.Ordinal).Count(), "extension symbols are unique");
    CheckEqual(54, unicodeCodes.Distinct(StringComparer.Ordinal).Count(), "extension Unicode codes are unique");
    CheckEqual(54, assetKeys.Distinct(StringComparer.Ordinal).Count(), "extension asset keys are unique");
    CheckEqual(54, sourceRoots.Distinct(StringComparer.Ordinal).Count(), "extension source roots are unique");

    for (int index = 0; index < entries.Length; index++)
    {
        string symbol = symbols[index];
        string expectedUnicode = $"U{(int)symbol[0]:X4}";
        string expectedSourceFamily = char.IsDigit(symbol[0])
            ? "DIG"
            : char.IsLetter(symbol[0])
                ? "CYR"
                : "PUNC";

        CheckEqual(expectedUnicode, unicodeCodes[index], $"{symbol} records its Unicode code");
        CheckEqual(
            $"glyph_{expectedSourceFamily}_{expectedUnicode}.013",
            sourceRoots[index],
            $"{symbol} records its exact GLB source root");
        CheckEqual(
            true,
            assetKeys[index].All(character => character <= 0x7F),
            $"{symbol} uses an ASCII-safe asset key");
        CheckEqual(
            true,
            assetKeys[index].StartsWith($"{expectedSourceFamily}_{expectedUnicode}", StringComparison.Ordinal),
            $"{symbol} asset key includes its source family and Unicode code");
    }
}

void CheckCraftingContract()
{
    CheckEqual(
        "Neon Letter A (Small)",
        NeonLetterASmallDefinition.RecipeName,
        "Small A exposes its player-facing recipe name");
    CheckEqual(
        1_904_177_201,
        NeonLetterASmallDefinition.RecipeId,
        "Small A uses its unique recipe ID");
    CheckEqual(
        NeonLetterASmallDefinition.RecipeId - 1,
        NeonLetterASmallDefinition.CraftingNodeId,
        "Small A derives the crafting-node ID from its recipe ID");
    CheckSequence(
        new[]
        {
            new NeonLetterASmallDefinition.IngredientDefinition("Ingredient_Wire_Lead", 418),
            new NeonLetterASmallDefinition.IngredientDefinition("Ingredient_LightBulb_A", 635)
        },
        NeonLetterASmallDefinition.Ingredients,
        "Small A requires one wire followed by one light bulb");
}

void CheckDismantleRefundContract()
{
    CheckSequence(
        new[] { 418, 635 },
        NeonLetterDismantleRefundPolicy.ResolveItemIds(
            NeonLetterASmallDefinition.RecipeId),
        "dismantling Small A returns the wire and light bulb invested in its recipe");
    CheckEqual(
        true,
        NeonLetterDismantleRefundPolicy.ShouldSpawnRefund(
            isMultiplayer: false,
            isServer: false),
        "single-player dismantling spawns the letter refund");
    CheckEqual(
        true,
        NeonLetterDismantleRefundPolicy.ShouldSpawnRefund(
            isMultiplayer: true,
            isServer: true),
        "the multiplayer host spawns the letter refund once");
    CheckEqual(
        false,
        NeonLetterDismantleRefundPolicy.ShouldSpawnRefund(
            isMultiplayer: true,
            isServer: false),
        "a multiplayer client never duplicates the letter refund");
    CheckSequence(
        Array.Empty<int>(),
        NeonLetterDismantleRefundPolicy.ResolveItemIds(int.MinValue),
        "unrelated structures never receive a neon-letter refund");

    string? runtimePath = FindRepositoryFile("NeonLetterDismantleRuntime.cs");
    CheckEqual(false, runtimePath == null, "dismantling uses a dedicated runtime hook");
    if (runtimePath == null)
    {
        return;
    }

    string runtimeSource = File.ReadAllText(runtimePath);
    CheckEqual(
        true,
        runtimeSource.Contains("RegisterDismantled", StringComparison.Ordinal),
        "refund hook targets only the native dismantle lifecycle");
    CheckEqual(
        true,
        runtimeSource.Contains("SpawnItemsWorker", StringComparison.Ordinal),
        "refund hook uses the game's item drop routine");
}

void CheckWallPlacementContract()
{
    NeonLetterASmallDefinition.PlacementDefinition placement =
        NeonLetterASmallDefinition.Placement;

    CheckEqual(false, placement.AlignToSurface, "Small A avoids the crashing native surface-alignment path");
    CheckEqual(0f, placement.InitialRotationX, "Small A has no X rotation offset");
    CheckEqual(180f, placement.InitialRotationY, "Small A faces the player instead of showing its mirrored back");
    CheckEqual(0f, placement.InitialRotationZ, "Small A has no Z rotation offset");
    CheckEqual(true, placement.AllowsTreePlacement, "Small A accepts vanilla wall targets");
    CheckEqual(true, placement.AllowsNonTreePlacement, "Small A accepts built wall targets");
}

void CheckColliderContract()
{
    CheckEqual(
        true,
        NeonLetterASmallDefinition.IsColliderVisualChild("Ingredient_LightBulb_A"),
        "Small A sizes its collider from the visible letter");
    CheckEqual(
        false,
        NeonLetterASmallDefinition.IsColliderVisualChild("Ingredient_Wire_Lead"),
        "Small A excludes the wire from collider bounds");

    NeonLetterASmallDefinition.ColliderSize thin =
        NeonLetterASmallDefinition.ResolveColliderSize(0.42f, 0.50f, 0.02f);
    CheckEqual(0.42f, thin.Width, "Small A collider preserves visual width");
    CheckEqual(0.50f, thin.Height, "Small A collider preserves visual height");
    CheckEqual(0.08f, thin.Depth, "Small A collider clamps only a too-thin depth");

    NeonLetterASmallDefinition.ColliderSize thick =
        NeonLetterASmallDefinition.ResolveColliderSize(0.42f, 0.50f, 0.12f);
    CheckEqual(0.12f, thick.Depth, "Small A collider preserves sufficient visual depth");
}

void CheckRuntimeColliderPolicyContract()
{
    Type? policyType = typeof(RecipePlacementApplicator).Assembly.GetType(
        "SOTFNeonLetters.NeonLetterColliderPolicy");
    CheckEqual(
        false,
        policyType == null,
        "runtime collider sizing is exposed through a contract-testable production policy");
    if (policyType == null)
    {
        return;
    }

    System.Reflection.MethodInfo? resolve = policyType.GetMethod(
        "Resolve",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    CheckEqual(
        false,
        resolve == null,
        "runtime collider policy exposes its production Resolve entry point");
    if (resolve == null)
    {
        return;
    }

    foreach (NeonLetterSmallDefinition definition in NeonLetterSmallCatalog.All)
    {
        object? result = resolve.Invoke(
            null,
            new object[] { definition, 0.42f, 0.50f, 0.02f });
        CheckEqual(
            false,
            result == null,
            $"{definition.Symbol} runtime collider policy returns a size");
        if (result is not NeonLetterSmallDefinition.ColliderSize collider)
        {
            continue;
        }

        CheckEqual(0.42f, collider.Width,
            $"{definition.Symbol} runtime collider preserves visual width");
        CheckEqual(0.50f, collider.Height,
            $"{definition.Symbol} runtime collider preserves visual height");
        CheckEqual(
            NeonLetterSmallCatalog.MinimumColliderDepth,
            collider.Depth,
            $"{definition.Symbol} runtime collider clamps only a too-thin depth");
    }

    string blueprintPath = FindRepositoryFile("NeonLetterASmallBlueprint.cs")
        ?? throw new InvalidOperationException("Could not find the Blueprint production path.");
    string blueprintSource = File.ReadAllText(blueprintPath);
    CheckEqual(
        true,
        blueprintSource.Contains(
            "NeonLetterColliderPolicy.Resolve(",
            StringComparison.Ordinal),
        "Blueprint production applies the contract-tested collider policy");
    CheckEqual(
        false,
        blueprintSource.Contains(
            "definition.ResolveColliderSize(",
            StringComparison.Ordinal),
        "Blueprint production has no untested collider-size bypass");
}

void CheckBookContract()
{
    CheckEqual(
        "BLUEPRINT_PAGE_SOTF_NEON_LETTERS",
        NeonLetterASmallDefinition.BookPageTitleLocalizationKey,
        "Small A uses its dedicated book page title key");
    CheckEqual(
        "Neon Letters",
        NeonLetterASmallDefinition.BookPageTitle,
        "Small A exposes the Neon Letters book title");
    CheckEqual(1024, NeonLetterASmallDefinition.BookPageWidth, "Small A page width is stable");
    CheckEqual(1024, NeonLetterASmallDefinition.BookPageHeight, "Small A page height is stable");
    CheckEqual(11, NeonLetterASmallDefinition.BookPageMipCount, "Small A page mip count is stable");
    CheckEqual(128, NeonLetterASmallDefinition.BookIconSize, "Small A icon size is stable");
    CheckEqual(8, NeonLetterASmallDefinition.BookIconMipCount, "Small A icon mip count is stable");
}

void CheckPlacementApplication()
{
    var target = new FakePlacementTarget();
    NeonLetterASmallDefinition.PlacementDefinition placement =
        NeonLetterASmallDefinition.Placement;

    RecipePlacementApplicator.Apply(placement, target);

    CheckEqual(true, target.GroundPlacementChecksRemoved, "wall placement removes inherited ground checks");
    CheckEqual(true, target.ParentRecipeOverridesCleared, "wall placement clears inherited parent overrides");
    CheckEqual(placement, target.Snapshot, "placement transfers the complete wall contract");
    CheckSequence(
        new[]
        {
            nameof(IRecipePlacementTarget.RemoveGroundPlacementChecks),
            nameof(IRecipePlacementTarget.Anchor),
            nameof(IRecipePlacementTarget.CastRadiusFormula),
            nameof(IRecipePlacementTarget.AlignToSurface),
            nameof(IRecipePlacementTarget.CanBeRotated),
            nameof(IRecipePlacementTarget.ForceUp),
            nameof(IRecipePlacementTarget.LockUpwardVector),
            nameof(IRecipePlacementTarget.SetInitialRotation),
            nameof(IRecipePlacementTarget.AllowsTreePlacement),
            nameof(IRecipePlacementTarget.AllowsNonTreePlacement),
            nameof(IRecipePlacementTarget.MinimumHeightAboveTree),
            nameof(IRecipePlacementTarget.MaximumHeightAboveTree),
            nameof(IRecipePlacementTarget.AllowDynamicObjectParenting),
            nameof(IRecipePlacementTarget.AllowScrewStructureParenting),
            nameof(IRecipePlacementTarget.AllowFreeFormStructureParenting),
            nameof(IRecipePlacementTarget.UseOverridePlacementSize),
            nameof(IRecipePlacementTarget.PlacementDepthSizeRatio)
        },
        target.AppliedOperations,
        "placement explicitly replaces every inherited recipe-25 placement field");
}

void CheckRuntimeMaterialReplacement()
{
    var firstSource = new FakeRuntimeMaterial(
        "Letter",
        2450,
        new[] { "_EMISSIVE_COLOR_MAP", "_DOUBLESIDED_ON" },
        new Dictionary<string, float> { ["emission"] = 600f });
    var secondSource = new FakeRuntimeMaterial(
        "Wire",
        2000,
        Array.Empty<string>(),
        new Dictionary<string, float> { ["smoothness"] = 0.2f });
    var firstRenderer = new FakeRuntimeRenderer("LetterRenderer", firstSource, secondSource);
    var secondRenderer = new FakeRuntimeRenderer("WireRenderer", secondSource);
    var factory = new FakeRuntimeMaterialFactory("HDRP/Lit", true);

    RuntimeMaterialReplacement.ReplaceAll(
        "NeonLetter_A_Small",
        new IRuntimeRendererHandle[] { firstRenderer, secondRenderer },
        factory);

    CheckEqual(2, firstRenderer.Materials.Count, "runtime replacement preserves renderer slot count");
    CheckEqual(1, secondRenderer.Materials.Count, "runtime replacement updates every renderer");
    CheckEqual(3, factory.CreatedMaterials.Count, "runtime replacement creates one material per slot");

    FakeRuntimeMaterial firstRuntime = (FakeRuntimeMaterial)firstRenderer.Materials[0];
    CheckEqual("Letter_Runtime", firstRuntime.Name, "runtime material gets a diagnostic name");
    CheckEqual(2450, firstRuntime.RenderQueue, "runtime material preserves render queue");
    CheckSequence(
        (IEnumerable<string>)firstSource.ShaderKeywords,
        (IEnumerable<string>)firstRuntime.ShaderKeywords,
        "runtime material preserves shader keywords");
    CheckEqual(600f, firstRuntime.Properties["emission"], "runtime material preserves emission");
    CheckEqual("HDRP/Lit", firstRuntime.ShaderName, "runtime material uses the loaded game shader");
    CheckEqual(false, ReferenceEquals(firstSource, firstRuntime), "runtime replacement does not reuse bundle material");
}

void CheckBookPageRegistration()
{
    var target = new FakeBookPageRegistrationTarget();

    BookPageRegistrar.Register(
        "BLUEPRINT_PAGE_SOTF_NEON_LETTERS",
        "Neon Letters",
        "recipe-a",
        "Neon Letter A (Small)",
        null,
        null,
        "page-a",
        target);

    CheckEqual(1, target.PageCount, "book registrar adds exactly one page");
    CheckEqual(
        "Neon Letters",
        target.Localizations["BLUEPRINT_PAGE_SOTF_NEON_LETTERS"],
        "book registrar adds its section title");
    CheckEqual(
        "Neon Letter A (Small)",
        target.Localizations["LOCALIZED_recipe-a"],
        "book registrar adds the recipe title");
    CheckEqual("recipe-a", target.LastTopRecipe, "book registrar assigns the top recipe");
    CheckEqual(null, target.LastBottomRecipe, "book registrar keeps the lower slot empty for A");
    CheckEqual("page-a", target.LastBackground, "book registrar assigns the tested page art");

    BookPageRegistrar.Register(
        "BLUEPRINT_PAGE_SOTF_NEON_LETTERS",
        "Neon Letters",
        "recipe-a",
        "Neon Letter A (Small)",
        "recipe-b",
        "Neon Letter B (Small)",
        "page-ab",
        target);

    CheckEqual(2, target.PageCount, "book registrar adds a paired alphabet page exactly once");
    CheckEqual("recipe-a", target.LastTopRecipe, "paired page keeps A in the top slot");
    CheckEqual("recipe-b", target.LastBottomRecipe, "paired page assigns B to the bottom slot");
    CheckEqual("page-ab", target.LastBackground, "paired page uses the A-B page art");
    CheckEqual(
        "Neon Letter B (Small)",
        target.Localizations["LOCALIZED_recipe-b"],
        "paired page registers the lower recipe title");

    var retryTarget = new FakeBookPageRegistrationTarget
    {
        FailLocalizationKeyOnce = "LOCALIZED_recipe-d"
    };
    bool firstRegistrationFailed = false;
    try
    {
        BookPageRegistrar.Register(
            "BLUEPRINT_PAGE_SOTF_NEON_LETTERS_02",
            "Neon Letters",
            "recipe-c",
            "Neon Letter C (Small)",
            "recipe-d",
            "Neon Letter D (Small)",
            "page-cd",
            retryTarget);
    }
    catch (InvalidOperationException)
    {
        firstRegistrationFailed = true;
    }

    CheckEqual(true, firstRegistrationFailed, "a post-create localization failure is observable");
    CheckEqual(1, retryTarget.PageCount, "the failed attempt already appended its page");

    BookPageRegistrar.Register(
        "BLUEPRINT_PAGE_SOTF_NEON_LETTERS_02",
        "Neon Letters",
        "recipe-c",
        "Neon Letter C (Small)",
        "recipe-d",
        "Neon Letter D (Small)",
        "page-cd",
        retryTarget);

    CheckEqual(1, retryTarget.PageCount, "retry reuses the matching page instead of duplicating it");
    CheckEqual(
        "Neon Letter D (Small)",
        retryTarget.Localizations["LOCALIZED_recipe-d"],
        "retry completes the localization that failed after page creation");
}

void CheckAlphabetCatalog()
{
    IReadOnlyList<NeonLetterSmallDefinition> definitions =
        NeonLetterSmallCatalog.All.Take(26).ToArray();
    char[] expectedLetters = Enumerable.Range('A', 26).Select(value => (char)value).ToArray();

    CheckEqual(
        180f,
        NeonLetterSmallCatalog.Placement.InitialRotationY,
        "the shared A-Z placement faces every letter toward the player");
    CheckEqual(26, definitions.Count, "small catalog contains the complete English alphabet");
    CheckSequence(expectedLetters, definitions.Select(definition => definition.Letter), "small catalog is ordered A-Z");
    CheckEqual(
        26,
        definitions.Select(definition => definition.RecipeId).Distinct().Count(),
        "every small letter has a unique recipe ID");
    CheckEqual(
        26,
        definitions.Select(definition => definition.CraftingNodeId).Distinct().Count(),
        "every small letter has a unique crafting-node ID");
    CheckEqual(
        26,
        definitions.Select(definition => definition.PrefabAssetName).Distinct().Count(),
        "every small letter has a unique prefab address");
    CheckEqual(
        26,
        definitions.Select(definition => definition.BookIconAssetName).Distinct().Count(),
        "every small letter has a unique icon address");
    CheckEqual(
        13,
        definitions.Select(definition => definition.BookPageAssetName).Distinct().Count(),
        "small alphabet uses thirteen two-recipe book pages");
    CheckEqual(
        13,
        definitions
            .Select(definition => definition.BookPageTitleLocalizationKey)
            .Distinct()
            .Count(),
        "small alphabet uses a unique title localization key for every book page");

    for (int index = 0; index < definitions.Count; index++)
    {
        NeonLetterSmallDefinition definition = definitions[index];
        char letter = expectedLetters[index];
        CheckEqual(
            $"Neon Letter {letter} (Small)",
            definition.RecipeName,
            $"{letter} recipe name is derived from its letter");
        CheckEqual(
            $"NeonLetter_{letter}_Small",
            definition.PrefabAssetName,
            $"{letter} prefab address is stable");
        CheckEqual(
            $"NeonLetter_{letter}_Small_Icon",
            definition.BookIconAssetName,
            $"{letter} icon address is stable");
        CheckEqual(
            NeonLetterSmallCatalog.BaseRecipeId + index * 2,
            definition.RecipeId,
            $"{letter} recipe ID follows the collision-free sequence");
        CheckEqual(definition.RecipeId - 1, definition.CraftingNodeId, $"{letter} node ID precedes its recipe ID");
        CheckEqual(index / 2, definition.BookPageIndex, $"{letter} maps to its paired book page");
        CheckEqual(
            $"BLUEPRINT_PAGE_SOTF_NEON_LETTERS_{index / 2 + 1:00}",
            definition.BookPageTitleLocalizationKey,
            $"{letter} uses its paired page localization key");
        CheckEqual(
            index % 2 == 0 ? NeonLetterBookSlot.Top : NeonLetterBookSlot.Bottom,
            definition.BookSlot,
            $"{letter} maps to the correct book-page slot");
        CheckSequence(
            new[]
            {
                new NeonLetterSmallDefinition.IngredientDefinition("Ingredient_Wire_Lead", 418),
                new NeonLetterSmallDefinition.IngredientDefinition($"Ingredient_LightBulb_{letter}", 635)
            },
            definition.Ingredients,
            $"{letter} requires one wire and one light bulb");

        CheckEqual(
            true,
            definition.IsColliderVisualChild($"Ingredient_LightBulb_{letter}"),
            $"{letter} sizes its collider from its own visible letter child");
        CheckEqual(
            false,
            definition.IsColliderVisualChild("Ingredient_Wire_Lead"),
            $"{letter} excludes its wire lead from collider bounds");
        NeonLetterSmallDefinition.ColliderSize collider =
            definition.ResolveColliderSize(0.40f + index, 0.50f + index, 0.01f);
        CheckEqual(0.40f + index, collider.Width, $"{letter} collider preserves visual width");
        CheckEqual(0.50f + index, collider.Height, $"{letter} collider preserves visual height");
        CheckEqual(
            NeonLetterSmallCatalog.MinimumColliderDepth,
            collider.Depth,
            $"{letter} collider clamps only the thin depth axis");
    }

    CheckEqual("g C Letters", NeonLetterSmallCatalog.Get('C').SourceNodeName, "C preserves its exceptional DAE node name");
    CheckEqual("g Letters A", NeonLetterSmallCatalog.Get('A').SourceNodeName, "A uses its canonical DAE node name");
    CheckEqual("g Letters Z", NeonLetterSmallCatalog.Get('Z').SourceNodeName, "Z uses its canonical DAE node name");
    CheckCatalogAgainstModelManifest(definitions);
}

void CheckExpandedSymbolCatalogContract()
{
    const string expectedSymbols =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ0123456789!#$&*+,-.=?";
    IReadOnlyList<NeonLetterSmallDefinition> definitions = NeonLetterSmallCatalog.All;
    IReadOnlyList<NeonSymbolManifestEntry> manifest = NeonSymbolManifest.All;

    CheckEqual(80, definitions.Count, "the small catalog contains every supported neon symbol");
    CheckEqual(80, manifest.Count, "the shared manifest contains every supported neon symbol");
    CheckSequence(
        expectedSymbols,
        definitions.Select(definition => definition.Symbol),
        "the Blueprints catalog keeps the approved symbol order");
    CheckSequence(
        expectedSymbols,
        manifest.Select(entry => entry.Symbol),
        "the shared manifest keeps the approved symbol order");
    CheckEqual(
        40,
        definitions.Select(definition => definition.BookPageIndex).Distinct().Count(),
        "eighty symbols fill forty paired Blueprints pages");
    CheckEqual(
        40,
        definitions.Select(definition => definition.BookPageAssetName).Distinct().Count(),
        "eighty symbols use forty deterministic page asset names");
    CheckEqual(
        40,
        definitions.Select(definition => definition.BookPageTitleLocalizationKey).Distinct().Count(),
        "eighty symbols use forty deterministic page localization keys");
    CheckEqual(
        "Neon Symbols",
        NeonLetterSmallCatalog.BookPageTitle,
        "all catalog pages expose the common Neon Symbols title");

    CheckEqual(80, definitions.Select(definition => definition.Symbol).Distinct().Count(),
        "every catalog symbol is unique");
    CheckEqual(80, definitions.Select(definition => definition.UnicodeCode).Distinct().Count(),
        "every catalog Unicode code is unique");
    CheckEqual(80, definitions.Select(definition => definition.SourceNodeName).Distinct().Count(),
        "every catalog source node is unique");
    CheckEqual(80, definitions.Select(definition => definition.AssetKey).Distinct().Count(),
        "every catalog asset key is unique");
    CheckEqual(80, definitions.Select(definition => definition.RecipeId).Distinct().Count(),
        "every catalog recipe ID is unique");
    CheckEqual(80, definitions.Select(definition => definition.CraftingNodeId).Distinct().Count(),
        "every catalog crafting-node ID is unique");
    CheckEqual(80, definitions.Select(definition => definition.PrefabAssetName).Distinct().Count(),
        "every catalog prefab name is unique");
    CheckEqual(80, definitions.Select(definition => definition.BookIconAssetName).Distinct().Count(),
        "every catalog icon name is unique");
    CheckEqual(80, definitions.Select(definition => definition.ColliderVisualChildName).Distinct().Count(),
        "every catalog light-bulb ingredient child name is unique");
    CheckEqual(
        true,
        definitions.All(definition =>
            definition.AssetKey.All(character =>
                character == '_' ||
                character is >= 'A' and <= 'Z' ||
                character is >= '0' and <= '9')),
        "every catalog asset key is ASCII-safe");

    for (int index = 0; index < definitions.Count; index++)
    {
        NeonLetterSmallDefinition definition = definitions[index];
        NeonSymbolManifestEntry entry = manifest[index];
        CheckEqual(entry.Symbol, definition.Symbol, $"catalog item {index} uses its manifest symbol");
        CheckEqual(entry.Symbol, definition.Letter, $"catalog item {index} preserves the Letter alias");
        CheckEqual(entry.UnicodeCode, definition.UnicodeCode,
            $"catalog item {index} uses its manifest Unicode code");
        CheckEqual(entry.AssetKey, definition.AssetKey,
            $"catalog item {index} uses its manifest asset key");
        CheckEqual(entry.SourceNodeName, definition.SourceNodeName,
            $"catalog item {index} uses its manifest source node");
        CheckEqual(entry.Source, definition.Source,
            $"catalog item {index} uses its manifest source kind");
        CheckEqual(NeonLetterSmallCatalog.BaseRecipeId + index * 2, definition.RecipeId,
            $"catalog item {index} uses deterministic recipe ID allocation");
        CheckEqual(definition.RecipeId - 1, definition.CraftingNodeId,
            $"catalog item {index} keeps its crafting node immediately before its recipe");
        CheckEqual(index / 2, definition.BookPageIndex,
            $"catalog item {index} uses its deterministic paired page");
        CheckEqual(index % 2 == 0 ? NeonLetterBookSlot.Top : NeonLetterBookSlot.Bottom,
            definition.BookSlot,
            $"catalog item {index} uses its deterministic paired-page slot");
        CheckEqual($"NeonLetter_{definition.AssetKey}_Small", definition.PrefabAssetName,
            $"catalog item {index} derives an ASCII-safe prefab name from its asset key");
        CheckEqual($"NeonLetter_{definition.AssetKey}_Small_Icon", definition.BookIconAssetName,
            $"catalog item {index} derives an ASCII-safe icon name from its asset key");
        CheckEqual($"Ingredient_LightBulb_{definition.AssetKey}", definition.ColliderVisualChildName,
            $"catalog item {index} derives its visual ingredient child from its asset key");
    }

    if (definitions.Count == 80)
    {
        CheckEqual('Я', definitions[58].Symbol, "the Cyrillic alphabet ends at the top of page 30");
        CheckEqual(NeonLetterBookSlot.Top, definitions[58].BookSlot, "Я occupies the top slot");
        CheckEqual('0', definitions[59].Symbol, "digit zero follows Я on page 30");
        CheckEqual(NeonLetterBookSlot.Bottom, definitions[59].BookSlot, "zero occupies the bottom slot");
        CheckEqual('9', definitions[68].Symbol, "digit nine ends at the top of page 35");
        CheckEqual(NeonLetterBookSlot.Top, definitions[68].BookSlot, "nine occupies the top slot");
        CheckEqual('!', definitions[69].Symbol, "punctuation starts below digit nine on page 35");
        CheckEqual(NeonLetterBookSlot.Bottom, definitions[69].BookSlot, "exclamation occupies the bottom slot");
        CheckEqual('?', definitions[79].Symbol, "question mark fills the final bottom slot");
        CheckEqual(39, definitions[79].BookPageIndex, "the final symbol fills page 40");
        CheckEqual(NeonLetterBookSlot.Bottom, definitions[79].BookSlot,
            "the final symbol leaves no empty page slot");
    }

    CheckEqual('Я', NeonLetterSmallCatalog.Get('я').Symbol,
        "catalog lookup normalizes lowercase Cyrillic symbols");
    CheckEqual('?', NeonLetterSmallCatalog.Get('?').Symbol,
        "catalog lookup resolves punctuation without scene-order assumptions");

    for (int index = 0; index < 26; index++)
    {
        char letter = (char)('A' + index);
        NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.Get(letter);
        string expectedSourceNode = letter == 'C' ? "g C Letters" : $"g Letters {letter}";
        CheckEqual(letter, definition.Symbol, $"legacy {letter} symbol remains stable");
        CheckEqual(letter, definition.Letter, $"legacy {letter} Letter API remains stable");
        CheckEqual($"U{(int)letter:X4}", definition.UnicodeCode,
            $"legacy {letter} Unicode code remains stable");
        CheckEqual(letter.ToString(), definition.AssetKey, $"legacy {letter} asset key remains stable");
        CheckEqual(expectedSourceNode, definition.SourceNodeName,
            $"legacy {letter} source node remains stable");
        CheckEqual(NeonSymbolSource.LegacyDae, definition.Source,
            $"legacy {letter} source kind remains stable");
        CheckEqual(NeonLetterSmallCatalog.BaseRecipeId + index * 2, definition.RecipeId,
            $"legacy {letter} recipe ID remains stable");
        CheckEqual(NeonLetterSmallCatalog.BaseRecipeId + index * 2 - 1, definition.CraftingNodeId,
            $"legacy {letter} crafting-node ID remains stable");
        CheckEqual($"NeonLetter_{letter}_Small", definition.PrefabAssetName,
            $"legacy {letter} prefab name remains stable");
        CheckEqual($"NeonLetter_{letter}_Small_Icon", definition.BookIconAssetName,
            $"legacy {letter} icon name remains stable");
        CheckEqual($"NeonLetters_Small_Page_{index / 2 + 1:00}", definition.BookPageAssetName,
            $"legacy {letter} page asset name remains stable");
        CheckEqual(index / 2, definition.BookPageIndex, $"legacy {letter} page remains stable");
        CheckEqual($"Ingredient_LightBulb_{letter}", definition.ColliderVisualChildName,
            $"legacy {letter} ingredient child remains stable");
    }

    string inventoryPath = FindRepositoryFile("assets/source/neon-symbols/symbol-inventory.json")
        ?? throw new InvalidOperationException("Could not find the extension symbol inventory.");
    using JsonDocument inventoryDocument = JsonDocument.Parse(File.ReadAllText(inventoryPath));
    JsonElement[] inventoryEntries = inventoryDocument.RootElement.EnumerateArray().ToArray();
    var extensionManifestBySymbol = manifest
        .Skip(26)
        .ToDictionary(entry => entry.Symbol);
    CheckEqual(
        inventoryEntries.Length,
        extensionManifestBySymbol.Count,
        "every extension inventory symbol is represented in the shared manifest");
    foreach (JsonElement inventoryEntry in inventoryEntries)
    {
        char symbol = inventoryEntry.GetProperty("symbol").GetString()![0];
        bool foundManifestEntry = extensionManifestBySymbol.TryGetValue(
            symbol,
            out NeonSymbolManifestEntry? manifestEntry);
        CheckEqual(
            true,
            foundManifestEntry,
            $"extension manifest contains the inventoried {symbol} symbol");
        if (!foundManifestEntry || manifestEntry == null)
        {
            continue;
        }

        CheckEqual(inventoryEntry.GetProperty("unicode").GetString(), manifestEntry.UnicodeCode,
            $"extension manifest {symbol} uses the inventoried Unicode code");
        CheckEqual(inventoryEntry.GetProperty("assetKey").GetString(), manifestEntry.AssetKey,
            $"extension manifest {symbol} uses the inventoried asset key");
        CheckEqual(inventoryEntry.GetProperty("sourceRoot").GetString(), manifestEntry.SourceNodeName,
            $"extension manifest {symbol} uses the inventoried source root");
        CheckEqual(NeonSymbolSource.ExtensionGlb, manifestEntry.Source,
            $"extension manifest {symbol} uses the GLB source kind");
    }
}

void CheckAlphabetRuntimeBindings()
{
    string assetsPath = FindRepositoryFile("Assets.cs")
        ?? throw new InvalidOperationException("Could not find the runtime asset bindings.");
    string legacySource = File.ReadAllText(assetsPath);
    CheckEqual(
        true,
        legacySource.Contains("public static partial class Assets", StringComparison.Ordinal),
        "legacy runtime asset bindings are extended through a partial class");

    foreach (NeonLetterSmallDefinition definition in NeonLetterSmallCatalog.All.Take(26))
    {
        char letter = definition.Letter;
        CheckEqual(
            true,
            legacySource.Contains(
                $"[AssetReference(\"{definition.PrefabAssetName}\")]\n" +
                $"    public static GameObject NeonLetter{letter}SmallPrefab",
                StringComparison.Ordinal),
            $"{letter} runtime prefab reference matches the catalog");
        CheckEqual(
            true,
            legacySource.Contains(
                $"[AssetReference(\"{definition.BookIconAssetName}\")]\n" +
                $"    public static Texture2D NeonLetter{letter}SmallBookIcon",
                StringComparison.Ordinal),
            $"{letter} runtime icon reference matches the catalog");
        CheckEqual(
            true,
            legacySource.Contains(
                $"'{letter}' => NeonLetter{letter}SmallPrefab",
                StringComparison.Ordinal),
            $"{letter} runtime prefab lookup returns its bound asset");
        CheckEqual(
            true,
            legacySource.Contains(
                $"'{letter}' => NeonLetter{letter}SmallBookIcon",
                StringComparison.Ordinal),
            $"{letter} runtime icon lookup returns its bound asset");
    }

    foreach (int pageIndex in Enumerable.Range(0, 13))
    {
        string pageName = $"NeonLetters_Small_Page_{pageIndex + 1:00}";
        string propertyName = $"NeonLettersSmallPage{pageIndex + 1:00}";
        CheckEqual(
            true,
            legacySource.Contains(
                $"[AssetReference(\"{pageName}\")]\n" +
                $"    public static Texture2D {propertyName}",
                StringComparison.Ordinal),
            $"book page {pageIndex + 1:00} runtime reference matches the catalog");
        CheckEqual(
            true,
            legacySource.Contains(
                $"{pageIndex} => {propertyName}",
                StringComparison.Ordinal),
            $"book page {pageIndex + 1:00} runtime lookup returns its bound asset");
    }

    CheckEqual(
        true,
        legacySource.Contains("_ => GetExtensionPrefab(letter)", StringComparison.Ordinal),
        "legacy prefab lookup delegates only its fallback to extension bindings");
    CheckEqual(
        true,
        legacySource.Contains("_ => GetExtensionBookIcon(letter)", StringComparison.Ordinal),
        "legacy icon lookup delegates only its fallback to extension bindings");
    CheckEqual(
        true,
        legacySource.Contains("_ => GetExtensionBookPage(pageIndex)", StringComparison.Ordinal),
        "legacy page lookup delegates only its fallback to extension bindings");

    string? extensionPath = FindRepositoryFile("Assets.Extension.cs");
    CheckEqual(
        false,
        extensionPath == null,
        "extension runtime asset bindings are isolated from legacy A-Z properties");
    if (extensionPath == null)
    {
        return;
    }

    string extensionSource = File.ReadAllText(extensionPath);
    foreach (NeonLetterSmallDefinition definition in NeonLetterSmallCatalog.All.Skip(26))
    {
        string propertyStem = $"NeonLetter_{definition.AssetKey}_Small";
        CheckEqual(
            true,
            extensionSource.Contains(
                $"[AssetReference(\"{definition.PrefabAssetName}\")]\n" +
                $"    public static GameObject {propertyStem}Prefab",
                StringComparison.Ordinal),
            $"{definition.Symbol} extension prefab reference matches the catalog");
        CheckEqual(
            true,
            extensionSource.Contains(
                $"[AssetReference(\"{definition.BookIconAssetName}\")]\n" +
                $"    public static Texture2D {propertyStem}BookIcon",
                StringComparison.Ordinal),
            $"{definition.Symbol} extension icon reference matches the catalog");
        CheckEqual(
            true,
            extensionSource.Contains(
                $"'{definition.Symbol}' => {propertyStem}Prefab",
                StringComparison.Ordinal),
            $"{definition.Symbol} extension prefab lookup returns its bound asset");
        CheckEqual(
            true,
            extensionSource.Contains(
                $"'{definition.Symbol}' => {propertyStem}BookIcon",
                StringComparison.Ordinal),
            $"{definition.Symbol} extension icon lookup returns its bound asset");
    }

    foreach (int pageIndex in Enumerable.Range(13, 27))
    {
        string pageName = $"NeonLetters_Small_Page_{pageIndex + 1:00}";
        string propertyName = $"NeonLettersSmallPage{pageIndex + 1:00}";
        CheckEqual(
            true,
            extensionSource.Contains(
                $"[AssetReference(\"{pageName}\")]\n" +
                $"    public static Texture2D {propertyName}",
                StringComparison.Ordinal),
            $"book page {pageIndex + 1:00} extension reference matches the catalog");
        CheckEqual(
            true,
            extensionSource.Contains(
                $"{pageIndex} => {propertyName}",
                StringComparison.Ordinal),
            $"book page {pageIndex + 1:00} extension lookup returns its bound asset");
    }

    CheckEqual(
        200,
        CountOccurrences(legacySource + extensionSource, "[AssetReference(\""),
        "runtime exposes exactly 80 prefabs, 80 icons and 40 Blueprints pages");
    CheckEqual(
        3,
        CountOccurrences(extensionSource, "throw new ArgumentOutOfRangeException("),
        "unknown extension symbols and page indexes remain rejected");

    string blueprintPath = FindRepositoryFile("NeonLetterASmallBlueprint.cs")
        ?? throw new InvalidOperationException("Could not find Blueprint registration.");
    string blueprintSource = File.ReadAllText(blueprintPath);
    CheckEqual(
        true,
        blueprintSource.Contains(
            "foreach (NeonLetterSmallDefinition definition in NeonLetterSmallCatalog.All)",
            StringComparison.Ordinal),
        "Blueprint registration consumes all eighty catalog definitions");
    CheckEqual(
        1,
        CountOccurrences(blueprintSource, "CustomBlueprintManager.TryRegister("),
        "Blueprint registration uses one existing TryRegister path for every catalog definition");
    CheckEqual(
        true,
        blueprintSource.Contains(
            "NeonLetterSmallCatalog.BookPageTitle",
            StringComparison.Ordinal),
        "Blueprint pages use the shared Neon Symbols title");
    CheckEqual(
        false,
        blueprintSource.Contains("Take(26)", StringComparison.Ordinal),
        "Blueprint registration has no A-Z-only truncation");
}

void CheckBookPageCoordinator()
{
    var coordinator = new AlphabetBookPageCoordinator<string>();
    var readyPages = new List<ReadyAlphabetBookPage<string>>();
    IReadOnlyList<NeonLetterSmallDefinition> definitions = NeonLetterSmallCatalog.All;

    foreach (NeonLetterSmallDefinition definition in definitions)
    {
        ReadyAlphabetBookPage<string>? readyPage = coordinator.Add(
            definition,
            $"recipe-{definition.AssetKey}");
        if (definition.Symbol == 'A')
        {
            CheckEqual<ReadyAlphabetBookPage<string>?>(
                null,
                readyPage,
                "alphabet coordinator waits for B after receiving A");
        }

        if (readyPage != null)
        {
            readyPages.Add(readyPage);
            coordinator.MarkCompleted(readyPage.PageIndex);
        }
    }

    CheckEqual(40, readyPages.Count, "eighty recipe callbacks create forty complete Blueprints pages");
    CheckSequence(
        Enumerable.Range(0, 40),
        readyPages.Select(page => page.PageIndex),
        "Neon Symbols pages are registered in catalog order");

    for (int pageIndex = 0; pageIndex < readyPages.Count; pageIndex++)
    {
        ReadyAlphabetBookPage<string> page = readyPages[pageIndex];
        NeonLetterSmallDefinition topDefinition = definitions[pageIndex * 2];
        NeonLetterSmallDefinition bottomDefinition = definitions[pageIndex * 2 + 1];
        CheckEqual(topDefinition.Symbol, page.TopDefinition.Symbol,
            $"page {pageIndex + 1:00} keeps its top symbol");
        CheckEqual(bottomDefinition.Symbol, page.BottomDefinition.Symbol,
            $"page {pageIndex + 1:00} keeps its bottom symbol");
        CheckEqual($"recipe-{topDefinition.AssetKey}", page.TopRecipe,
            $"page {pageIndex + 1:00} keeps its top recipe");
        CheckEqual($"recipe-{bottomDefinition.AssetKey}", page.BottomRecipe,
            $"page {pageIndex + 1:00} keeps its bottom recipe");
        CheckEqual("Neon Symbols", NeonLetterSmallCatalog.BookPageTitle,
            $"page {pageIndex + 1:00} uses the visible Neon Symbols title");
        CheckEqual(false, page.BottomDefinition == null,
            $"page {pageIndex + 1:00} has no empty bottom recipe");
    }

    CheckEqual('Я', readyPages[29].TopDefinition.Symbol,
        "the final Cyrillic symbol precedes the first digit");
    CheckEqual('0', readyPages[29].BottomDefinition.Symbol,
        "the first digit fills the next available page slot");
    CheckEqual('9', readyPages[34].TopDefinition.Symbol,
        "the final digit precedes punctuation");
    CheckEqual('!', readyPages[34].BottomDefinition.Symbol,
        "punctuation fills the next available page slot");
    CheckEqual('?', readyPages[39].BottomDefinition.Symbol,
        "the final punctuation symbol fills the final bottom slot");

    int repeatedPageCount = 0;
    foreach (NeonLetterSmallDefinition definition in definitions)
    {
        if (coordinator.Add(
                definition,
                $"replacement-{definition.AssetKey}") != null)
        {
            repeatedPageCount++;
        }
    }

    CheckEqual(0, repeatedPageCount, "repeated symbol callbacks do not duplicate ready pages");

    var reversePairCoordinator = new AlphabetBookPageCoordinator<string>();
    NeonLetterSmallDefinition a = NeonLetterSmallCatalog.Get('A');
    NeonLetterSmallDefinition b = NeonLetterSmallCatalog.Get('B');
    CheckEqual<ReadyAlphabetBookPage<string>?>(
        null,
        reversePairCoordinator.Add(b, "recipe-b"),
        "B-first callback waits for A");
    ReadyAlphabetBookPage<string>? reverseReadyPage =
        reversePairCoordinator.Add(a, "recipe-a");
    CheckEqual(false, reverseReadyPage == null, "B then A completes the paired page");
    if (reverseReadyPage != null)
    {
        CheckEqual('A', reverseReadyPage.TopDefinition.Letter, "B then A still places A on top");
        CheckEqual("recipe-a", reverseReadyPage.TopRecipe, "B then A keeps the A recipe on top");
        CheckEqual('B', reverseReadyPage.BottomDefinition.Letter, "B then A still places B below");
        CheckEqual("recipe-b", reverseReadyPage.BottomRecipe, "B then A keeps the B recipe below");
    }

    var shuffledPageCoordinator = new AlphabetBookPageCoordinator<string>();
    NeonLetterSmallDefinition c = NeonLetterSmallCatalog.Get('C');
    NeonLetterSmallDefinition d = NeonLetterSmallCatalog.Get('D');
    CheckEqual<ReadyAlphabetBookPage<string>?>(
        null,
        shuffledPageCoordinator.Add(c, "recipe-c"),
        "a later page waits for its pair");
    CheckEqual<ReadyAlphabetBookPage<string>?>(
        null,
        shuffledPageCoordinator.Add(d, "recipe-d"),
        "a complete later page waits until all preceding pages are registered");
    CheckEqual<ReadyAlphabetBookPage<string>?>(
        null,
        shuffledPageCoordinator.Add(a, "recipe-a"),
        "the first page still waits for its pair after a later page is buffered");
    ReadyAlphabetBookPage<string>? firstShuffledReadyPage =
        shuffledPageCoordinator.Add(b, "recipe-b");
    CheckEqual(false, firstShuffledReadyPage == null,
        "the first page becomes ready before the buffered later page");
    if (firstShuffledReadyPage != null)
    {
        CheckEqual(0, firstShuffledReadyPage.PageIndex,
            "shuffled callbacks still emit page one first");
        shuffledPageCoordinator.MarkCompleted(firstShuffledReadyPage.PageIndex);
    }

    ReadyAlphabetBookPage<string>? secondShuffledReadyPage =
        shuffledPageCoordinator.GetNextReadyPage();
    CheckEqual(false, secondShuffledReadyPage == null,
        "the buffered later page becomes ready after its predecessor completes");
    if (secondShuffledReadyPage != null)
    {
        CheckEqual(1, secondShuffledReadyPage.PageIndex,
            "shuffled callbacks emit the buffered page second");
    }

    string blueprintPath = FindRepositoryFile("NeonLetterASmallBlueprint.cs")
        ?? throw new InvalidOperationException("Could not find Blueprint registration.");
    string blueprintSource = File.ReadAllText(blueprintPath);
    CheckEqual(
        true,
        blueprintSource.Contains("while (readyPage != null)", StringComparison.Ordinal),
        "Blueprint registration drains every contiguous ready page");
    CheckEqual(
        true,
        blueprintSource.Contains(
            "readyPage = BookPageCoordinator.GetNextReadyPage();",
            StringComparison.Ordinal),
        "Blueprint registration continues with the next buffered page");

    var retryCoordinator = new AlphabetBookPageCoordinator<string>();
    retryCoordinator.Add(a, "recipe-a");
    ReadyAlphabetBookPage<string>? firstAttempt =
        retryCoordinator.Add(b, "recipe-b");
    ReadyAlphabetBookPage<string>? retryAttempt =
        retryCoordinator.Add(b, "recipe-b");
    CheckEqual(false, firstAttempt == null, "a complete pair is ready for its first registration attempt");
    CheckEqual(false, retryAttempt == null, "a failed page registration can be retried");
    retryCoordinator.MarkCompleted(0);
    CheckEqual<ReadyAlphabetBookPage<string>?>(
        null,
        retryCoordinator.Add(b, "recipe-b"),
        "a successfully registered page is not emitted again");
}

void CheckColorEditingContract()
{
    var original = new NeonRgba(0.10f, 0.20f, 0.30f, 0.40f);
    var preview = new NeonRgba(0.70f, 0.60f, 0.50f, 0.90f);
    var editor = new NeonLetterColorEditor(original);

    CheckEqual(original, editor.Original, "color editor starts with the source RGBA");
    CheckEqual(original, editor.Preview, "color editor initially previews the source RGBA");
    CheckEqual(original, editor.Committed, "color editor initially keeps the source RGBA committed");

    editor.SetPreview(preview);

    CheckEqual(original, editor.Original, "preview changes do not replace the source RGBA");
    CheckEqual(preview, editor.Preview, "preview changes expose the selected RGBA");
    CheckEqual(original, editor.Committed, "preview changes do not commit a color");

    NeonLetterColorDecision applied = editor.Apply();

    CheckEqual(preview, editor.Committed, "Apply commits the current preview");
    CheckEqual(preview, applied.Color, "Apply returns the committed RGBA for persistence");
    CheckEqual(true, applied.ShouldPersist, "Apply requests a persistent color change");

    var canceledEditor = new NeonLetterColorEditor(original);
    canceledEditor.SetPreview(preview);
    NeonLetterColorDecision canceled = canceledEditor.Cancel();

    CheckEqual(original, canceledEditor.Preview, "Cancel restores the source RGBA preview");
    CheckEqual(original, canceledEditor.Committed, "Cancel leaves the source RGBA committed");
    CheckEqual(original, canceled.Color, "Cancel returns the source RGBA");
    CheckEqual(false, canceled.ShouldPersist, "Cancel does not request a persistent color change");

    var resetEditor = new NeonLetterColorEditor(original);
    resetEditor.Reset();

    CheckEqual(NeonRgba.ProjectCyan, resetEditor.Preview, "Reset previews the project cyan");
    CheckEqual(original, resetEditor.Committed, "Reset remains a preview until Apply");
}

void CheckMultiplayerProtocolContract()
{
    CheckEqual(
        (byte)1,
        NeonLetterNetworkProtocol.CurrentVersion,
        "multiplayer packets use protocol version 1");

    uint packed = NeonLetterNetworkProtocol.Pack(
        new NeonRgba(0.10f, 0.50f, 1f, 1f));

    CheckEqual(
        0xFFFF801Au,
        packed,
        "multiplayer colors pack deterministically as RGBA32");
    CheckEqual(
        new NeonRgba(26f / 255f, 128f / 255f, 1f, 1f),
        NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            packed),
        "multiplayer colors unpack the quantized RGBA32 components");
    CheckEqual(
        0x0080FF00u,
        NeonLetterNetworkProtocol.Pack(new NeonRgba(-1f, 2f, 0.50f, 0f)),
        "multiplayer colors clamp components and round midpoint values away from zero");
    CheckEqual(
        0x0000004Du,
        NeonLetterNetworkProtocol.Pack(new NeonRgba(0.30f, 0f, 0f, 0f)),
        "multiplayer colors round an even-lower midpoint away from zero");
    CheckEqual(
        0x0000004Cu,
        NeonLetterNetworkProtocol.Pack(
            new NeonRgba(76.25f / byte.MaxValue, 0f, 0f, 0f)),
        "multiplayer colors round a nearby value below the midpoint down");
    CheckEqual(
        new NeonRgba(17f / 255f, 34f / 255f, 51f / 255f, 68f / 255f),
        NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            0x44332211u),
        "multiplayer colors unpack red, green, blue and alpha from their assigned bytes");

    var nonFiniteColors = new (string Component, NeonRgba Color)[]
    {
        ("red", new NeonRgba(float.NaN, 0f, 0f, 1f)),
        ("green", new NeonRgba(0f, float.PositiveInfinity, 0f, 1f)),
        ("blue", new NeonRgba(0f, 0f, float.NegativeInfinity, 1f)),
        ("alpha", new NeonRgba(0f, 0f, 0f, float.NaN))
    };
    foreach ((string component, NeonRgba color) in nonFiniteColors)
    {
        CheckThrows<InvalidOperationException>(
            () => NeonLetterNetworkProtocol.Pack(color),
            "finite",
            $"multiplayer colors reject a non-finite {component} component");
    }

    foreach (byte unsupportedVersion in new[]
             {
                 (byte)0,
                 (byte)(NeonLetterNetworkProtocol.CurrentVersion + 1)
             })
    {
        CheckThrows<InvalidOperationException>(
            () => NeonLetterNetworkProtocol.Unpack(unsupportedVersion, packed),
            "protocol version",
            $"multiplayer colors reject unsupported protocol version {unsupportedVersion}");
    }
}

void CheckMultiplayerStateContract()
{
    int aRecipeId = NeonLetterSmallCatalog.Get('A').RecipeId;
    int zRecipeId = NeonLetterSmallCatalog.Get('Z').RecipeId;
    var requestedColor = new NeonRgba(0.10f, 0.50f, 0.90f, 0.75f);
    NeonRgba canonicalColor = NeonLetterNetworkProtocol.Unpack(
        NeonLetterNetworkProtocol.CurrentVersion,
        NeonLetterNetworkProtocol.Pack(requestedColor));
    var authoritativeColors = new NeonLetterAuthoritativeColors<string>();

    NeonLetterColorAcceptance accepted = authoritativeColors.TryAccept(
        isHost: true,
        identity: "entity-a",
        isLive: true,
        recipeId: aRecipeId,
        color: requestedColor);

    CheckEqual(true, accepted.Accepted, "the host accepts a live A-Z color request");
    CheckEqual(
        canonicalColor,
        accepted.AuthoritativeColor,
        "the host returns the canonical RGBA32 color");
    CheckEqual(
        canonicalColor,
        authoritativeColors.Resolve("entity-a"),
        "the host stores the canonical RGBA32 color");

    var rejectedReplacement = new NeonRgba(1f, 0f, 0f, 1f);
    NeonLetterColorAcceptance clientRejection = authoritativeColors.TryAccept(
        isHost: false,
        identity: "entity-a",
        isLive: true,
        recipeId: aRecipeId,
        color: rejectedReplacement);
    CheckEqual(false, clientRejection.Accepted, "a client cannot accept authoritative color state");
    CheckEqual(
        canonicalColor,
        clientRejection.AuthoritativeColor,
        "a rejected client request returns the current authoritative color");
    CheckEqual(
        canonicalColor,
        authoritativeColors.Resolve("entity-a"),
        "a rejected client request does not replace an accepted color");

    CheckEqual(
        false,
        authoritativeColors.TryAccept(
            isHost: true,
            identity: "entity-a",
            isLive: false,
            recipeId: aRecipeId,
            color: rejectedReplacement).Accepted,
        "the host rejects a color for a non-live identity");
    CheckEqual(
        canonicalColor,
        authoritativeColors.Resolve("entity-a"),
        "a dead-identity rejection does not replace an accepted color");
    CheckEqual(
        false,
        authoritativeColors.TryAccept(
            isHost: true,
            identity: "entity-a",
            isLive: true,
            recipeId: int.MinValue,
            color: rejectedReplacement).Accepted,
        "the host rejects the missing recipe sentinel");
    CheckEqual(
        canonicalColor,
        authoritativeColors.Resolve("entity-a"),
        "a missing-recipe rejection does not replace an accepted color");
    CheckEqual(
        false,
        authoritativeColors.TryAccept(
            isHost: true,
            identity: "entity-a",
            isLive: true,
            recipeId: NeonLetterSmallCatalog.BaseRecipeId + 1,
            color: rejectedReplacement).Accepted,
        "the host rejects a recipe outside the A-Z catalog");
    CheckEqual(
        canonicalColor,
        authoritativeColors.Resolve("entity-a"),
        "an unknown-recipe rejection does not replace an accepted color");
    CheckEqual(
        NeonRgba.ProjectCyan,
        authoritativeColors.Resolve("entity-unknown"),
        "an identity without an accepted color resolves to project cyan");

    var secondColor = new NeonRgba(0.20f, 0.40f, 0.60f, 1f);
    var replacementColor = new NeonRgba(0.90f, 0.70f, 0.50f, 1f);
    NeonRgba canonicalReplacement = NeonLetterNetworkProtocol.Unpack(
        NeonLetterNetworkProtocol.CurrentVersion,
        NeonLetterNetworkProtocol.Pack(replacementColor));
    authoritativeColors.TryAccept(true, "entity-z", true, zRecipeId, secondColor);
    authoritativeColors.TryAccept(true, "entity-pruned", true, aRecipeId, requestedColor);
    authoritativeColors.TryAccept(true, "entity-a", true, aRecipeId, replacementColor);
    authoritativeColors.Remove("entity-pruned");

    NeonLetterAuthoritativeColorPage<string> page =
        authoritativeColors.CreatePage(
            cursorChangeSerial: 0,
            watermarkChangeSerial: 0);
    Dictionary<string, NeonRgba> pageByIdentity = page.Entries.ToDictionary(
        entry => entry.Identity,
        entry => entry.Color);
    CheckEqual(2, page.Entries.Count, "a color page includes each current customized identity once");
    CheckEqual(2, pageByIdentity.Count, "a color page does not duplicate customized identities");
    CheckEqual(
        canonicalReplacement,
        pageByIdentity["entity-a"],
        "a color page contains the latest accepted color for a live identity");
    CheckEqual(
        true,
        pageByIdentity.ContainsKey("entity-z"),
        "a color page contains every other current customized identity");
    CheckEqual(
        false,
        pageByIdentity.ContainsKey("entity-pruned"),
        "a color page excludes a removed customized identity");
    CheckEqual(
        NeonRgba.ProjectCyan,
        authoritativeColors.Resolve("entity-pruned"),
        "authoritative removal clears a dismantled identity");

    var replicatedState = new NeonLetterReplicatedColorState<string>(
        pendingCapacity: 2,
        pendingLifetimeSeconds: 5d);
    var immediateApplications = new List<KeyValuePair<string, NeonRgba>>();
    CheckEqual(
        true,
        replicatedState.Receive(
            "entity-live",
            canonicalColor,
            nowSeconds: 10d,
            isReady: _ => true,
            apply: (identity, color) => immediateApplications.Add(new(identity, color))),
        "host state applies immediately when its network identity is live");
    CheckSequence(
        new[] { new KeyValuePair<string, NeonRgba>("entity-live", canonicalColor) },
        immediateApplications,
        "immediate host state applies to its live letter exactly once");
    CheckEqual(
        canonicalColor,
        replicatedState.Resolve("entity-live"),
        "immediate host state becomes the client's resolved color");
    CheckEqual(
        0,
        replicatedState.PendingCount,
        "immediate host state never enters the pending queue");

    var latestStateWins = new NeonLetterReplicatedColorState<string>(
        pendingCapacity: 2,
        pendingLifetimeSeconds: 5d);
    latestStateWins.Receive(
        "entity-replaced-before-spawn",
        requestedColor,
        nowSeconds: 12d,
        isReady: _ => false,
        apply: (_, _) => { });
    var latestStateApplications = new List<NeonRgba>();
    CheckEqual(
        true,
        latestStateWins.Receive(
            "entity-replaced-before-spawn",
            replacementColor,
            nowSeconds: 13d,
            isReady: _ => true,
            apply: (_, color) => latestStateApplications.Add(color)),
        "a newer live host state replaces an older pre-spawn state");
    CheckEqual(
        0,
        latestStateWins.PendingCount,
        "applying the newer live host state removes the older pending state");
    CheckEqual(
        0,
        latestStateWins.DrainReady(
            nowSeconds: 14d,
            isReady: _ => true,
            apply: (_, color) => latestStateApplications.Add(color)),
        "a later drain cannot restore an older color after newer state applied");
    CheckSequence(
        new[] { replacementColor },
        latestStateApplications,
        "only the latest host state applies after an identity becomes live");
    CheckEqual(
        replacementColor,
        latestStateWins.Resolve("entity-replaced-before-spawn"),
        "the latest live host state remains the resolved color");

    var immediateRetryState = new NeonLetterReplicatedColorState<string>(
        pendingCapacity: 2,
        pendingLifetimeSeconds: 5d);
    CheckThrows<InvalidOperationException>(
        () => immediateRetryState.Receive(
            "entity-immediate-retry",
            secondColor,
            nowSeconds: 15d,
            isReady: _ => true,
            apply: (_, _) => throw new InvalidOperationException("immediate apply failed")),
        "immediate apply failed",
        "a failed immediate host-state apply remains observable");
    CheckEqual(
        1,
        immediateRetryState.PendingCount,
        "a failed immediate host-state apply remains queued for retry");
    var immediateRetryApplications = new List<NeonRgba>();
    CheckEqual(
        1,
        immediateRetryState.DrainReady(
            nowSeconds: 16d,
            isReady: _ => true,
            apply: (_, color) => immediateRetryApplications.Add(color)),
        "a failed immediate host-state apply can be retried");
    CheckSequence(
        new[] { secondColor },
        immediateRetryApplications,
        "retry preserves the immediately received host state");
    CheckEqual(
        secondColor,
        immediateRetryState.Resolve("entity-immediate-retry"),
        "a successful immediate-state retry commits the replicated color");

    int outgoingRequestCount = 0;
    CheckEqual(
        false,
        replicatedState.Receive(
            "entity-before-spawn",
            secondColor,
            nowSeconds: 20d,
            isReady: _ => false,
            apply: (_, _) => outgoingRequestCount++),
        "host state received before entity spawn is queued without applying");
    CheckEqual(
        1,
        replicatedState.PendingCount,
        "pre-spawn host state remains pending until its identity resolves");
    CheckEqual(
        0,
        outgoingRequestCount,
        "receiving host state cannot emit an outgoing color request");

    var drainedApplications = new List<KeyValuePair<string, NeonRgba>>();
    CheckEqual(
        1,
        replicatedState.DrainReady(
            nowSeconds: 21d,
            isReady: identity => identity == "entity-before-spawn",
            apply: (identity, color) => drainedApplications.Add(new(identity, color))),
        "a spawned identity applies its queued host state");
    CheckEqual(
        0,
        replicatedState.PendingCount,
        "applying queued host state removes its pending entry");
    CheckEqual(
        secondColor,
        replicatedState.Resolve("entity-before-spawn"),
        "applying queued host state commits the client's resolved color");
    CheckEqual(
        0,
        replicatedState.DrainReady(
            nowSeconds: 21d,
            isReady: _ => true,
            apply: (identity, color) => drainedApplications.Add(new(identity, color))),
        "applied queued host state cannot run a second time");
    CheckEqual(
        1,
        drainedApplications.Count,
        "queued host state invokes emission exactly once");

    replicatedState.Receive(
        "entity-clear-resolved",
        replacementColor,
        nowSeconds: 30d,
        isReady: _ => true,
        apply: (_, _) => { });
    replicatedState.Receive(
        "entity-clear-pending",
        requestedColor,
        nowSeconds: 30d,
        isReady: _ => false,
        apply: (_, _) => { });
    replicatedState.Clear();
    CheckEqual(
        NeonRgba.ProjectCyan,
        replicatedState.Resolve("entity-clear-resolved"),
        "world cleanup removes resolved replicated colors");
    CheckEqual(
        0,
        replicatedState.PendingCount,
        "world cleanup removes pre-spawn replicated colors");

    replicatedState.Receive(
        "entity-apply-retry",
        requestedColor,
        nowSeconds: 40d,
        isReady: _ => false,
        apply: (_, _) => { });
    CheckThrows<InvalidOperationException>(
        () => replicatedState.DrainReady(
            nowSeconds: 41d,
            isReady: _ => true,
            apply: (_, _) => throw new InvalidOperationException("replicated apply failed")),
        "replicated apply failed",
        "a failed queued host-state apply remains observable");
    CheckEqual(
        1,
        replicatedState.PendingCount,
        "a failed queued host-state apply remains pending for retry");
    var retriedReplicatedColors = new List<NeonRgba>();
    CheckEqual(
        1,
        replicatedState.DrainReady(
            nowSeconds: 42d,
            isReady: _ => true,
            apply: (_, color) => retriedReplicatedColors.Add(color)),
        "a failed queued host-state apply can be retried after the entity recovers");
    CheckSequence(
        new[] { requestedColor },
        retriedReplicatedColors,
        "retry applies the same queued host state");
    CheckEqual(
        requestedColor,
        replicatedState.Resolve("entity-apply-retry"),
        "a successful retry commits the replicated color");

    var isolatedFailures = new NeonLetterReplicatedColorState<string>(
        pendingCapacity: 4,
        pendingLifetimeSeconds: 5d);
    isolatedFailures.Receive(
        "entity-broken-first",
        requestedColor,
        nowSeconds: 50d,
        isReady: _ => false,
        apply: (_, _) => { });
    isolatedFailures.Receive(
        "entity-valid-second",
        replacementColor,
        nowSeconds: 50.5d,
        isReady: _ => false,
        apply: (_, _) => { });
    var isolatedApplications = new List<string>();
    var isolatedApplyErrors = new List<(string Identity, string Message)>();
    CheckEqual(
        1,
        isolatedFailures.DrainReady(
            nowSeconds: 51d,
            isReady: _ => true,
            apply: (identity, _) =>
            {
                if (identity == "entity-broken-first")
                {
                    throw new InvalidOperationException("first emission failed");
                }

                isolatedApplications.Add(identity);
            },
            onApplyError: (identity, exception) =>
                isolatedApplyErrors.Add((identity, exception.Message))),
        "one failed pending color does not block a later ready color");
    CheckSequence(
        new[] { "entity-valid-second" },
        isolatedApplications,
        "the later ready color applies exactly once in the same drain");
    CheckSequence(
        new[] { ("entity-broken-first", "first emission failed") },
        isolatedApplyErrors,
        "a failed pending color reports its identity and exception");
    CheckEqual(
        1,
        isolatedFailures.PendingCount,
        "only the failed pending color remains queued for retry");
    CheckEqual(
        NeonRgba.ProjectCyan,
        isolatedFailures.Resolve("entity-broken-first"),
        "a failed pending color is not committed");
    CheckEqual(
        replacementColor,
        isolatedFailures.Resolve("entity-valid-second"),
        "a later successful pending color commits despite an earlier failure");

    CheckEqual(
        1,
        isolatedFailures.DrainReady(
            nowSeconds: 52d,
            isReady: _ => true,
            apply: (identity, _) => isolatedApplications.Add(identity),
            onApplyError: (identity, exception) =>
                isolatedApplyErrors.Add((identity, exception.Message))),
        "the failed pending color can apply on a later retry");
    CheckSequence(
        new[] { "entity-valid-second", "entity-broken-first" },
        isolatedApplications,
        "retry applies the failed color without reapplying the prior success");
    CheckEqual(
        0,
        isolatedFailures.PendingCount,
        "a successful retry removes the formerly failed pending color");
    CheckEqual(
        requestedColor,
        isolatedFailures.Resolve("entity-broken-first"),
        "a successful retry commits the formerly failed color");
    CheckEqual(
        0,
        isolatedFailures.DrainReady(
            nowSeconds: 52d,
            isReady: _ => true,
            apply: (identity, _) => isolatedApplications.Add(identity),
            onApplyError: (identity, exception) =>
                isolatedApplyErrors.Add((identity, exception.Message))),
        "a completed retry cannot apply either color again");
    CheckEqual(
        1,
        isolatedApplyErrors.Count,
        "the recovered color reports only its original failed attempt");

    var pendingColors = new NeonLetterPendingColors<string>(capacity: 2, lifetimeSeconds: 5d);
    var appliedColors = new List<KeyValuePair<string, NeonRgba>>();
    pendingColors.Enqueue("entity-late", requestedColor, nowSeconds: 10d);
    CheckEqual(
        0,
        pendingColors.ApplyReady(
            nowSeconds: 11d,
            isReady: _ => false,
            apply: (identity, color) => appliedColors.Add(new(identity, color))),
        "a pending color remains queued before its identity is ready");
    CheckEqual(1, pendingColors.Count, "an unresolved identity remains in the pending queue");
    CheckEqual(
        1,
        pendingColors.ApplyReady(
            nowSeconds: 12d,
            isReady: identity => identity == "entity-late",
            apply: (identity, color) => appliedColors.Add(new(identity, color))),
        "a ready identity applies its pending color once");
    CheckEqual(0, pendingColors.Count, "an applied pending color disappears from the queue");
    CheckEqual(
        0,
        pendingColors.ApplyReady(
            nowSeconds: 12d,
            isReady: _ => true,
            apply: (identity, color) => appliedColors.Add(new(identity, color))),
        "an applied pending color cannot be applied a second time");
    CheckEqual(1, appliedColors.Count, "a ready pending color invokes its apply action exactly once");

    var firstPendingColor = new NeonRgba(1f, 0f, 0f, 1f);
    var latestPendingColor = new NeonRgba(0f, 1f, 0f, 1f);
    var retryAfterFailure = new NeonLetterPendingColors<string>(capacity: 2, lifetimeSeconds: 5d);
    retryAfterFailure.Enqueue("entity-retry", requestedColor, nowSeconds: 40d);
    CheckThrows<InvalidOperationException>(
        () => retryAfterFailure.ApplyReady(
            nowSeconds: 41d,
            isReady: _ => true,
            apply: (_, _) => throw new InvalidOperationException("apply failed")),
        "apply failed",
        "a failing apply action remains observable to the caller");
    CheckEqual(
        1,
        retryAfterFailure.Count,
        "a failing apply action leaves the same pending color queued for retry");
    var retryApplications = new List<NeonRgba>();
    CheckEqual(
        1,
        retryAfterFailure.ApplyReady(
            nowSeconds: 42d,
            isReady: _ => true,
            apply: (_, color) => retryApplications.Add(color)),
        "a pending color can be retried after its apply action fails");
    CheckSequence(
        new[] { requestedColor },
        retryApplications,
        "retry applies the same color that failed previously");
    CheckEqual(0, retryAfterFailure.Count, "a successful retry removes the pending color");

    var reentrantReplacement = new NeonLetterPendingColors<string>(capacity: 2, lifetimeSeconds: 5d);
    reentrantReplacement.Enqueue("entity-reentrant", firstPendingColor, nowSeconds: 50d);
    var reentrantApplications = new List<NeonRgba>();
    CheckEqual(
        1,
        reentrantReplacement.ApplyReady(
            nowSeconds: 51d,
            isReady: _ => true,
            apply: (identity, color) =>
            {
                reentrantApplications.Add(color);
                reentrantReplacement.Enqueue(identity, latestPendingColor, nowSeconds: 51d);
            }),
        "applying an older pending color completes once when it is replaced re-entrantly");
    CheckEqual(
        1,
        reentrantReplacement.Count,
        "a re-entrant replacement survives completion of the older apply action");
    CheckEqual(
        1,
        reentrantReplacement.ApplyReady(
            nowSeconds: 52d,
            isReady: _ => true,
            apply: (_, color) => reentrantApplications.Add(color)),
        "a surviving re-entrant replacement applies later exactly once");
    CheckEqual(
        0,
        reentrantReplacement.ApplyReady(
            nowSeconds: 52d,
            isReady: _ => true,
            apply: (_, color) => reentrantApplications.Add(color)),
        "an applied re-entrant replacement cannot be applied again");
    CheckSequence(
        new[] { firstPendingColor, latestPendingColor },
        reentrantApplications,
        "re-entrant replacement applies after the older color without duplication");

    var clearAndReplace = new NeonLetterPendingColors<string>(capacity: 2, lifetimeSeconds: 5d);
    clearAndReplace.Enqueue("entity-clear-reentrant", firstPendingColor, nowSeconds: 60d);
    var clearAndReplaceApplications = new List<NeonRgba>();
    CheckEqual(
        1,
        clearAndReplace.ApplyReady(
            nowSeconds: 61d,
            isReady: _ => true,
            apply: (identity, color) =>
            {
                clearAndReplaceApplications.Add(color);
                clearAndReplace.Clear();
                clearAndReplace.Enqueue(identity, latestPendingColor, nowSeconds: 61d);
            }),
        "clearing and replacing during apply completes the older color once");
    CheckEqual(
        1,
        clearAndReplace.Count,
        "a same-identity replacement queued after Clear survives the older apply action");
    CheckEqual(
        1,
        clearAndReplace.ApplyReady(
            nowSeconds: 62d,
            isReady: _ => true,
            apply: (_, color) => clearAndReplaceApplications.Add(color)),
        "a same-identity replacement queued after Clear applies later exactly once");
    CheckEqual(
        0,
        clearAndReplace.ApplyReady(
            nowSeconds: 62d,
            isReady: _ => true,
            apply: (_, color) => clearAndReplaceApplications.Add(color)),
        "an applied same-identity replacement queued after Clear cannot apply again");
    CheckSequence(
        new[] { firstPendingColor, latestPendingColor },
        clearAndReplaceApplications,
        "Clear-time replacement applies after the older color without duplication");

    pendingColors.Enqueue("entity-replaced", firstPendingColor, nowSeconds: 20d);
    pendingColors.Enqueue("entity-replaced", latestPendingColor, nowSeconds: 23d);
    CheckEqual(1, pendingColors.Count, "replacing one identity keeps one pending entry");
    var replacementApplications = new List<NeonRgba>();
    CheckEqual(
        1,
        pendingColors.ApplyReady(
            nowSeconds: 26d,
            isReady: _ => true,
            apply: (_, color) => replacementApplications.Add(color)),
        "replacing a pending identity refreshes its expiry");
    CheckSequence(
        new[] { latestPendingColor },
        replacementApplications,
        "replacing a pending identity keeps only its latest color");

    pendingColors.Enqueue("entity-oldest", requestedColor, nowSeconds: 30d);
    pendingColors.Enqueue("entity-refreshed", firstPendingColor, nowSeconds: 31d);
    pendingColors.Enqueue("entity-refreshed", latestPendingColor, nowSeconds: 32d);
    pendingColors.Enqueue("entity-newest", secondColor, nowSeconds: 33d);
    CheckEqual(2, pendingColors.Count, "the pending queue never exceeds its fixed capacity");
    var capacityApplications = new Dictionary<string, NeonRgba>();
    pendingColors.ApplyReady(
        nowSeconds: 34d,
        isReady: _ => true,
        apply: (identity, color) => capacityApplications.Add(identity, color));
    CheckEqual(
        false,
        capacityApplications.ContainsKey("entity-oldest"),
        "capacity overflow evicts the oldest pending identity");
    CheckEqual(
        latestPendingColor,
        capacityApplications["entity-refreshed"],
        "refreshing an identity makes its latest state eligible to survive eviction");
    CheckEqual(
        true,
        capacityApplications.ContainsKey("entity-newest"),
        "capacity overflow retains the newest pending identity");

    pendingColors.Enqueue("entity-expiring", requestedColor, nowSeconds: 100d);
    pendingColors.Prune(nowSeconds: 104.999d);
    CheckEqual(1, pendingColors.Count, "a pending color remains before its expiry boundary");
    pendingColors.Prune(nowSeconds: 105d);
    CheckEqual(0, pendingColors.Count, "a pending color expires when now reaches its expiry");

    authoritativeColors.TryAccept(true, "entity-clear", true, aRecipeId, requestedColor);
    pendingColors.Enqueue("entity-clear", requestedColor, nowSeconds: 200d);
    authoritativeColors.Clear();
    pendingColors.Clear();
    CheckEqual(
        NeonRgba.ProjectCyan,
        authoritativeColors.Resolve("entity-clear"),
        "clearing authoritative state removes every world color");
    CheckEqual(0, pendingColors.Count, "clearing pending state removes every queued color");

    pendingColors.Enqueue("entity-after-clear-oldest", firstPendingColor, nowSeconds: 210d);
    pendingColors.Enqueue("entity-after-clear-newer", latestPendingColor, nowSeconds: 211d);
    pendingColors.Enqueue("entity-after-clear-newest", secondColor, nowSeconds: 212d);
    CheckEqual(
        2,
        pendingColors.Count,
        "a cleared pending queue keeps its fixed capacity when reused");
    var postClearApplications = new Dictionary<string, NeonRgba>();
    pendingColors.ApplyReady(
        nowSeconds: 213d,
        isReady: _ => true,
        apply: (identity, color) => postClearApplications.Add(identity, color));
    CheckEqual(
        false,
        postClearApplications.ContainsKey("entity-after-clear-oldest"),
        "a reused queue evicts its oldest post-Clear identity first");
    CheckEqual(
        true,
        postClearApplications.ContainsKey("entity-after-clear-newer"),
        "a reused queue retains the newer post-Clear identity");
    CheckEqual(
        true,
        postClearApplications.ContainsKey("entity-after-clear-newest"),
        "a reused queue retains the newest post-Clear identity");

    foreach (int invalidCapacity in new[] { 0, -1 })
    {
        CheckThrows<ArgumentOutOfRangeException>(
            () => new NeonLetterPendingColors<string>(invalidCapacity, 5d),
            "capacity",
            $"pending colors reject capacity {invalidCapacity}");
    }

    foreach (double invalidLifetime in new[]
             {
                 0d,
                 -1d,
                 double.NaN,
                 double.PositiveInfinity,
                 double.NegativeInfinity
             })
    {
        CheckThrows<ArgumentOutOfRangeException>(
            () => new NeonLetterPendingColors<string>(2, invalidLifetime),
            "lifetime",
            $"pending colors reject lifetime {invalidLifetime}");
    }

    foreach (double invalidNow in new[]
             {
                 double.NaN,
                 double.PositiveInfinity,
                 double.NegativeInfinity
             })
    {
        var invalidEnqueueTime = new NeonLetterPendingColors<string>(2, 5d);
        CheckThrows<ArgumentOutOfRangeException>(
            () => invalidEnqueueTime.Enqueue("entity-invalid", requestedColor, invalidNow),
            "finite",
            $"pending colors reject Enqueue time {invalidNow}");
        CheckEqual(
            0,
            invalidEnqueueTime.Count,
            $"rejecting Enqueue time {invalidNow} leaves the queue unchanged");

        var invalidApplyTime = new NeonLetterPendingColors<string>(2, 5d);
        invalidApplyTime.Enqueue("entity-stable", requestedColor, nowSeconds: 0d);
        int invalidApplyCalls = 0;
        CheckThrows<ArgumentOutOfRangeException>(
            () => invalidApplyTime.ApplyReady(
                invalidNow,
                _ => true,
                (_, _) => invalidApplyCalls++),
            "finite",
            $"pending colors reject ApplyReady time {invalidNow}");
        CheckEqual(
            1,
            invalidApplyTime.Count,
            $"rejecting ApplyReady time {invalidNow} keeps the pending entry");
        CheckEqual(
            0,
            invalidApplyCalls,
            $"rejecting ApplyReady time {invalidNow} does not call the apply action");

        var invalidPruneTime = new NeonLetterPendingColors<string>(2, 5d);
        invalidPruneTime.Enqueue("entity-stable", requestedColor, nowSeconds: 0d);
        CheckThrows<ArgumentOutOfRangeException>(
            () => invalidPruneTime.Prune(invalidNow),
            "finite",
            $"pending colors reject Prune time {invalidNow}");
        CheckEqual(
            1,
            invalidPruneTime.Count,
            $"rejecting Prune time {invalidNow} keeps the pending entry");
    }

    var overflowingExpiry = new NeonLetterPendingColors<string>(
        capacity: 2,
        lifetimeSeconds: double.MaxValue);
    CheckThrows<ArgumentOutOfRangeException>(
        () => overflowingExpiry.Enqueue(
            "entity-overflow",
            requestedColor,
            nowSeconds: double.MaxValue),
        "expiry",
        "pending colors reject an Enqueue time whose calculated expiry is non-finite");
    CheckEqual(
        0,
        overflowingExpiry.Count,
        "rejecting a non-finite calculated expiry leaves the queue unchanged");
}

void CheckMultiplayerPersistenceContract()
{
    int recipeG = NeonLetterSmallCatalog.Get('G').RecipeId;
    int recipeH = NeonLetterSmallCatalog.Get('H').RecipeId;
    uint packedColor = NeonLetterNetworkProtocol.Pack(
        new NeonRgba(0.25f, 0.50f, 0.75f, 1f));
    var validEntry = new NeonLetterMultiplayerSaveEntry
    {
        RecipeId = recipeG,
        NativeSaveId = 42,
        Position = new NeonVector3(1f, 2f, 3f),
        Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
        PackedColor = packedColor
    };
    var envelope = new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry> { validEntry }
    };

    string json = LoaderUtils.JsonSerialize(envelope);
    NeonLetterMultiplayerSaveEnvelope? roundTrip =
        LoaderUtils.JsonDeserialize<NeonLetterMultiplayerSaveEnvelope>(json);

    CheckEqual(false, roundTrip == null, "multiplayer world state round-trips through RedLoader LoaderUtils");
    if (roundTrip != null)
    {
        CheckEqual(
            NeonLetterMultiplayerSaveEnvelope.CurrentVersion,
            roundTrip.Version,
            "multiplayer world state round-trip retains the envelope version");
        CheckEqual(1, roundTrip.Entries.Count, "multiplayer world state round-trip retains its entry");
        CheckEqual(recipeG, roundTrip.Entries[0].RecipeId, "multiplayer world state round-trip retains the recipe ID");
        CheckEqual(42, roundTrip.Entries[0].NativeSaveId, "multiplayer world state round-trip retains the native SaveId");
        CheckEqual(1f, roundTrip.Entries[0].Position.X, "multiplayer world state round-trip retains position X");
        CheckEqual(2f, roundTrip.Entries[0].Position.Y, "multiplayer world state round-trip retains position Y");
        CheckEqual(3f, roundTrip.Entries[0].Position.Z, "multiplayer world state round-trip retains position Z");
        CheckEqual(0f, roundTrip.Entries[0].Rotation.X, "multiplayer world state round-trip retains rotation X");
        CheckEqual(0f, roundTrip.Entries[0].Rotation.Y, "multiplayer world state round-trip retains rotation Y");
        CheckEqual(0f, roundTrip.Entries[0].Rotation.Z, "multiplayer world state round-trip retains rotation Z");
        CheckEqual(1f, roundTrip.Entries[0].Rotation.W, "multiplayer world state round-trip retains rotation W");
        CheckEqual(packedColor, roundTrip.Entries[0].PackedColor, "multiplayer world state round-trip retains packed RGBA32");
    }

    var filteredSource = new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry>
        {
            new()
            {
                RecipeId = int.MinValue,
                Position = new NeonVector3(4f, 5f, 6f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = packedColor
            },
            new()
            {
                RecipeId = recipeH,
                NativeSaveId = 73,
                Position = new NeonVector3(7f, 8f, 9f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = packedColor
            }
        }
    };
    NeonLetterMultiplayerSaveEnvelope filtered =
        NeonLetterMultiplayerPersistencePolicy.Sanitize(filteredSource);

    CheckEqual(1, filtered.Entries.Count, "unknown multiplayer recipes are filtered without dropping later valid entries");
    CheckEqual(recipeH, filtered.Entries[0].RecipeId, "a valid A-Z recipe after an unknown recipe is retained");
    filteredSource.Entries[1].Position.X = 99f;
    CheckEqual(7f, filtered.Entries[0].Position.X, "sanitizing multiplayer world state takes an isolated scalar snapshot");

    NeonLetterMultiplayerSaveEnvelope unsupported =
        NeonLetterMultiplayerPersistencePolicy.Sanitize(
            new NeonLetterMultiplayerSaveEnvelope
            {
                Version = NeonLetterMultiplayerSaveEnvelope.CurrentVersion + 1,
                Entries = new List<NeonLetterMultiplayerSaveEntry> { validEntry }
            });
    CheckEqual(
        NeonLetterMultiplayerSaveEnvelope.CurrentVersion,
        unsupported.Version,
        "an unsupported multiplayer envelope resets to the current empty version");
    CheckEqual(0, unsupported.Entries.Count, "an unsupported multiplayer envelope cannot enter restore state");

    var malformedTransforms = new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry>
        {
            new()
            {
                RecipeId = recipeG,
                Position = new NeonVector3(float.NaN, 0f, 0f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = packedColor
            },
            new()
            {
                RecipeId = recipeG,
                Position = new NeonVector3(0f, 0f, 0f),
                Rotation = new NeonQuaternion(0f, float.PositiveInfinity, 0f, 1f),
                PackedColor = packedColor
            },
            validEntry
        }
    };
    NeonLetterMultiplayerSaveEnvelope finiteOnly =
        NeonLetterMultiplayerPersistencePolicy.Sanitize(malformedTransforms);
    CheckEqual(1, finiteOnly.Entries.Count, "non-finite positions and rotations are rejected without dropping a later valid entry");
    CheckEqual(recipeG, finiteOnly.Entries[0].RecipeId, "a valid transform after non-finite transforms remains restorable");

    CheckEqual(
        0.001f,
        NeonLetterMultiplayerPersistencePolicy.QuaternionMagnitudeTolerance,
        "quaternion validation exposes its small floating-point tolerance");
    var malformedRotations = new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry>
        {
            new()
            {
                RecipeId = recipeG,
                Position = new NeonVector3(0f, 0f, 0f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 0f),
                PackedColor = packedColor
            },
            new()
            {
                RecipeId = recipeG,
                Position = new NeonVector3(0f, 0f, 0f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 2f),
                PackedColor = packedColor
            },
            new()
            {
                RecipeId = recipeH,
                Position = new NeonVector3(0f, 0f, 0f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 0.99975f),
                PackedColor = packedColor
            }
        }
    };
    NeonLetterMultiplayerSaveEnvelope normalizedOnly =
        NeonLetterMultiplayerPersistencePolicy.Sanitize(malformedRotations);
    CheckEqual(1, normalizedOnly.Entries.Count, "zero and non-normalized rotations are rejected within an explicit tolerance");
    CheckEqual(0.99975f, normalizedOnly.Entries[0].Rotation.W, "a tolerated quaternion is copied without silent normalization");

    NeonLetterMultiplayerSaveEnvelope clientSave =
        NeonLetterMultiplayerPersistencePolicy.CreateWorldPayload(
            isMultiplayer: true,
            isHost: false,
            envelope);
    CheckEqual(0, clientSave.Entries.Count, "a known multiplayer client cannot produce a world payload");
    NeonLetterMultiplayerSaveEnvelope clientLoad =
        NeonLetterMultiplayerPersistencePolicy.AcceptLoadedWorldPayload(
            isMultiplayer: true,
            isHost: false,
            envelope);
    CheckEqual(0, clientLoad.Entries.Count, "a known multiplayer client cannot accept a loaded world payload");

    var nativeRestore = new NeonLetterMultiplayerRestoreEntryState(validEntry);
    CheckEqual(
        NeonLetterMultiplayerRestoreDecision.UseNative,
        nativeRestore.Decide(nativeIdentityResolved: true, resolvedRecipeId: recipeG),
        "the host prefers a native SaveId that resolves the exact saved recipe");

    var mismatchedNative = new NeonLetterMultiplayerRestoreEntryState(validEntry);
    CheckEqual(
        NeonLetterMultiplayerRestoreDecision.Skip,
        mismatchedNative.Decide(nativeIdentityResolved: true, resolvedRecipeId: recipeH),
        "a native SaveId reused by another recipe is skipped");
    CheckEqual(
        NeonLetterMultiplayerRestoreDecision.Skip,
        mismatchedNative.Decide(nativeIdentityResolved: false, resolvedRecipeId: default),
        "a mismatched native SaveId never becomes a fallback spawn if the lookup later changes");

    var missingNative = new NeonLetterMultiplayerRestoreEntryState(validEntry);
    CheckEqual(
        NeonLetterMultiplayerRestoreDecision.SpawnFallback,
        missingNative.Decide(nativeIdentityResolved: false, resolvedRecipeId: default),
        "a missing native identity requests one fallback spawn");
    missingNative.MarkFallbackSpawnStarted();
    CheckEqual(
        NeonLetterMultiplayerRestoreDecision.Skip,
        missingNative.Decide(nativeIdentityResolved: false, resolvedRecipeId: default),
        "a started fallback spawn cannot be requested again");

    CheckEqual(
        true,
        NeonLetterMultiplayerPersistencePolicy.TryDecodeColor(
            NeonLetterNetworkProtocol.CurrentVersion,
            packedColor,
            out NeonRgba decodedColor),
        "saved RGBA32 is decoded with the current multiplayer protocol version");
    CheckEqual(
        NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            packedColor),
        decodedColor,
        "saved RGBA32 restores the protocol-canonical color");
    CheckEqual(
        false,
        NeonLetterMultiplayerPersistencePolicy.TryDecodeColor(
            (byte)(NeonLetterNetworkProtocol.CurrentVersion + 1),
            packedColor,
            out NeonRgba invalidVersionColor),
        "an unsupported color protocol version cannot enter restore");
    CheckEqual(default, invalidVersionColor, "a rejected color protocol version leaks no decoded color");
}

void CheckMultiplayerRestoreCoordinatorContract()
{
    int recipeId = NeonLetterSmallCatalog.Get('G').RecipeId;
    uint packedColor = NeonLetterNetworkProtocol.Pack(
        new NeonRgba(0.25f, 0.50f, 0.75f, 1f));
    var envelope = new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry>
        {
            new()
            {
                RecipeId = recipeId,
                NativeSaveId = 0,
                Position = new NeonVector3(1f, 2f, 3f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = packedColor
            }
        }
    };
    var coordinator = new NeonLetterMultiplayerRestoreCoordinator<string>();
    coordinator.Stage(envelope);
    coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);

    NeonLetterMultiplayerRestoreObservationKind observationKind =
        NeonLetterMultiplayerRestoreObservationKind.ProcessedRecipeUnavailable;
    int fallbackStarts = 0;
    var restoredTargets = new List<string>();

    void Advance()
    {
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, fallbackStarted, spawnedTarget) =>
            {
                string? readyTarget =
                    observationKind ==
                    NeonLetterMultiplayerRestoreObservationKind.FallbackTargetReady
                        ? spawnedTarget
                        : null;
                return new NeonLetterMultiplayerRestoreObservation<string>(
                    observationKind,
                    readyTarget);
            },
            startFallback: _ =>
            {
                fallbackStarts++;
                return "fallback-g";
            },
            applyRestored: (_, target) =>
            {
                restoredTargets.Add(target);
                return true;
            },
            onEntryError: (_, exception) =>
                failures.Add($"unexpected coordinator error: {exception.Message}"));
    }

    Advance();
    CheckEqual(1, coordinator.PendingCount, "an unavailable processed recipe remains pending");
    CheckEqual(0, fallbackStarts, "an unavailable processed recipe cannot start fallback");

    observationKind =
        NeonLetterMultiplayerRestoreObservationKind.FallbackPrefabUnavailable;
    Advance();
    CheckEqual(1, coordinator.PendingCount, "an unavailable built prefab remains pending");
    CheckEqual(0, fallbackStarts, "an unavailable built prefab cannot start fallback");

    observationKind =
        NeonLetterMultiplayerRestoreObservationKind.ReadyToSpawnFallback;
    Advance();
    CheckEqual(1, fallbackStarts, "later recipe readiness starts fallback exactly once");
    CheckEqual(1, coordinator.PendingCount, "a started fallback remains pending attachment");
    CheckEqual(1, coordinator.StartedFallbackCount, "fallback spawn bookkeeping records the started entry");

    observationKind =
        NeonLetterMultiplayerRestoreObservationKind.FallbackTargetUnavailable;
    Advance();
    Advance();
    CheckEqual(1, fallbackStarts, "pending fallback attachment cannot duplicate the spawn");
    CheckEqual(1, coordinator.PendingCount, "an unattached fallback remains pending");

    observationKind =
        NeonLetterMultiplayerRestoreObservationKind.FallbackTargetReady;
    Advance();
    CheckSequence(
        new[] { "fallback-g" },
        restoredTargets,
        "a later attached fallback progresses through the coordinator exactly once");
    CheckEqual(0, coordinator.PendingCount, "a restored fallback leaves no pending entry");
}

void CheckMultiplayerNativeRestoreCoordinatorContract()
{
    int recipeG = NeonLetterSmallCatalog.Get('G').RecipeId;
    int recipeH = NeonLetterSmallCatalog.Get('H').RecipeId;
    uint packedColor = NeonLetterNetworkProtocol.Pack(
        new NeonRgba(0.25f, 0.50f, 0.75f, 1f));
    var envelope = new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry>
        {
            new()
            {
                RecipeId = recipeG,
                NativeSaveId = 42,
                Position = new NeonVector3(1f, 2f, 3f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = packedColor
            }
        }
    };
    var coordinator = new NeonLetterMultiplayerRestoreCoordinator<string>();
    coordinator.Stage(envelope);
    coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);

    NeonLetterMultiplayerRestoreObservationKind observationKind =
        NeonLetterMultiplayerRestoreObservationKind.NativeRecipeUnavailable;
    int fallbackStarts = 0;
    var restoredTargets = new List<string>();

    void Advance(int? resolvedRecipeId = null)
    {
        coordinator.Advance(
            nowSeconds: 0d,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    observationKind,
                    observationKind ==
                    NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady
                        ? "native-g"
                        : null,
                    resolvedRecipeId),
            startFallback: _ =>
            {
                fallbackStarts++;
                return "unexpected-fallback";
            },
            applyRestored: (_, target) =>
            {
                restoredTargets.Add(target);
                return true;
            },
            onEntryError: (_, exception) =>
                failures.Add($"unexpected native coordinator error: {exception.Message}"));
    }

    Advance();
    CheckEqual(1, coordinator.PendingCount, "a resolved native structure with no recipe remains pending");
    CheckEqual(0, fallbackStarts, "a native structure with no recipe never falls back");

    observationKind =
        NeonLetterMultiplayerRestoreObservationKind.NativeTargetUnavailable;
    Advance(recipeG);
    Advance(recipeG);
    CheckEqual(1, coordinator.PendingCount, "a matching native recipe with an unavailable entity or attachment remains pending");
    CheckEqual(0, fallbackStarts, "a matching native recipe never falls back while its entity is unavailable");

    observationKind =
        NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady;
    Advance(recipeG);
    CheckSequence(
        new[] { "native-g" },
        restoredTargets,
        "a later live native entity restores through the coordinator exactly once");
    CheckEqual(0, coordinator.PendingCount, "a restored native entry leaves pending state");

    var mismatchCoordinator =
        new NeonLetterMultiplayerRestoreCoordinator<string>();
    mismatchCoordinator.Stage(envelope);
    mismatchCoordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
    mismatchCoordinator.Advance(
        nowSeconds: 0d,
        observe: (_, _, _) =>
            new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind.NativeRecipeMismatch,
                Target: null,
                ResolvedRecipeId: recipeH),
        startFallback: _ =>
        {
            fallbackStarts++;
            return "unexpected-fallback";
        },
        applyRestored: (_, _) => true,
        onEntryError: (_, exception) =>
            failures.Add($"unexpected mismatch coordinator error: {exception.Message}"));
    CheckEqual(0, mismatchCoordinator.PendingCount, "a definite native recipe mismatch is terminally skipped");
    mismatchCoordinator.Advance(
        nowSeconds: 0d,
        observe: (_, _, _) =>
            new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind.ReadyToSpawnFallback),
        startFallback: _ =>
        {
            fallbackStarts++;
            return "unexpected-fallback";
        },
        applyRestored: (_, _) => true,
        onEntryError: (_, exception) =>
            failures.Add($"unexpected post-mismatch error: {exception.Message}"));
    CheckEqual(0, fallbackStarts, "a definite native recipe mismatch can never become fallback");
}

void CheckMultiplayerRestoreFailureIsolationContract()
{
    int recipeId = NeonLetterSmallCatalog.Get('J').RecipeId;
    uint packedColor = NeonLetterNetworkProtocol.Pack(
        new NeonRgba(0.10f, 0.20f, 0.30f, 1f));
    NeonLetterMultiplayerSaveEntry Entry(int saveId)
    {
        return new NeonLetterMultiplayerSaveEntry
        {
            RecipeId = recipeId,
            NativeSaveId = saveId,
            Position = new NeonVector3(saveId, 0f, 0f),
            Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
            PackedColor = packedColor
        };
    }

    var coordinator = new NeonLetterMultiplayerRestoreCoordinator<string>();
    coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry>
        {
            Entry(1),
            Entry(2),
            Entry(3)
        }
    });
    coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
    var errors = new List<int>();
    var restoredSaveIds = new List<int>();

    coordinator.Advance(
        nowSeconds: 0d,
        observe: (entry, _, _) => entry.NativeSaveId switch
        {
            1 => new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind.ProcessedRecipeUnavailable),
            2 => throw new InvalidOperationException("native lookup failed"),
            _ => new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                "native-j",
                recipeId)
        },
        startFallback: _ => "unexpected-fallback",
        applyRestored: (entry, _) =>
        {
            restoredSaveIds.Add(entry.NativeSaveId);
            return true;
        },
        onEntryError: (entry, _) => errors.Add(entry.NativeSaveId));

    CheckSequence(new[] { 2 }, errors, "one thrown restore error is reported for its own entry");
    CheckSequence(new[] { 3 }, restoredSaveIds, "a later ready entry restores after an earlier entry throws");
    CheckEqual(1, coordinator.PendingCount, "a transient entry remains pending when another entry throws");

    coordinator.Advance(
        nowSeconds: 0d,
        observe: (_, _, _) =>
            new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                "native-j",
                recipeId),
        startFallback: _ => "unexpected-fallback",
        applyRestored: (entry, _) =>
        {
            restoredSaveIds.Add(entry.NativeSaveId);
            return true;
        },
        onEntryError: (entry, _) => errors.Add(entry.NativeSaveId));
    CheckSequence(new[] { 3, 1 }, restoredSaveIds, "the retained transient entry progresses on a later update");
    CheckEqual(0, coordinator.PendingCount, "successful retry readiness leaves no pending entries");
}

void CheckMultiplayerRestoreRoleContract()
{
    int recipeId = NeonLetterSmallCatalog.Get('K').RecipeId;
    var envelope = new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry>
        {
            new()
            {
                RecipeId = recipeId,
                NativeSaveId = 0,
                Position = new NeonVector3(1f, 2f, 3f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = NeonLetterNetworkProtocol.Pack(
                    new NeonRgba(0.40f, 0.50f, 0.60f, 1f))
            }
        }
    };

    var clientCoordinator =
        new NeonLetterMultiplayerRestoreCoordinator<string>();
    clientCoordinator.Stage(envelope);
    CheckEqual(true, clientCoordinator.HasStagedEnvelope, "role-unknown Load keeps a sanitized staged envelope");
    clientCoordinator.SetRole(NeonLetterMultiplayerRestoreRole.Client);
    CheckEqual(false, clientCoordinator.HasStagedEnvelope, "a known client clears staged multiplayer world state");
    CheckEqual(0, clientCoordinator.PendingCount, "a known client accepts no pending multiplayer restore entries");
    clientCoordinator.Stage(envelope);
    CheckEqual(false, clientCoordinator.HasStagedEnvelope, "Load is rejected when the multiplayer client role is already known");

    var singlePlayerCoordinator =
        new NeonLetterMultiplayerRestoreCoordinator<string>();
    singlePlayerCoordinator.Stage(envelope);
    singlePlayerCoordinator.SetRole(
        NeonLetterMultiplayerRestoreRole.SinglePlayer);
    CheckEqual(false, singlePlayerCoordinator.HasStagedEnvelope, "the Single Player spawn signal clears staged multiplayer world state");
    CheckEqual(0, singlePlayerCoordinator.PendingCount, "Single Player accepts no pending multiplayer restore entries");
    singlePlayerCoordinator.Stage(envelope);
    CheckEqual(false, singlePlayerCoordinator.HasStagedEnvelope, "Load is rejected when Single Player is already known");

    var worldCoordinator =
        new NeonLetterMultiplayerRestoreCoordinator<string>();
    worldCoordinator.Stage(envelope);
    CheckEqual(
        true,
        worldCoordinator.HasStagedEnvelope,
        "world-clear setup has staged role-unknown state");
    worldCoordinator.Clear();
    CheckEqual(
        false,
        worldCoordinator.HasStagedEnvelope,
        "world exit clears staged multiplayer state");

    worldCoordinator.Stage(envelope);
    worldCoordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
    worldCoordinator.Advance(
        nowSeconds: 0d,
        observe: (_, _, _) =>
            new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind.ReadyToSpawnFallback),
        startFallback: _ => "fallback-k",
        applyRestored: (_, _) => true,
        onEntryError: (_, exception) =>
            failures.Add($"unexpected world-clear setup error: {exception.Message}"));
    CheckEqual(1, worldCoordinator.PendingCount, "world-clear setup has one pending fallback");
    CheckEqual(1, worldCoordinator.StartedFallbackCount, "world-clear setup records one started fallback");

    worldCoordinator.Clear();
    CheckEqual(false, worldCoordinator.HasStagedEnvelope, "world exit retains no staged multiplayer state");
    CheckEqual(0, worldCoordinator.PendingCount, "world exit clears pending multiplayer state");
    CheckEqual(0, worldCoordinator.StartedFallbackCount, "world exit clears fallback-spawn bookkeeping");
    CheckEqual(
        NeonLetterMultiplayerRestoreRole.Unknown,
        worldCoordinator.Role,
        "world exit resets multiplayer restore role discovery");
}

void CheckMultiplayerRestoreReadinessContract()
{
    int recipeId = NeonLetterSmallCatalog.Get('L').RecipeId;
    const double arbitraryDelaySeconds = 1_000_000d;

    NeonLetterMultiplayerSaveEntry Entry(int nativeSaveId)
    {
        return new NeonLetterMultiplayerSaveEntry
        {
            RecipeId = recipeId,
            NativeSaveId = nativeSaveId,
            Position = new NeonVector3(nativeSaveId, 0f, 0f),
            Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
            PackedColor = NeonLetterNetworkProtocol.Pack(
                new NeonRgba(0.30f, 0.60f, 0.90f, 1f))
        };
    }

    NeonLetterMultiplayerRestoreCoordinator<string> HostCoordinator(
        params NeonLetterMultiplayerSaveEntry[] entries)
    {
        var coordinator =
            new NeonLetterMultiplayerRestoreCoordinator<string>();
        coordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = entries.ToList()
        });
        coordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
        return coordinator;
    }

    var unchangedCoordinator = HostCoordinator(Entry(1));
    var unchangedErrors = new List<Exception>();
    void AdvanceUnchanged(double nowSeconds)
    {
        unchangedCoordinator.Advance(
            nowSeconds,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable),
            startFallback: _ => "unexpected-fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) =>
                unchangedErrors.Add(exception));
    }

    AdvanceUnchanged(100d);
    AdvanceUnchanged(100d + arbitraryDelaySeconds);
    CheckEqual(
        1,
        unchangedCoordinator.PendingCount,
        "an unchanged unavailable restore stage remains pending after an arbitrary delay");
    CheckEqual(
        0,
        unchangedErrors.Count,
        "an unavailable restore stage reports no elapsed-time error");

    var temporaryCoordinator = HostCoordinator(Entry(2));
    var temporaryRestores = new List<int>();
    temporaryCoordinator.Advance(
        nowSeconds: 200d,
        observe: (_, _, _) =>
            new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ProcessedRecipeUnavailable),
        startFallback: _ => "unexpected-fallback",
        applyRestored: (_, _) => true,
        onEntryError: (_, exception) =>
            failures.Add($"unexpected temporary readiness error: {exception.Message}"));
    temporaryCoordinator.Advance(
        nowSeconds: 200d + arbitraryDelaySeconds,
        observe: (_, _, _) =>
            new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                "native-l",
                recipeId),
        startFallback: _ => "unexpected-fallback",
        applyRestored: (entry, _) =>
        {
            temporaryRestores.Add(entry.NativeSaveId);
            return true;
        },
        onEntryError: (_, exception) =>
            failures.Add($"unexpected temporary restore error: {exception.Message}"));
    CheckSequence(
        new[] { 2 },
        temporaryRestores,
        "temporary unavailability that becomes ready later restores normally");
    CheckEqual(
        0,
        temporaryCoordinator.PendingCount,
        "a temporary unavailable entry leaves no pending state after restore");

    var transitionCoordinator = HostCoordinator(Entry(0));
    var transitionErrors = new List<Exception>();
    int transitionFallbackStarts = 0;
    NeonLetterMultiplayerRestoreObservationKind transitionKind =
        NeonLetterMultiplayerRestoreObservationKind.ProcessedRecipeUnavailable;
    void AdvanceTransition(double nowSeconds)
    {
        transitionCoordinator.Advance(
            nowSeconds,
            observe: (_, _, spawnedTarget) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    transitionKind,
                    transitionKind ==
                    NeonLetterMultiplayerRestoreObservationKind
                        .FallbackTargetReady
                        ? spawnedTarget
                        : null),
            startFallback: _ =>
            {
                transitionFallbackStarts++;
                return "fallback-l";
            },
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) =>
                transitionErrors.Add(exception));
    }

    AdvanceTransition(300d);
    transitionKind =
        NeonLetterMultiplayerRestoreObservationKind.ReadyToSpawnFallback;
    AdvanceTransition(300d + arbitraryDelaySeconds);
    CheckEqual(
        1,
        transitionFallbackStarts,
        "delayed readiness progress can start fallback exactly once");

    transitionKind =
        NeonLetterMultiplayerRestoreObservationKind.FallbackTargetUnavailable;
    AdvanceTransition(300d + arbitraryDelaySeconds);
    AdvanceTransition(300d + arbitraryDelaySeconds * 2d);
    CheckEqual(
        1,
        transitionCoordinator.PendingCount,
        "fallback attachment remains pending across arbitrary delay");
    CheckEqual(
        1,
        transitionFallbackStarts,
        "time advances while awaiting attachment cannot duplicate fallback spawn");

    transitionKind =
        NeonLetterMultiplayerRestoreObservationKind.FallbackTargetReady;
    AdvanceTransition(300d + arbitraryDelaySeconds * 3d);
    CheckEqual(
        0,
        transitionCoordinator.PendingCount,
        "a fallback that attaches after an arbitrary delay restores normally");
    CheckEqual(
        1,
        transitionFallbackStarts,
        "a delayed fallback entry never starts a second fallback");
    CheckEqual(
        0,
        transitionErrors.Count,
        "delayed fallback attachment reports no elapsed-time error");

    var continuingCoordinator = HostCoordinator(Entry(11), Entry(12));
    var continuingErrors = new List<int>();
    var continuingRestores = new List<int>();
    continuingCoordinator.Advance(
        nowSeconds: 400d,
        observe: (_, _, _) =>
            new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ProcessedRecipeUnavailable),
        startFallback: _ => "unexpected-fallback",
        applyRestored: (_, _) => true,
        onEntryError: (entry, _) =>
            continuingErrors.Add(entry.NativeSaveId));
    continuingCoordinator.Advance(
        nowSeconds: 400d + arbitraryDelaySeconds,
        observe: (entry, _, _) => entry.NativeSaveId == 11
            ? new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ProcessedRecipeUnavailable)
            : new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind.NativeTargetReady,
                "native-l",
                recipeId),
        startFallback: _ => "unexpected-fallback",
        applyRestored: (entry, _) =>
        {
            continuingRestores.Add(entry.NativeSaveId);
            return true;
        },
        onEntryError: (entry, _) =>
            continuingErrors.Add(entry.NativeSaveId));
    CheckSequence(
        Array.Empty<int>(),
        continuingErrors,
        "an unavailable entry reports no elapsed-time restore error");
    CheckSequence(
        new[] { 12 },
        continuingRestores,
        "a ready entry restores while an earlier entry remains unavailable");
    CheckEqual(
        1,
        continuingCoordinator.PendingCount,
        "the unavailable entry stays pending while the ready entry completes");

    var invalidStagedCoordinator =
        new NeonLetterMultiplayerRestoreCoordinator<string>();
    invalidStagedCoordinator.Stage(new NeonLetterMultiplayerSaveEnvelope
    {
        Entries = new List<NeonLetterMultiplayerSaveEntry> { Entry(20) }
    });
    CheckThrows<ArgumentOutOfRangeException>(
        () => invalidStagedCoordinator.Advance(
            nowSeconds: double.NaN,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .ProcessedRecipeUnavailable),
            startFallback: _ => "unexpected-fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, _) => { }),
        "finite and non-negative",
        "restore coordination rejects invalid time before role resolution");
    CheckEqual(
        true,
        invalidStagedCoordinator.HasStagedEnvelope,
        "invalid time does not clear staged restore state");

    var invalidPendingCoordinator = HostCoordinator(Entry(0));
    int invalidFallbackStarts = 0;
    int invalidObserveCalls = 0;
    invalidPendingCoordinator.Advance(
        nowSeconds: 500d,
        observe: (_, _, _) =>
            new NeonLetterMultiplayerRestoreObservation<string>(
                NeonLetterMultiplayerRestoreObservationKind
                    .ReadyToSpawnFallback),
        startFallback: _ =>
        {
            invalidFallbackStarts++;
            return "fallback-invalid";
        },
        applyRestored: (_, _) => true,
        onEntryError: (_, exception) =>
            failures.Add($"unexpected invalid-time setup error: {exception.Message}"));

    foreach (double invalidNowSeconds in new[]
             {
                 -1d,
                 double.NaN,
                 double.PositiveInfinity,
                 double.NegativeInfinity
             })
    {
        CheckThrows<ArgumentOutOfRangeException>(
            () => invalidPendingCoordinator.Advance(
                invalidNowSeconds,
                observe: (_, _, _) =>
                {
                    invalidObserveCalls++;
                    return new NeonLetterMultiplayerRestoreObservation<string>(
                        NeonLetterMultiplayerRestoreObservationKind
                            .FallbackTargetUnavailable);
                },
                startFallback: _ =>
                {
                    invalidFallbackStarts++;
                    return "duplicate-fallback";
                },
                applyRestored: (_, _) => true,
                onEntryError: (_, _) => { }),
            "finite and non-negative",
            $"restore coordination rejects invalid time {invalidNowSeconds}");
    }

    CheckEqual(
        0,
        invalidObserveCalls,
        "invalid time invokes no per-entry restore observation");
    CheckEqual(
        1,
        invalidPendingCoordinator.PendingCount,
        "invalid time does not remove pending restore state");
    CheckEqual(
        1,
        invalidPendingCoordinator.StartedFallbackCount,
        "invalid time does not clear fallback-started bookkeeping");
    CheckEqual(
        1,
        invalidFallbackStarts,
        "invalid time cannot duplicate fallback spawn");

    var roleResetCoordinator = HostCoordinator(Entry(30));
    var roleResetErrors = new List<Exception>();
    void AdvanceRoleReset(double nowSeconds)
    {
        roleResetCoordinator.Advance(
            nowSeconds,
            observe: (_, _, _) =>
                new NeonLetterMultiplayerRestoreObservation<string>(
                    NeonLetterMultiplayerRestoreObservationKind
                        .NativeTargetUnavailable),
            startFallback: _ => "unexpected-fallback",
            applyRestored: (_, _) => true,
            onEntryError: (_, exception) =>
                roleResetErrors.Add(exception));
    }

    AdvanceRoleReset(600d);
    AdvanceRoleReset(600d + arbitraryDelaySeconds);
    roleResetCoordinator.SetRole(NeonLetterMultiplayerRestoreRole.Host);
    AdvanceRoleReset(600d + arbitraryDelaySeconds * 2d);
    AdvanceRoleReset(600d + arbitraryDelaySeconds * 3d);
    CheckEqual(
        1,
        roleResetCoordinator.PendingCount,
        "setting restore role preserves pending readiness");
    CheckEqual(
        0,
        roleResetErrors.Count,
        "role updates cannot create elapsed-time readiness errors");

    AdvanceRoleReset(600d + arbitraryDelaySeconds * 4d);
    CheckEqual(
        1,
        roleResetCoordinator.PendingCount,
        "an unchanged stage remains pending after another arbitrary delay");
}

void CheckColorPersistenceContract()
{
    var firstColor = new NeonRgba(0.25f, 0.50f, 0.75f, 1f);
    var replacementColor = new NeonRgba(0.90f, 0.40f, 0.20f, 0.80f);
    var envelope = new NeonLetterColorSaveEnvelope();

    CheckEqual(
        NeonLetterColorSaveEnvelope.CurrentVersion,
        envelope.Version,
        "new color saves use the current envelope version");

    NeonLetterColorStore.Upsert(
        envelope,
        new NeonLetterColorSaveEntry(101, 1_904_177_201, firstColor));
    NeonLetterColorStore.Upsert(
        envelope,
        new NeonLetterColorSaveEntry(101, 1_904_177_201, replacementColor));

    CheckEqual(1, envelope.Entries.Count, "color save keeps at most one entry per SaveId");
    CheckEqual(101, envelope.Entries[0].SaveId, "color save entry retains its SaveId");
    CheckEqual(
        1_904_177_201,
        envelope.Entries[0].RecipeId,
        "color save entry retains its recipe ID");
    CheckEqual(replacementColor, envelope.Entries[0].Color, "color save entry retains its RGBA");

    CheckEqual<NeonRgba?>(
        replacementColor,
        NeonLetterColorStore.Resolve(envelope, 101, 1_904_177_201),
        "matching SaveId and recipe ID resolve the persisted RGBA");
    CheckEqual<NeonRgba?>(
        null,
        NeonLetterColorStore.Resolve(envelope, 101, 1_904_177_203),
        "a stale SaveId with a different recipe ID is ignored");
    CheckEqual<NeonRgba?>(
        null,
        NeonLetterColorStore.Resolve(envelope, 999, 1_904_177_201),
        "an unknown SaveId does not resolve a color");

    NeonLetterColorSaveEnvelope nullEntriesEnvelope =
        JsonSerializer.Deserialize<NeonLetterColorSaveEnvelope>(
            "{\"Version\":1,\"Entries\":null}")!;
    bool nullEntriesHandled = true;
    try
    {
        NeonLetterColorStore.Upsert(
            nullEntriesEnvelope,
            new NeonLetterColorSaveEntry(202, 1_904_177_203, firstColor));
    }
    catch (Exception)
    {
        nullEntriesHandled = false;
    }

    CheckEqual(true, nullEntriesHandled, "a deserialized null Entries list is normalized during Upsert");
    CheckEqual(
        1,
        nullEntriesEnvelope.Entries?.Count ?? 0,
        "Upsert retains the new color after normalizing a null Entries list");

    NeonLetterColorSaveEnvelope nullEntryEnvelope =
        JsonSerializer.Deserialize<NeonLetterColorSaveEnvelope>(
            JsonSerializer.Serialize(
                new NeonLetterColorSaveEnvelope
                {
                    Entries = new List<NeonLetterColorSaveEntry>
                    {
                        null!,
                        new(303, 1_904_177_205, replacementColor)
                    }
                }))!;
    NeonRgba? colorAfterNullEntry = null;
    bool nullEntryHandled = true;
    try
    {
        colorAfterNullEntry =
            NeonLetterColorStore.Resolve(nullEntryEnvelope, 303, 1_904_177_205);
    }
    catch (Exception)
    {
        nullEntryHandled = false;
    }

    CheckEqual(true, nullEntryHandled, "Resolve ignores a null entry in a deserialized Entries list");
    CheckEqual<NeonRgba?>(
        replacementColor,
        colorAfterNullEntry,
        "Resolve continues past a null entry to a matching saved color");

    var unsupportedVersionEnvelope = new NeonLetterColorSaveEnvelope
    {
        Version = NeonLetterColorSaveEnvelope.CurrentVersion + 1,
        Entries = new List<NeonLetterColorSaveEntry>
        {
            new(404, 1_904_177_207, firstColor)
        }
    };
    CheckEqual<NeonRgba?>(
        null,
        NeonLetterColorStore.Resolve(
            unsupportedVersionEnvelope,
            404,
            1_904_177_207),
        "Resolve rejects a save envelope with an unsupported version");

    string json = LoaderUtils.JsonSerialize(envelope);
    NeonLetterColorSaveEnvelope? restored =
        LoaderUtils.JsonDeserialize<NeonLetterColorSaveEnvelope>(json);

    CheckEqual(false, restored == null, "color save envelope round-trips through RedLoader LoaderUtils");
    if (restored != null)
    {
        CheckEqual(envelope.Version, restored.Version, "round-trip retains the envelope version");
        CheckEqual(1, restored.Entries.Count, "round-trip retains the SaveId entry count");
        CheckEqual(101, restored.Entries[0].SaveId, "round-trip retains the SaveId");
        CheckEqual(
            1_904_177_201,
            restored.Entries[0].RecipeId,
            "round-trip retains the recipe ID");
        CheckEqual(replacementColor, restored.Entries[0].Color, "round-trip retains the RGBA");
    }
}

void CheckExtendedSymbolRuntimePolicyContract()
{
    var representativeSymbols = new (char Symbol, NeonRgba Color)[]
    {
        ('Я', new NeonRgba(0.95f, 0.10f, 0.25f, 1f)),
        ('7', new NeonRgba(0.20f, 0.80f, 0.35f, 0.90f)),
        ('?', new NeonRgba(0.30f, 0.45f, 1f, 0.75f))
    };
    var colorEnvelope = new NeonLetterColorSaveEnvelope();
    var multiplayerEnvelope = new NeonLetterMultiplayerSaveEnvelope();
    var restoreTargets = new Dictionary<int, FakeColorRestoreTarget>();
    var validSaveIds = new Dictionary<char, int>();
    var invalidSaveIds = new List<int>();
    var authoritativeColors = new NeonLetterAuthoritativeColors<string>();

    for (int index = 0; index < representativeSymbols.Length; index++)
    {
        (char symbol, NeonRgba color) = representativeSymbols[index];
        NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.Get(symbol);
        int validSaveId = 1_000 + index * 10;
        int craftingNodeSaveId = validSaveId + 1;
        int unrelatedSaveId = validSaveId + 2;
        int unrelatedNegativeRecipeId = -10_000 - index;
        string identity = $"extended-{definition.AssetKey}";

        CheckEqual(
            true,
            NeonLetterColorInteractionPolicy.IsEditable(
                hasCompletedStructure: true,
                definition.RecipeId),
            $"a completed {symbol} structure exposes the color editor");
        CheckEqual(
            false,
            NeonLetterColorInteractionPolicy.IsEditable(
                hasCompletedStructure: true,
                definition.CraftingNodeId),
            $"{symbol}'s adjacent crafting-node ID cannot expose the color editor");
        CheckEqual(
            false,
            NeonLetterColorInteractionPolicy.IsEditable(
                hasCompletedStructure: true,
                unrelatedNegativeRecipeId),
            $"an unrelated negative ID cannot expose the {symbol} color editor");

        CheckEqual(
            true,
            authoritativeColors.TryAccept(
                isHost: true,
                identity,
                isLive: true,
                definition.RecipeId,
                color).Accepted,
            $"the host accepts a live {symbol} color request");
        CheckEqual(
            false,
            authoritativeColors.TryAccept(
                isHost: false,
                identity: $"{identity}-client",
                isLive: true,
                definition.RecipeId,
                color).Accepted,
            $"a client cannot accept authoritative {symbol} color state");
        CheckEqual(
            false,
            authoritativeColors.TryAccept(
                isHost: true,
                identity: $"{identity}-dead",
                isLive: false,
                definition.RecipeId,
                color).Accepted,
            $"the host rejects a {symbol} color request for a non-live identity");
        CheckEqual(
            false,
            authoritativeColors.TryAccept(
                isHost: true,
                identity: $"{identity}-crafting",
                isLive: true,
                definition.CraftingNodeId,
                color).Accepted,
            $"the host rejects {symbol}'s adjacent crafting-node ID");
        CheckEqual(
            false,
            authoritativeColors.TryAccept(
                isHost: true,
                identity: $"{identity}-negative",
                isLive: true,
                unrelatedNegativeRecipeId,
                color).Accepted,
            $"the host rejects an unrelated negative ID near {symbol}");

        NeonLetterColorStore.Upsert(
            colorEnvelope,
            new NeonLetterColorSaveEntry(
                validSaveId,
                definition.RecipeId,
                color));
        NeonLetterColorStore.Upsert(
            colorEnvelope,
            new NeonLetterColorSaveEntry(
                craftingNodeSaveId,
                definition.CraftingNodeId,
                color));
        NeonLetterColorStore.Upsert(
            colorEnvelope,
            new NeonLetterColorSaveEntry(
                unrelatedSaveId,
                unrelatedNegativeRecipeId,
                color));

        validSaveIds[symbol] = validSaveId;
        restoreTargets[validSaveId] =
            new FakeColorRestoreTarget(definition.RecipeId);
        restoreTargets[craftingNodeSaveId] =
            new FakeColorRestoreTarget(definition.CraftingNodeId);
        restoreTargets[unrelatedSaveId] =
            new FakeColorRestoreTarget(unrelatedNegativeRecipeId);
        invalidSaveIds.Add(craftingNodeSaveId);
        invalidSaveIds.Add(unrelatedSaveId);

        uint packedColor = NeonLetterNetworkProtocol.Pack(color);
        multiplayerEnvelope.Entries.Add(
            new NeonLetterMultiplayerSaveEntry
            {
                RecipeId = definition.RecipeId,
                NativeSaveId = validSaveId,
                Position = new NeonVector3(index, index + 1f, index + 2f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = packedColor
            });
        multiplayerEnvelope.Entries.Add(
            new NeonLetterMultiplayerSaveEntry
            {
                RecipeId = definition.CraftingNodeId,
                NativeSaveId = craftingNodeSaveId,
                Position = new NeonVector3(index, index + 1f, index + 2f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = packedColor
            });
        multiplayerEnvelope.Entries.Add(
            new NeonLetterMultiplayerSaveEntry
            {
                RecipeId = unrelatedNegativeRecipeId,
                NativeSaveId = unrelatedSaveId,
                Position = new NeonVector3(index, index + 1f, index + 2f),
                Rotation = new NeonQuaternion(0f, 0f, 0f, 1f),
                PackedColor = packedColor
            });
    }

    string colorJson = LoaderUtils.JsonSerialize(colorEnvelope);
    NeonLetterColorSaveEnvelope? restoredColorEnvelope =
        LoaderUtils.JsonDeserialize<NeonLetterColorSaveEnvelope>(colorJson);
    CheckEqual(
        false,
        restoredColorEnvelope == null,
        "extended Single Player colors round-trip through the save envelope");
    if (restoredColorEnvelope != null)
    {
        int restoredCount = NeonLetterColorRestoreCoordinator.Restore(
            restoredColorEnvelope,
            saveId => restoreTargets.TryGetValue(
                saveId,
                out FakeColorRestoreTarget? target)
                ? target
                : null);
        CheckEqual(
            representativeSymbols.Length,
            restoredCount,
            "Single Player restores each representative extension color exactly once");

        foreach ((char symbol, NeonRgba color) in representativeSymbols)
        {
            FakeColorRestoreTarget target = restoreTargets[validSaveIds[symbol]];
            CheckEqual(
                1,
                target.AppliedColors.Count,
                $"Single Player restores the persisted {symbol} color");
            CheckEqual(
                color,
                target.AppliedColors[0],
                $"Single Player preserves the exact {symbol} RGBA");
        }

        foreach (int invalidSaveId in invalidSaveIds)
        {
            CheckEqual(
                0,
                restoreTargets[invalidSaveId].AppliedColors.Count,
                "Single Player ignores crafting-node and unrelated negative recipe IDs");
        }
    }

    NeonLetterMultiplayerSaveEnvelope hostPayload =
        NeonLetterMultiplayerPersistencePolicy.CreateWorldPayload(
            isMultiplayer: true,
            isHost: true,
            multiplayerEnvelope);
    CheckEqual(
        representativeSymbols.Length,
        hostPayload.Entries.Count,
        "the host world payload retains only representative extension recipes");

    var expectedWorldColors = representativeSymbols.ToDictionary(
        entry => NeonLetterSmallCatalog.Get(entry.Symbol).RecipeId,
        entry => NeonLetterNetworkProtocol.Pack(entry.Color));
    foreach ((int recipeId, uint packedColor) in expectedWorldColors)
    {
        NeonLetterMultiplayerSaveEntry? hostEntry = hostPayload.Entries
            .SingleOrDefault(entry => entry.RecipeId == recipeId);
        CheckEqual(
            false,
            hostEntry == null,
            "the host world payload retains each representative extension recipe");
        if (hostEntry != null)
        {
            CheckEqual(
                packedColor,
                hostEntry.PackedColor,
                "the host world payload retains each representative extension color");
        }
    }

    string worldJson = LoaderUtils.JsonSerialize(hostPayload);
    NeonLetterMultiplayerSaveEnvelope? restoredWorldEnvelope =
        LoaderUtils.JsonDeserialize<NeonLetterMultiplayerSaveEnvelope>(worldJson);
    NeonLetterMultiplayerSaveEnvelope acceptedWorldPayload =
        NeonLetterMultiplayerPersistencePolicy.AcceptLoadedWorldPayload(
            isMultiplayer: true,
            isHost: true,
            restoredWorldEnvelope);
    CheckEqual(
        representativeSymbols.Length,
        acceptedWorldPayload.Entries.Count,
        "serialized multiplayer world state restores every representative extension recipe");
    foreach ((int recipeId, uint packedColor) in expectedWorldColors)
    {
        NeonLetterMultiplayerSaveEntry? restoredEntry = acceptedWorldPayload.Entries
            .SingleOrDefault(entry => entry.RecipeId == recipeId);
        CheckEqual(
            false,
            restoredEntry == null,
            "serialized multiplayer world state retains each representative extension recipe");
        if (restoredEntry != null)
        {
            CheckEqual(
                packedColor,
                restoredEntry.PackedColor,
                "serialized multiplayer world state preserves each representative extension color");
        }
    }

    NeonLetterMultiplayerSaveEnvelope clientPayload =
        NeonLetterMultiplayerPersistencePolicy.CreateWorldPayload(
            isMultiplayer: true,
            isHost: false,
            multiplayerEnvelope);
    CheckEqual(
        0,
        clientPayload.Entries.Count,
        "a multiplayer client cannot serialize representative extension world state");
}

void CheckColorRestoreContract()
{
    int recipeA = NeonLetterSmallCatalog.Get('A').RecipeId;
    int recipeB = NeonLetterSmallCatalog.Get('B').RecipeId;
    var cyan = NeonRgba.ProjectCyan;
    var red = new NeonRgba(1f, 0f, 0f, 1f);
    var green = new NeonRgba(0f, 1f, 0f, 1f);
    var blue = new NeonRgba(0f, 0f, 1f, 1f);

    var removable = new NeonLetterColorSaveEnvelope
    {
        Entries = new List<NeonLetterColorSaveEntry>
        {
            null!,
            new(11, recipeA, red),
            new(12, recipeB, green)
        }
    };
    NeonLetterColorStore.Remove(removable, 11);
    CheckEqual(
        1,
        removable.Entries.Count,
        "removing a destroyed letter drops its persisted SaveId and malformed null entries");
    CheckEqual(
        12,
        removable.Entries[0].SaveId,
        "removing one persisted letter leaves other SaveIds intact");

    var loadSource = new NeonLetterColorSaveEnvelope
    {
        Entries = new List<NeonLetterColorSaveEntry>
        {
            new(21, recipeA, red)
        }
    };
    var saveState = new NeonLetterColorSaveState();
    saveState.Load(loadSource);
    loadSource.Entries[0].Color = green;
    CheckEqual<NeonRgba?>(
        red,
        saveState.Resolve(21, recipeA),
        "loading color state takes an isolated snapshot of the deserialized envelope");

    NeonLetterColorSaveEnvelope saveSnapshot = saveState.Save();
    saveSnapshot.Entries[0].Color = blue;
    CheckEqual<NeonRgba?>(
        red,
        saveState.Resolve(21, recipeA),
        "serializing color state cannot mutate the live in-memory envelope");
    string roundTripJson = JsonSerializer.Serialize(saveState.Save());
    NeonLetterColorSaveEnvelope roundTrip =
        JsonSerializer.Deserialize<NeonLetterColorSaveEnvelope>(roundTripJson)!;
    CheckEqual<NeonRgba?>(
        red,
        NeonLetterColorStore.Resolve(roundTrip, 21, recipeA),
        "the isolated save-state snapshot round-trips SaveId, recipe and RGBA");

    saveState.Load(new NeonLetterColorSaveEnvelope
    {
        Version = NeonLetterColorSaveEnvelope.CurrentVersion + 1,
        Entries = new List<NeonLetterColorSaveEntry>
        {
            new(22, recipeA, green)
        }
    });
    CheckEqual<NeonRgba?>(
        null,
        saveState.Resolve(22, recipeA),
        "loading an unsupported envelope version resets in-memory persistence safely");

    saveState.Load(null!);
    CheckEqual(
        0,
        saveState.Save().Entries.Count,
        "loading a null envelope resets in-memory persistence safely");

    saveState.Load(new NeonLetterColorSaveEnvelope
    {
        Entries = new List<NeonLetterColorSaveEntry>
        {
            new(24, recipeA, new NeonRgba(float.NaN, 0f, 0f, 1f)),
            new(25, recipeB, new NeonRgba(0f, float.PositiveInfinity, 0f, 1f)),
            new(26, recipeA, red)
        }
    });
    CheckEqual(
        1,
        saveState.Save().Entries.Count,
        "loading skips NaN and Infinity RGBA entries without dropping later valid colors");
    CheckEqual<NeonRgba?>(
        red,
        saveState.Resolve(26, recipeA),
        "a valid color after malformed non-finite RGBA is retained");
    saveState.Upsert(new NeonLetterColorSaveEntry(23, recipeB, blue));
    saveState.Clear();
    CheckEqual<NeonRgba?>(
        null,
        saveState.Resolve(23, recipeB),
        "leaving the world clears the in-memory persistence envelope");

    var failingTarget = new FakeColorRestoreTarget(recipeA, throwOnApply: true);
    var validTarget = new FakeColorRestoreTarget(recipeB);
    var mismatchedTarget = new FakeColorRestoreTarget(recipeB);
    var restoreEnvelope = new NeonLetterColorSaveEnvelope
    {
        Entries = new List<NeonLetterColorSaveEntry>
        {
            null!,
            new(30, recipeA, red),
            new(31, recipeA, green),
            new(32, int.MinValue, blue),
            new(33, recipeA, cyan),
            new(34, recipeB, blue)
        }
    };
    var targets = new Dictionary<int, INeonLetterColorRestoreTarget>
    {
        [30] = failingTarget,
        [31] = mismatchedTarget,
        [34] = validTarget
    };
    var restoreErrors = new List<Exception>();
    int restoredCount = NeonLetterColorRestoreCoordinator.Restore(
        restoreEnvelope,
        saveId => targets.TryGetValue(saveId, out INeonLetterColorRestoreTarget? target)
            ? target
            : null,
        restoreErrors.Add);

    CheckEqual(
        1,
        restoredCount,
        "restore applies only the matching resolvable A-Z SaveId and recipe entry");
    CheckEqual(
        1,
        restoreErrors.Count,
        "one restore target failure is reported without aborting later entries");
    CheckEqual(
        0,
        mismatchedTarget.AppliedColors.Count,
        "a reused SaveId with a mismatched recipe is ignored");
    CheckSequence(
        new[] { blue },
        validTarget.AppliedColors,
        "a valid entry after a failed stale entry is still restored");

    var commitOrder = new List<string>();
    NeonLetterColorCommitCoordinator.Commit(
        green,
        _ => commitOrder.Add("emission"),
        _ => commitOrder.Add("persistence"));
    CheckSequence(
        new[] { "emission", "persistence" },
        commitOrder,
        "Apply persists only after emission succeeds");

    int persistedAfterFailure = 0;
    CheckThrows<InvalidOperationException>(
        () => NeonLetterColorCommitCoordinator.Commit(
            red,
            _ => throw new InvalidOperationException("emission failed"),
            _ => persistedAfterFailure++),
        "emission failed",
        "a failed emission aborts the Apply commit");
    CheckEqual(
        0,
        persistedAfterFailure,
        "a failed emission never writes persistent color state");
}

void CheckEmissionApplicationContract()
{
    NeonLetterSmallDefinition definition = NeonLetterSmallCatalog.Get('A');
    var wireRenderer = new FakeEmissionRenderer(
        "WireRenderer",
        new FakeEmissionMaterial(8f));
    var otherLetterRenderer = new FakeEmissionRenderer(
        "LetterBRenderer",
        new FakeEmissionMaterial(12f));
    var firstLetterRenderer = new FakeEmissionRenderer(
        "LetterAFirstRenderer",
        new FakeEmissionMaterial(2f),
        new FakeEmissionMaterial(4f));
    var secondLetterRenderer = new FakeEmissionRenderer(
        "LetterASecondRenderer",
        new FakeEmissionMaterial(6f));
    firstLetterRenderer.Blocks[0].FloatProperties["_ExistingFloat"] = 17f;
    firstLetterRenderer.Blocks[1].FloatProperties["_ExistingFloat"] = 23f;
    secondLetterRenderer.Blocks[0].FloatProperties["_ExistingFloat"] = 31f;

    var wire = new FakeEmissionSubtree(
        NeonLetterSmallCatalog.WireIngredientName,
        wireRenderer);
    var otherLetter = new FakeEmissionSubtree(
        NeonLetterSmallCatalog.Get('B').ColliderVisualChildName,
        otherLetterRenderer);
    var selectedLetter = new FakeEmissionSubtree(
        definition.ColliderVisualChildName,
        firstLetterRenderer,
        secondLetterRenderer);
    var selectedColor = new NeonRgba(0.5f, 0.25f, 1f, 0.75f);

    NeonLetterEmissionPolicy.Apply(
        definition,
        new IEmissionVisualSubtree[] { wire, otherLetter, selectedLetter },
        selectedColor);

    CheckEqual(0, wireRenderer.Writes.Count, "changing A leaves the wire subtree unchanged");
    CheckEqual(0, otherLetterRenderer.Writes.Count, "changing A leaves another letter subtree unchanged");
    CheckEqual(2, firstLetterRenderer.Writes.Count, "every material slot on the first selected renderer is updated");
    CheckEqual(1, secondLetterRenderer.Writes.Count, "every renderer in the selected subtree is updated");
    CheckSequence(
        new[] { 0, 1 },
        firstLetterRenderer.ReadSlots,
        "existing property blocks are read from every material slot");
    CheckSequence(
        new[] { 0, 1 },
        firstLetterRenderer.Writes.Select(write => write.SlotIndex),
        "property blocks are returned to their source material slots");
    CheckEqual(
        true,
        ReferenceEquals(firstLetterRenderer.Blocks[0], firstLetterRenderer.Writes[0].Block),
        "the existing property block is supplemented instead of replaced");
    CheckEqual(
        17f,
        firstLetterRenderer.Blocks[0].FloatProperties["_ExistingFloat"],
        "changing emission preserves unrelated property-block values");
    CheckSequence(
        new[] { NeonLetterEmissionPolicy.EmissiveColorPropertyName },
        firstLetterRenderer.Blocks[0].ColorProperties.Keys,
        "the policy writes only the runtime HDRP emissive color property");

    NeonRgba firstSlotColor = firstLetterRenderer.Blocks[0]
        .ColorProperties[NeonLetterEmissionPolicy.EmissiveColorPropertyName];
    CheckNear(
        0.21404114f * 2f,
        firstSlotColor.Red,
        "selected red is converted from sRGB to linear and scaled by slot intensity");
    CheckNear(
        0.05087609f * 2f,
        firstSlotColor.Green,
        "selected green is converted from sRGB to linear and scaled by slot intensity");
    CheckNear(
        2f,
        firstSlotColor.Blue,
        "selected blue is converted from sRGB to linear and scaled by slot intensity");
    CheckNear(
        selectedColor.Alpha,
        firstSlotColor.Alpha,
        "selected alpha is preserved without emissive-intensity scaling");
    NeonRgba secondSlotColor = firstLetterRenderer.Blocks[1]
        .ColorProperties[NeonLetterEmissionPolicy.EmissiveColorPropertyName];
    CheckNear(
        firstSlotColor.Red * 2f,
        secondSlotColor.Red,
        "each material slot uses its own source emissive intensity");

    foreach (FakeEmissionMaterial material in firstLetterRenderer.Materials)
    {
        CheckEqual(1, material.IntensityReadCount, "source material intensity is read exactly once per slot");
        CheckEqual(false, material.WasMutated, "shared source materials are never mutated");
    }

    var partiallyInvalidRenderer = new FakeEmissionRenderer(
        "PartiallyInvalidLetterRenderer",
        new FakeEmissionMaterial(2f),
        new FakeEmissionMaterial(float.NaN));
    CheckThrows<InvalidOperationException>(
        () => NeonLetterEmissionPolicy.Apply(
            definition,
            new IEmissionVisualSubtree[]
            {
                new FakeEmissionSubtree(
                    definition.ColliderVisualChildName,
                    partiallyInvalidRenderer)
            },
            selectedColor),
        "finite positive",
        "an invalid later material slot rejects the complete emission update");
    CheckEqual(
        0,
        partiallyInvalidRenderer.Writes.Count,
        "an invalid later material slot leaves every property block unwritten");

    CheckThrows<InvalidOperationException>(
        () => NeonLetterEmissionPolicy.Apply(
            definition,
            new IEmissionVisualSubtree[]
            {
                new FakeEmissionSubtree(definition.ColliderVisualChildName)
            },
            selectedColor),
        "has no renderers",
        "a selected visual subtree without renderers fails visibly");
    CheckThrows<InvalidOperationException>(
        () => NeonLetterEmissionPolicy.Apply(
            definition,
            new IEmissionVisualSubtree[]
            {
                new FakeEmissionSubtree(
                    definition.ColliderVisualChildName,
                    new FakeEmissionRenderer(
                        "InvisibleLetterRenderer",
                        new FakeEmissionMaterial(0f)))
            },
            selectedColor),
        "non-positive emissive intensity",
        "a material slot with non-positive emissive intensity fails visibly");
}

void CheckColorInteractionContract()
{
    int knownRecipeId = NeonLetterSmallCatalog.Get('A').RecipeId;

    CheckEqual(
        true,
        NeonLetterColorInteractionPolicy.CanOpenEditor(true, true),
        "the Use action opens the editor for a focused letter while the player is controllable");
    CheckEqual(
        false,
        NeonLetterColorInteractionPolicy.CanOpenEditor(false, true),
        "selecting a blueprint cannot open the editor for a previously focused letter");
    CheckEqual(
        false,
        NeonLetterColorInteractionPolicy.CanOpenEditor(true, false),
        "the Use action does nothing when no neon letter is focused");

    CheckEqual(
        true,
        NeonLetterColorInteractionPolicy.IsEditable(true, knownRecipeId),
        "a completed A-Z structure is editable in Single Player or multiplayer");
    CheckEqual(
        false,
        NeonLetterColorInteractionPolicy.IsEditable(false, knownRecipeId),
        "a crafting preview cannot open the color editor");
    CheckEqual(
        false,
        NeonLetterColorInteractionPolicy.IsEditable(true, int.MinValue),
        "a completed structure with an unknown recipe cannot open the color editor");

    CheckEqual(
        NeonLetterColorCommitRoute.SinglePlayer,
        NeonLetterColorCommitRoutingPolicy.Resolve(
            targetMode: NeonLetterColorTargetMode.SinglePlayer,
            isServer: false,
            isClient: false),
        "Single Player routes color commits to local state");
    CheckEqual(
        NeonLetterColorCommitRoute.MultiplayerHost,
        NeonLetterColorCommitRoutingPolicy.Resolve(
            targetMode: NeonLetterColorTargetMode.Multiplayer,
            isServer: true,
            isClient: true),
        "a multiplayer host takes precedence over the client role");
    CheckEqual(
        NeonLetterColorCommitRoute.MultiplayerClient,
        NeonLetterColorCommitRoutingPolicy.Resolve(
            targetMode: NeonLetterColorTargetMode.Multiplayer,
            isServer: false,
            isClient: true),
        "a multiplayer client routes color commits to the host");
    CheckEqual(
        NeonLetterColorCommitRoute.Unavailable,
        NeonLetterColorCommitRoutingPolicy.Resolve(
            targetMode: NeonLetterColorTargetMode.Multiplayer,
            isServer: false,
            isClient: false),
        "multiplayer without a usable network role cannot commit colors");

    var routedColor = new NeonRgba(0.2f, 0.4f, 0.6f, 1f);
    int localCommitCount = 0;
    int networkRequestCount = 0;
    NeonLetterColorRoutedCommit singlePlayerCommit =
        NeonLetterColorCommitRoutingCoordinator.TryCommit(
            targetMode: NeonLetterColorTargetMode.SinglePlayer,
            isServer: false,
            isClient: false,
            routedColor,
            _ => localCommitCount++,
            _ =>
            {
                networkRequestCount++;
                return true;
            });
    CheckEqual(true, singlePlayerCommit.Succeeded, "Single Player color commit succeeds locally");
    CheckEqual(
        NeonLetterColorCommitRoute.SinglePlayer,
        singlePlayerCommit.Route,
        "Single Player color commit reports its local route");
    CheckEqual(1, localCommitCount, "Single Player invokes the local commit exactly once");
    CheckEqual(0, networkRequestCount, "Single Player never sends a network color request");

    localCommitCount = 0;
    networkRequestCount = 0;
    NeonLetterColorRoutedCommit hostCommit =
        NeonLetterColorCommitRoutingCoordinator.TryCommit(
            targetMode: NeonLetterColorTargetMode.Multiplayer,
            isServer: true,
            isClient: true,
            routedColor,
            _ => localCommitCount++,
            _ =>
            {
                networkRequestCount++;
                return true;
            });
    CheckEqual(true, hostCommit.Succeeded, "a host color request succeeds through transport");
    CheckEqual(
        NeonLetterColorCommitRoute.MultiplayerHost,
        hostCommit.Route,
        "a host color request reports the host route");
    CheckEqual(0, localCommitCount, "a host never writes Single Player color state");
    CheckEqual(1, networkRequestCount, "a host invokes the network request exactly once");

    localCommitCount = 0;
    networkRequestCount = 0;
    NeonLetterColorRoutedCommit clientCommit =
        NeonLetterColorCommitRoutingCoordinator.TryCommit(
            targetMode: NeonLetterColorTargetMode.Multiplayer,
            isServer: false,
            isClient: true,
            routedColor,
            _ => localCommitCount++,
            _ =>
            {
                networkRequestCount++;
                return true;
            });
    CheckEqual(true, clientCommit.Succeeded, "a client color request succeeds through transport");
    CheckEqual(
        NeonLetterColorCommitRoute.MultiplayerClient,
        clientCommit.Route,
        "a client color request reports the client route");
    CheckEqual(0, localCommitCount, "a client never writes Single Player color state");
    CheckEqual(1, networkRequestCount, "a client invokes the network request exactly once");

    localCommitCount = 0;
    networkRequestCount = 0;
    NeonLetterColorRoutedCommit unavailableNetworkCommit =
        NeonLetterColorCommitRoutingCoordinator.TryCommit(
            targetMode: NeonLetterColorTargetMode.Multiplayer,
            isServer: false,
            isClient: true,
            routedColor,
            _ => localCommitCount++,
            _ =>
            {
                networkRequestCount++;
                return false;
            });
    CheckEqual(
        false,
        unavailableNetworkCommit.Succeeded,
        "an immutable multiplayer target rejects an unavailable network request");
    CheckEqual(
        NeonLetterColorCommitRoute.MultiplayerClient,
        unavailableNetworkCommit.Route,
        "an unavailable request preserves the multiplayer target route");
    CheckEqual(
        0,
        localCommitCount,
        "an unavailable multiplayer target never falls through to Single Player state");
    CheckEqual(
        1,
        networkRequestCount,
        "an unavailable multiplayer target attempts only its network request");

    localCommitCount = 0;
    networkRequestCount = 0;
    NeonLetterColorRoutedCommit unavailableCommit =
        NeonLetterColorCommitRoutingCoordinator.TryCommit(
            targetMode: NeonLetterColorTargetMode.Multiplayer,
            isServer: false,
            isClient: false,
            routedColor,
            _ => localCommitCount++,
            _ =>
            {
                networkRequestCount++;
                return true;
            });
    CheckEqual(false, unavailableCommit.Succeeded, "an unavailable role rejects color commits");
    CheckEqual(
        NeonLetterColorCommitRoute.Unavailable,
        unavailableCommit.Route,
        "an unavailable color commit reports its rejected route");
    CheckEqual(0, localCommitCount, "an unavailable role never writes local color state");
    CheckEqual(0, networkRequestCount, "an unavailable role never sends a network request");

    CheckEqual(
        true,
        NeonLetterColorPersistenceEligibility.CanPersist(true, true),
        "a SaveId owned by the current tracked structure can persist a color");
    CheckEqual(
        false,
        NeonLetterColorPersistenceEligibility.CanPersist(false, true),
        "a structure without a SaveId interface cannot persist a color");
    CheckEqual(
        false,
        NeonLetterColorPersistenceEligibility.CanPersist(true, false),
        "an unassigned or reused SaveId cannot persist a color");

    var focusedLetter = new object();
    var unrelatedLetter = new object();
    var focus = new NeonLetterColorFocus<object>();
    focus.Enter(focusedLetter);
    focus.Exit(unrelatedLetter);

    CheckEqual(
        true,
        ReferenceEquals(focusedLetter, focus.Current),
        "GrabExit from another interaction does not clear the focused letter");

    focus.Exit(focusedLetter);
    CheckEqual<object?>(
        null,
        focus.Current,
        "GrabExit from the focused interaction clears the focused letter");

    focus.Enter(focusedLetter);
    focus.Clear();
    CheckEqual<object?>(
        null,
        focus.Current,
        "leaving the world clears the focused letter without requiring a live target");

    CheckEqual(
        "#1A80FF",
        NeonLetterColorFormatting.ToHex(new NeonRgba(0.10f, 0.50f, 1f, 0.25f)),
        "the selected color is displayed as an uppercase RGB hex value");

    var originalColor = new NeonRgba(0.1f, 0.2f, 0.3f, 1f);
    var editorSession = new NeonLetterColorEditorSession<object>();
    editorSession.Open(focusedLetter, originalColor);

    NeonLetterColorTargetLoss unrelatedLoss =
        editorSession.LoseTarget(unrelatedLetter);
    CheckEqual(
        false,
        unrelatedLoss.ShouldRollback,
        "losing another letter leaves the active color editor open");
    CheckEqual(
        true,
        ReferenceEquals(focusedLetter, editorSession.Target),
        "losing another letter preserves the active editor target");

    NeonLetterColorTargetLoss activeLoss =
        editorSession.LoseTarget(focusedLetter);
    CheckEqual(
        true,
        activeLoss.ShouldRollback,
        "losing the edited letter requests a live-preview rollback");
    CheckEqual(
        originalColor,
        activeLoss.RollbackColor,
        "losing the edited letter restores the color from before editing");
    CheckEqual<object?>(
        null,
        editorSession.Target,
        "losing the edited letter clears the editor target");
    CheckEqual<NeonLetterColorEditor?>(
        null,
        editorSession.Editor,
        "losing the edited letter clears the editor state");

    editorSession.Open(focusedLetter, originalColor);
    NeonLetterColorTargetLoss worldExit = editorSession.ExitWorld();
    CheckEqual(
        true,
        worldExit.ShouldRollback,
        "leaving the world requests rollback for an open color editor");
    CheckEqual<object?>(
        null,
        editorSession.Target,
        "leaving the world clears the editor target");

    var sessionColors = new NeonLetterSessionColors<int>();
    var temporaryColor = new NeonRgba(1f, 0f, 0f, 1f);
    sessionColors.Commit(42, temporaryColor);
    sessionColors.Remove(42);
    CheckEqual(
        NeonRgba.ProjectCyan,
        sessionColors.Resolve(42),
        "destroying a letter removes its session color before an instance ID can be reused");

    sessionColors.Commit(42, temporaryColor);
    sessionColors.Commit(84, new NeonRgba(0f, 1f, 0f, 1f));
    sessionColors.Clear();
    CheckEqual(
        NeonRgba.ProjectCyan,
        sessionColors.Resolve(42),
        "leaving the world clears every remembered session color");
    CheckEqual(
        NeonRgba.ProjectCyan,
        sessionColors.Resolve(84),
        "leaving the world prevents another remembered color from leaking into the next world");
}

void CheckColorRuntimeSafetyContract()
{
    string runtimePath = FindRepositoryFile("NeonLetterColorRuntime.cs")
        ?? throw new InvalidOperationException("Could not find the color runtime adapter.");
    string source = File.ReadAllText(runtimePath);

    CheckEqual(
        false,
        source.Contains("PersistentColors.Remove(saveId.Value)", StringComparison.Ordinal),
        "ordinary Unity OnDestroy never deletes persistent color state");
    CheckEqual(
        true,
        source.Contains("NetUtils.IsMultiplayer", StringComparison.Ordinal),
        "the runtime checks SonsSdk multiplayer state before editing a letter");
    CheckEqual(
        true,
        source.Contains("ScrewStructureManager.TryGetStructureBySaveID", StringComparison.Ordinal),
        "the runtime validates SaveId ownership through the structure manager");
    CheckEqual(
        true,
        source.Contains("GameState.IsPlayerControllable", StringComparison.Ordinal),
        "the global Use handler is gated while the blueprint book or console owns input");
    CheckEqual(
        false,
        source.Contains("GlobalInput.OnUsePerformed.Subscribe", StringComparison.Ordinal),
        "color editing does not depend on the unreliable mapped Use action");
    CheckEqual(
        true,
        source.Contains("GlobalInput.RegisterKey(KeyCode.E", StringComparison.Ordinal),
        "color editing registers the requested E key through the SonsSdk key API");
    CheckEqual(
        false,
        source.Contains("AttachToBuiltPrefab", StringComparison.Ordinal),
        "color interaction is never added to an already registered built prefab");
    CheckEqual(
        false,
        source.Contains("RegisterOnStructureComplete", StringComparison.Ordinal),
        "color editing never hooks the native structure-completion callback");
    CheckEqual(
        false,
        source.Contains("SonsInteractionTools.CreateInteraction", StringComparison.Ordinal),
        "color editing never injects interaction objects into registered or built structures");
    CheckEqual(
        false,
        source.Contains("ClassInjector", StringComparison.Ordinal),
        "color editing does not inject a custom MonoBehaviour into IL2CPP");
    CheckEqual(
        true,
        source.Contains("Physics.Raycast", StringComparison.Ordinal),
        "the Use action selects a completed letter through the player's view ray");
    CheckEqual(
        true,
        source.Contains("TryResolveTargetFromView", StringComparison.Ordinal),
        "the raycast result is validated as a known completed neon recipe before editing");
    CheckEqual(
        true,
        source.Contains("GetComponentInParent<ScrewStructure>()", StringComparison.Ordinal),
        "the raycast resolves the completed ScrewStructure from the letter collider");
    CheckEqual(
        true,
        source.Contains("SOTFNeonLettersUi.Open(target)", StringComparison.Ordinal),
        "a valid E-key target opens the color editor");
    CheckEqual(
        true,
        source.Contains(
            "NeonLetterRestoreReadinessScheduler",
            StringComparison.Ordinal),
        "single-player restore uses readiness tokens and bounded safety probes");
    CheckEqual(
        true,
        source.Contains("HasWorkForToken", StringComparison.Ordinal),
        "single-player idle updates skip parked restore entries");

    string multiplayerSaveRuntimePath =
        FindRepositoryFile("NeonLetterMultiplayerSaveRuntime.cs")
        ?? throw new InvalidOperationException(
            "Could not find the multiplayer save runtime adapter.");
    string multiplayerSaveRuntimeSource =
        File.ReadAllText(multiplayerSaveRuntimePath);
    CheckEqual(
        true,
        multiplayerSaveRuntimeSource.Contains(
            "NeonLetterRestoreReadinessScheduler",
            StringComparison.Ordinal),
        "multiplayer restore uses readiness tokens and bounded safety probes");
    CheckEqual(
        true,
        multiplayerSaveRuntimeSource.Contains(
            "HasWorkForToken",
            StringComparison.Ordinal),
        "multiplayer idle updates skip parked restore entries");
    CheckEqual(
        true,
        multiplayerSaveRuntimeSource.Contains(
            "readinessToken:",
            StringComparison.Ordinal),
        "multiplayer runtime advances token-scoped restore waves");

    string uiPath = FindRepositoryFile("SOTFNeonLettersUi.cs")
        ?? throw new InvalidOperationException("Could not find the color editor UI adapter.");
    string uiSource = File.ReadAllText(uiPath);
    CheckEqual(
        true,
        uiSource.Contains("TogglePanel(PanelId, show: true)", StringComparison.Ordinal),
        "opening the color editor makes its registered SUI panel visible");
    CheckEqual(
        true,
        uiSource.Contains("EditorSession.Target.PreviewColor(selectedColor)", StringComparison.Ordinal),
        "moving the color picker previews the chosen color on the targeted letter");
}

void CheckColorPickerLayoutContract()
{
    string uiPath = FindRepositoryFile("SOTFNeonLettersUi.cs")
        ?? throw new InvalidOperationException("Could not find the color editor UI adapter.");
    string source = File.ReadAllText(uiPath);

    CheckEqual(
        true,
        source.Contains(".PWidth(360f)", StringComparison.Ordinal),
        "the color picker reserves its full preferred width in the SUI layout");
    CheckEqual(
        true,
        source.Contains(".PHeight(360f)", StringComparison.Ordinal),
        "the color picker reserves its full preferred height in the SUI layout");
    CheckEqual(
        true,
        source.Contains(".MWidth(360f)", StringComparison.Ordinal),
        "the color picker cannot collapse below its usable width");
    CheckEqual(
        true,
        source.Contains(".MHeight(360f)", StringComparison.Ordinal),
        "the color picker cannot collapse below its usable height");
}

void CheckCatalogAgainstModelManifest(IReadOnlyList<NeonLetterSmallDefinition> definitions)
{
    string manifestPath = FindRepositoryFile(
        "assets/processed/neon-letters/model/model-manifest.json")
        ?? throw new InvalidOperationException("Could not find the canonical model manifest.");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
    JsonElement objects = document.RootElement.GetProperty("objects");
    CheckEqual(26, objects.GetArrayLength(), "model manifest contains 26 source letters");

    var manifestNodes = new Dictionary<char, string>();
    var geometryIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (JsonElement item in objects.EnumerateArray())
    {
        char letter = item.GetProperty("letter").GetString()![0];
        string nodeId = item.GetProperty("nodeId").GetString()!;
        string geometryId = item.GetProperty("geometryId").GetString()!;
        manifestNodes.Add(letter, nodeId);
        geometryIds.Add(geometryId);
    }

    CheckEqual(26, geometryIds.Count, "every source letter uses an independent geometry");
    foreach (NeonLetterSmallDefinition definition in definitions)
    {
        CheckEqual(
            manifestNodes[definition.Letter],
            definition.SourceNodeName,
            $"{definition.Letter} catalog node matches the canonical model manifest");
    }
}

string? FindRepositoryFile(string relativePath)
{
    DirectoryInfo? directory = new(Environment.CurrentDirectory);
    while (directory != null)
    {
        string candidate = Path.Combine(directory.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}

int CountOccurrences(string source, string value)
{
    int count = 0;
    int offset = 0;
    while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }

    return count;
}

void CheckEqual<T>(T expected, T actual, string testName)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        failures.Add($"{testName}: expected '{expected}', got '{actual}'.");
    }
}

void CheckNear(float expected, float actual, string testName)
{
    if (MathF.Abs(expected - actual) > 0.00001f)
    {
        failures.Add($"{testName}: expected '{expected}', got '{actual}'.");
    }
}

void CheckThrows<TException>(
    Action action,
    string expectedMessageFragment,
    string testName)
    where TException : Exception
{
    try
    {
        action();
        failures.Add($"{testName}: expected {typeof(TException).Name}, but no exception was thrown.");
    }
    catch (TException exception)
    {
        if (!exception.Message.Contains(expectedMessageFragment, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{testName}: expected message containing '{expectedMessageFragment}', " +
                $"got '{exception.Message}'.");
        }
    }
    catch (Exception exception)
    {
        failures.Add(
            $"{testName}: expected {typeof(TException).Name}, got {exception.GetType().Name}.");
    }
}

void CheckSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string testName)
{
    T[] expectedItems = expected.ToArray();
    T[] actualItems = actual.ToArray();
    if (!expectedItems.SequenceEqual(actualItems))
    {
        failures.Add(
            $"{testName}: expected [{string.Join(", ", expectedItems)}], " +
            $"got [{string.Join(", ", actualItems)}].");
    }
}

}

sealed class FakePlacementTarget : IRecipePlacementTarget
{
    public bool GroundPlacementChecksRemoved { get; private set; }
    public List<string> AppliedOperations { get; } = new();
    public bool ParentRecipeOverridesCleared =>
        _dynamicParentOverrideCleared &&
        _screwParentOverrideCleared &&
        _freeFormParentOverrideCleared;
    public NeonLetterASmallDefinition.PlacementDefinition Snapshot => new(
        Anchor,
        CastRadiusFormula,
        AlignToSurface,
        CanBeRotated,
        ForceUp,
        LockUpwardVector,
        InitialRotationX,
        InitialRotationY,
        InitialRotationZ,
        AllowsTreePlacement,
        AllowsNonTreePlacement,
        MinimumHeightAboveTree,
        MaximumHeightAboveTree,
        AllowDynamicObjectParenting,
        AllowScrewStructureParenting,
        AllowFreeFormStructureParenting,
        UseOverridePlacementSize,
        PlacementDepthSizeRatio);
    private NeonLetterASmallDefinition.PlacementAnchor _anchor;
    private NeonLetterASmallDefinition.PlacementCastRadiusFormula _castRadiusFormula;
    private bool _canBeRotated;
    private bool _forceUp;
    private bool _lockUpwardVector;
    private float _minimumHeightAboveTree;
    private float _maximumHeightAboveTree;
    private bool _allowDynamicObjectParenting;
    private bool _allowScrewStructureParenting;
    private bool _allowFreeFormStructureParenting;
    private bool _useOverridePlacementSize;
    private float _placementDepthSizeRatio;
    private bool _dynamicParentOverrideCleared;
    private bool _screwParentOverrideCleared;
    private bool _freeFormParentOverrideCleared;

    public NeonLetterASmallDefinition.PlacementAnchor Anchor
    {
        get => _anchor;
        set { _anchor = value; AppliedOperations.Add(nameof(Anchor)); }
    }
    public NeonLetterASmallDefinition.PlacementCastRadiusFormula CastRadiusFormula
    {
        get => _castRadiusFormula;
        set { _castRadiusFormula = value; AppliedOperations.Add(nameof(CastRadiusFormula)); }
    }
    private bool _alignToSurface;
    public bool AlignToSurface
    {
        get => _alignToSurface;
        set { _alignToSurface = value; AppliedOperations.Add(nameof(AlignToSurface)); }
    }
    public bool CanBeRotated
    {
        get => _canBeRotated;
        set { _canBeRotated = value; AppliedOperations.Add(nameof(CanBeRotated)); }
    }
    public bool ForceUp
    {
        get => _forceUp;
        set { _forceUp = value; AppliedOperations.Add(nameof(ForceUp)); }
    }
    public bool LockUpwardVector
    {
        get => _lockUpwardVector;
        set { _lockUpwardVector = value; AppliedOperations.Add(nameof(LockUpwardVector)); }
    }
    public float InitialRotationX { get; set; }
    public float InitialRotationY { get; set; }
    public float InitialRotationZ { get; set; }
    private bool _allowsTreePlacement;
    public bool AllowsTreePlacement
    {
        get => _allowsTreePlacement;
        set { _allowsTreePlacement = value; AppliedOperations.Add(nameof(AllowsTreePlacement)); }
    }
    private bool _allowsNonTreePlacement;
    public bool AllowsNonTreePlacement
    {
        get => _allowsNonTreePlacement;
        set { _allowsNonTreePlacement = value; AppliedOperations.Add(nameof(AllowsNonTreePlacement)); }
    }
    public float MinimumHeightAboveTree
    {
        get => _minimumHeightAboveTree;
        set { _minimumHeightAboveTree = value; AppliedOperations.Add(nameof(MinimumHeightAboveTree)); }
    }
    public float MaximumHeightAboveTree
    {
        get => _maximumHeightAboveTree;
        set { _maximumHeightAboveTree = value; AppliedOperations.Add(nameof(MaximumHeightAboveTree)); }
    }
    public bool AllowDynamicObjectParenting
    {
        get => _allowDynamicObjectParenting;
        set
        {
            _allowDynamicObjectParenting = value;
            _dynamicParentOverrideCleared = true;
            AppliedOperations.Add(nameof(AllowDynamicObjectParenting));
        }
    }
    public bool AllowScrewStructureParenting
    {
        get => _allowScrewStructureParenting;
        set
        {
            _allowScrewStructureParenting = value;
            _screwParentOverrideCleared = true;
            AppliedOperations.Add(nameof(AllowScrewStructureParenting));
        }
    }
    public bool AllowFreeFormStructureParenting
    {
        get => _allowFreeFormStructureParenting;
        set
        {
            _allowFreeFormStructureParenting = value;
            _freeFormParentOverrideCleared = true;
            AppliedOperations.Add(nameof(AllowFreeFormStructureParenting));
        }
    }
    public bool UseOverridePlacementSize
    {
        get => _useOverridePlacementSize;
        set { _useOverridePlacementSize = value; AppliedOperations.Add(nameof(UseOverridePlacementSize)); }
    }
    public float PlacementDepthSizeRatio
    {
        get => _placementDepthSizeRatio;
        set { _placementDepthSizeRatio = value; AppliedOperations.Add(nameof(PlacementDepthSizeRatio)); }
    }

    public void RemoveGroundPlacementChecks()
    {
        GroundPlacementChecksRemoved = true;
        AppliedOperations.Add(nameof(RemoveGroundPlacementChecks));
    }

    public void SetInitialRotation(float x, float y, float z)
    {
        InitialRotationX = x;
        InitialRotationY = y;
        InitialRotationZ = z;
        AppliedOperations.Add(nameof(SetInitialRotation));
    }
}

sealed class FakeRuntimeMaterialFactory : IRuntimeMaterialFactory
{
    public FakeRuntimeMaterialFactory(string shaderName, bool isShaderSupported)
    {
        ShaderName = shaderName;
        IsShaderSupported = isShaderSupported;
    }

    public string ShaderName { get; }
    public bool IsShaderSupported { get; }
    public List<FakeRuntimeMaterial> CreatedMaterials { get; } = new();

    public IRuntimeMaterialHandle Create()
    {
        var material = new FakeRuntimeMaterial(
            string.Empty,
            0,
            Array.Empty<string>(),
            new Dictionary<string, float>())
        {
            ShaderName = ShaderName
        };
        CreatedMaterials.Add(material);
        return material;
    }
}

sealed class FakeRuntimeRenderer : IRuntimeRendererHandle
{
    public FakeRuntimeRenderer(string name, params IRuntimeMaterialHandle[] materials)
    {
        Name = name;
        Materials = materials;
    }

    public string Name { get; }
    public IReadOnlyList<IRuntimeMaterialHandle> Materials { get; private set; }

    public void SetMaterials(IReadOnlyList<IRuntimeMaterialHandle> materials)
    {
        Materials = materials;
    }
}

sealed class FakeRuntimeMaterial : IRuntimeMaterialHandle
{
    public FakeRuntimeMaterial(
        string name,
        int renderQueue,
        object shaderKeywords,
        Dictionary<string, float> properties)
    {
        Name = name;
        RenderQueue = renderQueue;
        ShaderKeywords = shaderKeywords;
        Properties = properties;
    }

    public string Name { get; set; }
    public string ShaderName { get; set; } = string.Empty;
    public int RenderQueue { get; set; }
    public object ShaderKeywords { get; set; }
    public Dictionary<string, float> Properties { get; private set; }

    public void CopyPropertiesFrom(IRuntimeMaterialHandle source)
    {
        Properties = new Dictionary<string, float>(((FakeRuntimeMaterial)source).Properties);
    }
}

sealed class FakeEmissionSubtree : IEmissionVisualSubtree
{
    public FakeEmissionSubtree(string name, params IEmissionRenderer[] renderers)
    {
        Name = name;
        Renderers = renderers;
    }

    public string Name { get; }
    public IReadOnlyList<IEmissionRenderer> Renderers { get; }
}

sealed class FakeEmissionRenderer : IEmissionRenderer
{
    public FakeEmissionRenderer(string name, params FakeEmissionMaterial[] materials)
    {
        Name = name;
        Materials = materials;
        Blocks = materials.Select(_ => new FakeEmissionPropertyBlock()).ToArray();
    }

    public string Name { get; }
    public IReadOnlyList<IEmissionMaterial> SharedMaterials => Materials;
    public IReadOnlyList<FakeEmissionMaterial> Materials { get; }
    public IReadOnlyList<FakeEmissionPropertyBlock> Blocks { get; }
    public List<int> ReadSlots { get; } = new();
    public List<(int SlotIndex, IEmissionPropertyBlock Block)> Writes { get; } = new();

    public IEmissionPropertyBlock ReadPropertyBlock(int materialIndex)
    {
        ReadSlots.Add(materialIndex);
        return Blocks[materialIndex];
    }

    public void WritePropertyBlock(int materialIndex, IEmissionPropertyBlock propertyBlock)
    {
        Writes.Add((materialIndex, propertyBlock));
    }
}

sealed class FakeEmissionMaterial : IEmissionMaterial
{
    private readonly float _intensity;

    public FakeEmissionMaterial(float intensity)
    {
        _intensity = intensity;
    }

    public int IntensityReadCount { get; private set; }
    public bool WasMutated { get; private set; }

    public float ReadEmissiveIntensity()
    {
        IntensityReadCount++;
        return _intensity;
    }

    public void SimulateMutation()
    {
        WasMutated = true;
    }
}

sealed class FakeEmissionPropertyBlock : IEmissionPropertyBlock
{
    public Dictionary<string, float> FloatProperties { get; } = new();
    public Dictionary<string, NeonRgba> ColorProperties { get; } = new();

    public void SetColor(string propertyName, NeonRgba color)
    {
        ColorProperties[propertyName] = color;
    }
}

sealed class FakeColorRestoreTarget : INeonLetterColorRestoreTarget
{
    private readonly bool _throwOnApply;

    public FakeColorRestoreTarget(int recipeId, bool throwOnApply = false)
    {
        RecipeId = recipeId;
        _throwOnApply = throwOnApply;
    }

    public int RecipeId { get; }
    public List<NeonRgba> AppliedColors { get; } = new();

    public void Apply(NeonRgba color)
    {
        if (_throwOnApply)
        {
            throw new InvalidOperationException("restore failed");
        }

        AppliedColors.Add(color);
    }
}

sealed class FakeBookPageRegistrationTarget : IBookPageRegistrationTarget<string, string>
{
    public int PageCount { get; private set; }
    public Dictionary<string, string> Localizations { get; } = new();
    public string? FailLocalizationKeyOnce { get; set; }
    public string? LastTopRecipe { get; private set; }
    public string? LastBottomRecipe { get; private set; }
    public string? LastBackground { get; private set; }
    public string? LastTitleKey { get; private set; }

    public void AddLocalization(string key, string value)
    {
        if (FailLocalizationKeyOnce == key)
        {
            FailLocalizationKeyOnce = null;
            throw new InvalidOperationException($"Injected localization failure for {key}.");
        }

        Localizations[key] = value;
    }

    public string GetRecipeLocalizationId(string recipe)
    {
        return $"LOCALIZED_{recipe}";
    }

    public void CreatePage(
        string topRecipe,
        string? bottomRecipe,
        string background,
        string titleLocalizationKey)
    {
        LastTopRecipe = topRecipe;
        LastBottomRecipe = bottomRecipe;
        LastBackground = background;
        LastTitleKey = titleLocalizationKey;
        PageCount++;
    }

    public bool LastPageMatches(
        string topRecipe,
        string? bottomRecipe,
        string background,
        string titleLocalizationKey)
    {
        return LastTopRecipe == topRecipe &&
               LastBottomRecipe == bottomRecipe &&
               LastBackground == background &&
               LastTitleKey == titleLocalizationKey;
    }
}
