# Neon Symbols Expansion Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add the supplied 54 Cyrillic, numeric, and punctuation glyphs to the existing buildable neon alphabet while preserving every legacy A-Z identity and behavior.

**Architecture:** A single pure-C# symbol manifest is the source of truth for runtime definitions and the Unity editor build. The existing Unity generator keeps its A-Z path and imports the additive GLB through pinned glTFast 5.0.4, then emits extension prefabs, icons, and pages into the existing bundle. Runtime lookup, Blueprints pages, color editing, saves, and multiplayer continue to use recipe IDs from the expanded catalog.

**Tech Stack:** C#/.NET 6, RedLoader 0.8.6, SonsSdk 0.8.6, Unity 2022.2.16f1, HDRP 14.0.7, Unity glTFast 5.0.4, shell release gates.

---

### Task 1: Ingest and verify the additive source asset

**Files:**
- Create: `assets/source/neon-symbols/neon_letters_extended_game_ready.glb`
- Create: `assets/source/neon-symbols/README.md`
- Create: `assets/source/neon-symbols/symbol-inventory.json`
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`

**Step 1: Write the failing source-contract test**

Add a behavior-focused contract that resolves the repository root and verifies:

```csharp
const string ExpectedSha256 =
    "02f9f0fa2d0195824b9f767bc98d1010793475624ed29e893436689ff57679c4";
CheckEqual(ExpectedSha256, ComputeSha256(sourcePath),
    "the tracked extension GLB is the approved source asset");
CheckEqual(54, inventory.Count,
    "the extension inventory exposes every supplied symbol exactly once");
CheckSequence(
    "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ0123456789!#$&*+,-.=?",
    inventory.Select(entry => entry.Symbol),
    "the extension inventory uses the approved user-visible order");
```

The inventory must also reject duplicate symbols, Unicode codes, source-root names,
and asset keys.

**Step 2: Run the contract suite and verify red**

Run:

```bash
DOTNET_ROOT="$PWD/.tools/dotnet-6" \
DOTNET_CLI_HOME="$PWD/.tools/dotnet-cli" \
"$PWD/.tools/dotnet-6/dotnet" run \
  --project tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
```

Expected: FAIL because the tracked GLB and inventory do not exist.

**Step 3: Add the approved binary and inventory**

Copy the supplied file byte-for-byte from:

```text
/Users/nikita/Documents/Codex/2026-07-21/glb/outputs/neon_letters_extended_game_ready.glb
```

Record all 54 entries in `symbol-inventory.json` with these fields:

```json
{
  "symbol": "А",
  "unicode": "U0410",
  "assetKey": "CYR_U0410",
  "sourceRoot": "glyph_CYR_U0410.013"
}
```

Digits use `DIG_U0030` through `DIG_U0039`. Punctuation uses stable keys such as
`PUNC_U0021_EXCLAMATION` and the exact `glyph_PUNC_*` root names. The README records
the source path, checksum, approved order, and the rule that this file supplements
rather than replaces the legacy DAE.

**Step 4: Run the contract suite and verify green**

Run the command from Step 2.

Expected: PASS, including source checksum, count, order, and uniqueness.

**Step 5: Commit**

```bash
git add assets/source/neon-symbols tests/SOTFNeonLetters.ContractTests/Program.cs
git commit -m "assets: add extended neon symbol source"
```

### Task 2: Expand the data model without changing legacy A-Z identity

**Files:**
- Create: `NeonSymbolManifest.cs`
- Modify: `NeonLetterSmallCatalog.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj`
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`

**Step 1: Write failing catalog compatibility tests**

Add tests for observable catalog behavior:

```csharp
CheckEqual(80, NeonLetterSmallCatalog.All.Count,
    "the small catalog contains every supported neon symbol");
CheckSequence(expectedSymbols,
    NeonLetterSmallCatalog.All.Select(definition => definition.Symbol),
    "the Blueprints catalog keeps the approved symbol order");
CheckEqual(40,
    NeonLetterSmallCatalog.All.Select(d => d.BookPageIndex).Distinct().Count(),
    "eighty symbols fill forty paired Blueprints pages");
```

Capture legacy behavior with explicit expectations for each A-Z entry:

```csharp
for (int index = 0; index < 26; index++)
{
    char letter = (char)('A' + index);
    var definition = NeonLetterSmallCatalog.Get(letter);
    CheckEqual(NeonLetterSmallCatalog.BaseRecipeId + index * 2, definition.RecipeId,
        $"legacy {letter} recipe ID remains stable");
    CheckEqual($"NeonLetter_{letter}_Small", definition.PrefabAssetName,
        $"legacy {letter} prefab name remains stable");
    CheckEqual(index / 2, definition.BookPageIndex,
        $"legacy {letter} page remains stable");
}
```

Also verify all 80 recipe IDs, crafting IDs, symbols, Unicode codes, source roots,
asset keys, prefab names, icon names, and ingredient child names are unique.

**Step 2: Run the contract suite and verify red**

Run the Task 1 contract command.

Expected: FAIL because the catalog still contains only A-Z.

**Step 3: Add the shared manifest and minimal catalog generalization**

Create a Unity-compatible pure-C# manifest (no records, LINQ, JSON, or Unity types):

```csharp
public sealed class NeonSymbolManifestEntry
{
    public NeonSymbolManifestEntry(
        char symbol,
        string unicodeCode,
        string assetKey,
        string sourceNodeName,
        NeonSymbolSource source)
    {
        Symbol = symbol;
        UnicodeCode = unicodeCode;
        AssetKey = assetKey;
        SourceNodeName = sourceNodeName;
        Source = source;
    }

    public char Symbol { get; }
    public string UnicodeCode { get; }
    public string AssetKey { get; }
    public string SourceNodeName { get; }
    public NeonSymbolSource Source { get; }
}
```

`NeonSymbolManifest.All` contains the existing 26 definitions first and the exact
54 inventory definitions after them. Extend `NeonLetterSmallDefinition` with
`Symbol`, `UnicodeCode`, and `AssetKey`. Keep `Letter` as a compatibility alias.
Generate identity as follows:

```csharp
RecipeId = NeonLetterSmallCatalog.BaseRecipeId + catalogIndex * 2;
BookPageIndex = catalogIndex / 2;
BookSlot = catalogIndex % 2 == 0
    ? NeonLetterBookSlot.Top
    : NeonLetterBookSlot.Bottom;
PrefabAssetName = $"NeonLetter_{AssetKey}_Small";
BookIconAssetName = $"NeonLetter_{AssetKey}_Small_Icon";
```

For legacy entries `AssetKey` is the letter itself, preserving all old names.
Change the visible common page title to `Neon Symbols`, but keep the established
localization-key prefix and first 13 page asset names.

**Step 4: Run the contract suite and verify green**

Run the Task 1 contract command.

Expected: PASS with 80 definitions, 40 pages, and unchanged A-Z identity.

**Step 5: Commit**

```bash
git add NeonSymbolManifest.cs NeonLetterSmallCatalog.cs \
  tests/SOTFNeonLetters.ContractTests
git commit -m "feat: define extended neon symbol catalog"
```

### Task 3: Import the GLB and generate extension assets in Unity

**Files:**
- Modify: `unity/SOTFNeonLetters.Assets/Packages/manifest.json`
- Modify: `unity/SOTFNeonLetters.Assets/Packages/packages-lock.json`
- Modify: `tools/build-unity-assets.sh`
- Modify: `unity/SOTFNeonLetters.Assets/Assets/Editor/BuildNeonLetterA.cs`
- Modify: `unity/SOTFNeonLetters.Assets/Assets/Editor/NeonAlphabetAssetTests.cs`
- Create: `unity/SOTFNeonLetters.Assets/Canonical/NeonSymbolExtensionGeneratedAssets.zip`

**Step 1: Write failing Unity asset tests**

Generalize the test cases from 26 letters to the 80-entry shared manifest and add
observable checks for:

- the imported GLB resolves every exact source root once;
- the GLB contributes exactly 54 generated prefabs and icons;
- legacy A-Z remain 0.5 Unity units tall and the extension preserves one shared
  scale anchored to Cyrillic `А` at 0.5 units;
- every prefab has one visible light-bulb ingredient and one wire ingredient;
- renderer bounds produce finite, positive, close-fitting colliders;
- the front orientation matches the working A-Z orientation;
- all 40 page textures are 1024x1024 DXT1 with 11 mips;
- all 80 icons are 128x128 DXT1 with 8 mips;
- the bundle contains exactly 200 assets: 80 prefabs, 80 icons, and 40 pages;
- the first 26 prefab/icon names and first 13 page names are unchanged.

**Step 2: Run Unity tests and verify red**

Run:

```bash
./tools/test-unity-assets.sh
```

Expected: FAIL because Unity cannot yet import the GLB and the extension assets are
absent.

**Step 3: Pin the editor importer and stage additive inputs**

Pin `com.unity.cloud.gltfast` to `5.0.4`. The official importer supports editor
`.glb` import, node hierarchy, HDRP, and Unity 2019.4+, which includes the pinned
Unity 2022.2.16f1 editor.

Update `build-unity-assets.sh` to copy these tracked inputs into
`Assets/GeneratedSource` before the synchronous refresh:

```text
NeonSymbolManifest.cs
NeonLetters_Extended.glb
```

Keep the existing DAE, mask, and canonical snapshot flow unchanged. Extract a
second extension-only canonical metadata snapshot so clean builds reuse stable
GUIDs for only the new GLB and new generated assets; do not rewrite
`Canonical/GeneratedAssets.zip`.

**Step 4: Extend the generator additively**

Use `NeonSymbolManifest.All` as the combined order. Load the legacy DAE once and
the imported GLB once, then select each source root from the correct model. Preserve
the source hierarchy, root scale, and child translation; placement-grid translation
is eliminated by the existing bounds centering. Continue the established per-glyph
behavior for legacy A-Z. For the extension, calculate the uniform scale once from
Cyrillic `А` and reuse it for all 54 symbols so punctuation keeps the authored
relative proportions:

```csharp
float uniformScale = SmallTargetHeight / initialBounds.size.y;
geometry.transform.localScale *= uniformScale;
geometry.transform.position += new Vector3(
    -scaledBounds.center.x,
    -scaledBounds.min.y,
    -scaledBounds.center.z);
```

Reuse the existing cyan HDRP material rather than retaining the five equivalent
GLB materials. Generate only pages 14-40 from extension icons; pages 1-13 continue
to use the legacy path and names.

**Step 5: Run Unity tests and reproducibility twice**

Run:

```bash
./tools/test-unity-assets.sh
./tools/test-clean-unity-reproducibility.sh
```

Expected: PASS. Two clean Unity builds produce identical bundle bytes, and tracked
inputs remain unchanged.

**Step 6: Commit**

```bash
git add unity/SOTFNeonLetters.Assets/Packages tools/build-unity-assets.sh \
  unity/SOTFNeonLetters.Assets/Assets/Editor \
  unity/SOTFNeonLetters.Assets/Canonical/NeonSymbolExtensionGeneratedAssets.zip
git commit -m "feat: generate extended neon symbol assets"
```

### Task 4: Bind all assets and register 40 Blueprints pages

**Files:**
- Create: `Assets.Extension.cs`
- Modify: `Assets.cs`
- Modify: `NeonLetterASmallBlueprint.cs`
- Modify: `NeonLetterRuntimePolicy.cs`
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`

**Step 1: Write failing runtime binding and page tests**

Extend contract coverage to require one `[AssetReference]` for every generated
prefab, icon, and page. Drive the coordinator with all 80 recipes and assert:

```csharp
CheckEqual(40, readyPages.Count,
    "eighty recipe callbacks create forty complete Blueprints pages");
CheckSequence(Enumerable.Range(0, 40), readyPages.Select(page => page.PageIndex),
    "Neon Symbols pages are registered in catalog order");
CheckEqual('Я', readyPages[29].TopDefinition.Symbol,
    "the final Cyrillic symbol precedes the first digit");
CheckEqual('0', readyPages[29].BottomDefinition.Symbol,
    "the first digit fills the next available page slot");
```

Also assert all pages use visible title `Neon Symbols` and no empty bottom recipe.

**Step 2: Run the contract suite and verify red**

Run the Task 1 contract command.

Expected: FAIL because runtime bindings still stop at A-Z/page 13.

**Step 3: Add extension-only static bindings**

Change `Assets` to a partial class without altering existing A-Z properties. Put
the 54 prefab references, 54 icon references, and page 14-40 references in
`Assets.Extension.cs`. Use stable ASCII property names based on `AssetKey`.

Modify only the fallback branches of `GetPrefab`, `GetBookIcon`, and `GetBookPage`
to route extension symbols/indexes to the new partial implementation. Unknown
symbols and page indexes still throw `ArgumentOutOfRangeException`.

**Step 4: Generalize registration wording and symbol lookup**

Use `definition.Symbol`/`definition.AssetKey` in lookup and diagnostics. Keep
`CustomBlueprintManager.TryRegister`, the two existing ingredients, placement,
collider fitting, shader replacement, and `CreateBookPage` unchanged. Rename only
user-facing/error text that incorrectly claims the catalog is A-Z-only.

**Step 5: Run .NET contracts and the Unity asset gate**

Run:

```bash
DOTNET_ROOT="$PWD/.tools/dotnet-6" \
DOTNET_CLI_HOME="$PWD/.tools/dotnet-cli" \
"$PWD/.tools/dotnet-6/dotnet" run \
  --project tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
./tools/test-unity-assets.sh
```

Expected: PASS with 80 bindings and 40 complete Blueprints pages.

**Step 6: Commit**

```bash
git add Assets.cs Assets.Extension.cs NeonLetterASmallBlueprint.cs \
  NeonLetterRuntimePolicy.cs tests/SOTFNeonLetters.ContractTests/Program.cs
git commit -m "feat: register extended neon symbols in Blueprints"
```

### Task 5: Prove color, persistence, and multiplayer accept extension IDs

**Files:**
- Modify: `tests/SOTFNeonLetters.ContractTests/Program.cs`
- Modify only if a red test exposes a hardcoded range: `NeonLetterColorInteractionPolicy.cs`
- Modify only if a red test exposes a hardcoded range: `NeonLetterColorPersistencePolicy.cs`
- Modify only if a red test exposes a hardcoded range: `NeonLetterMultiplayerState.cs`
- Modify only if a red test exposes a hardcoded range: `NeonLetterMultiplayerPersistencePolicy.cs`

**Step 1: Write failing cross-system behavior tests**

Select representative extension IDs for `Я`, `7`, and `?`. Assert that each:

- is editable after construction;
- survives the Single Player color envelope round trip;
- is accepted by the host-authoritative color state only when host/live checks pass;
- survives multiplayer world-state filtering and serialization;
- remains rejected when an adjacent odd crafting-node ID or unrelated ID is used.

Example:

```csharp
int questionRecipeId = NeonLetterSmallCatalog.Get('?').RecipeId;
CheckEqual(true,
    NeonLetterColorInteractionPolicy.IsEditable(true, questionRecipeId),
    "a completed punctuation structure exposes the color editor");
CheckEqual(false,
    NeonLetterColorInteractionPolicy.IsEditable(true, questionRecipeId - 1),
    "a crafting-node ID is not mistaken for a completed symbol recipe");
```

**Step 2: Run the contract suite and inspect red/green**

Run the Task 1 contract command.

Expected: preferably PASS because policies derive their known IDs from the catalog.
If any test fails, confirm it is a real hardcoded A-Z behavior before changing code.

**Step 3: Apply the minimum policy correction, if required**

Replace only hardcoded count/range assumptions with membership in
`NeonLetterSmallCatalog.All`. Do not change packet schemas, protocol version,
save-envelope version, authority rules, retry behavior, or UI behavior.

**Step 4: Run the contract suite and verify green**

Run the Task 1 contract command.

Expected: PASS for legacy and extension recipes, including negative unrelated-ID
cases.

**Step 5: Commit**

```bash
git add tests/SOTFNeonLetters.ContractTests/Program.cs \
  NeonLetterColorInteractionPolicy.cs NeonLetterColorPersistencePolicy.cs \
  NeonLetterMultiplayerState.cs NeonLetterMultiplayerPersistencePolicy.cs
git commit -m "test: cover extended neon runtime policies"
```

Only stage policy files that actually changed.

### Task 6: Build and package the expanded release

**Files:**
- Modify: `README.md`
- Modify: `manifest.json`
- Modify: `SOTFNeonLetters.csproj`
- Modify: `tests/SOTFNeonLetters.ReleaseTests/Program.cs`
- Replace generated release artifact: `ReleaseBuild/SOTFNeonLetters.zip`

**Step 1: Write failing release expectations**

Require release documentation and package metadata to describe 80 small neon
symbols, 40 Blueprints pages, color editing, and multiplayer parity. Require the
release bundle to resolve representative English, Cyrillic, numeric, and
punctuation asset names.

**Step 2: Run release tests and verify red**

Run:

```bash
./tools/test-all.sh
```

Expected: FAIL because version/docs/release artifact still describe the 26-letter
release.

**Step 3: Update release metadata narrowly**

Bump the mod version from `0.2.0` to `0.3.0` in the project and manifest. Update
README scope and installation compatibility. Do not claim real two-peer runtime
certification; retain the existing multiplayer caveat.

**Step 4: Run the complete clean gate**

Run:

```bash
./tools/test-all.sh
```

Expected final lines include:

```text
Unity asset bundle reproducibility test passed.
Unity asset tests passed.
Cold tracked-input full release gate passed.
All SOTF Neon Letters test gates passed.
```

**Step 5: Inspect cleanup and package contents**

Run:

```bash
git status --short
unzip -l ReleaseBuild/SOTFNeonLetters.zip
```

Expected: only intentional tracked changes; release contains the mod DLL,
manifest, README, and one bundle, with no GLB source, Unity Library, logs, temp
files, or build intermediates.

**Step 6: Commit**

```bash
git add README.md manifest.json SOTFNeonLetters.csproj \
  tests/SOTFNeonLetters.ReleaseTests/Program.cs ReleaseBuild/SOTFNeonLetters.zip
git commit -m "release: package extended neon symbols"
```

### Task 7: Single Player runtime acceptance

**Files:**
- Create screenshots under an external temporary evidence directory, not the repo
- Modify no source files unless a reproduced defect first receives a failing test

**Step 1: Verify the game is not already running**

Check the CrossOver process before requesting or performing any install. If the
game is running, do not replace the DLL or bundle until it is closed.

**Step 2: Install the exact tested release**

Install only the DLL and asset bundle from the committed `0.3.0` release into the
existing RedLoader layout. Do not touch saves or unrelated mods.

**Step 3: Exercise representative symbols in Single Player**

Use the existing single save without saving changes. Capture a screenshot after
each checkpoint:

1. Blueprints pages show A-Z followed by Cyrillic, digits, and punctuation.
2. `A`, `Ё`, `Ж`, `Я`, `0`, `7`, `+`, `,`, and `?` each preview against a wall.
3. The same samples build against the wall, face forward, and remain visible.
4. The Use key opens the existing full-size color picker on `Ж`, `7`, and `?`.
5. Applied colors are visible after closing and reopening the picker.
6. Exit to menu and reload without saving; the game and RedLoader remain stable.

**Step 4: Classify failures before editing**

For any defect, preserve its screenshot and logs, add the narrowest permanent
behavior test that reproduces it, verify red, then make the minimum fix. Do not add
fallback hacks or broad placement safeguards.

**Step 5: Run the final gate and commit any tested correction**

Run `./tools/test-all.sh` after every runtime-derived correction. Commit only when
the full gate is green and the relevant screenshot confirms the behavior.
