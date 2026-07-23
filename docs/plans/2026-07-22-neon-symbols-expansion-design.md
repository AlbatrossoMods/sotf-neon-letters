# Neon Symbols Expansion Design

## Status

Approved by the user on 2026-07-22. The extension adds the supplied Cyrillic,
numeric, and punctuation geometry without modifying the existing English A-Z
source or generated assets. The pages remain inside the game's supported
Blueprints book flow; a new native side tab is explicitly out of scope because
SonsSdk does not expose a stable category-registration API.

## Goal

Expand the working small neon alphabet from 26 English letters to 80 buildable
symbols while preserving the established wall placement, color editing, save,
and multiplayer behavior.

## Source Asset

The additive source is:

`/Users/nikita/Documents/Codex/2026-07-21/glb/outputs/neon_letters_extended_game_ready.glb`

Its expected SHA-256 is
`02f9f0fa2d0195824b9f767bc98d1010793475624ed29e893436689ff57679c4`.
It contains exactly 54 glyph roots: 33 uppercase Cyrillic letters, ten digits,
and eleven punctuation symbols. It contains no English A-Z geometry.

The file is copied into a new extension-specific source path. The legacy DAE,
legacy generated prefabs, icons, pages, and their serialized metadata are not
rewritten or renamed.

## Symbol Order and Book Layout

The catalog order is deterministic:

1. English `A-Z` in the existing order;
2. Cyrillic `А Б В Г Д Е Ё Ж З И Й К Л М Н О П Р С Т У Ф Х Ц Ч Ш Щ Ъ Ы Ь Э Ю Я`;
3. digits `0-9`;
4. punctuation `! # $ & * + , - . = ?`.

The complete catalog contains 80 entries and therefore produces 40 full book
pages with two recipes per page. No empty book slot is needed. The existing 13
A-Z pages remain the first 13 pages; 27 pages are appended for the extension.
All pages use the visible title `NEON SYMBOLS` and are registered through
`CustomBlueprintManager.CreateBookPage`, so they remain in the supported
Blueprints book flow.

SonsSdk has no public API for adding a new native side tab. Directly cloning book
UI objects or mutating `_tabs` and `_categoryStartingPage` would be an
undocumented compatibility patch and will not be used.

## Stable Identity and Compatibility

The existing A-Z definitions are compatibility anchors:

- recipe IDs and crafting-node IDs remain unchanged;
- prefab, icon, and page asset names remain unchanged;
- source-node names remain unchanged;
- catalog indexes and the first 13 page indexes remain unchanged.

New definitions are appended after Z and receive recipe IDs from the existing
monotonic ID sequence. New asset keys use ASCII-safe Unicode-based names rather
than filesystem-sensitive display characters. Each definition records its
display symbol, Unicode code point, source root, stable asset key, catalog index,
recipe ID, page index, and page slot.

The recipe ID remains the runtime identity used by color editing, persistence,
and multiplayer validation. No save-envelope or network-protocol schema change
is required; the valid catalog set expands from 26 to 80 recipe IDs.

## GLB Import and Geometry Normalization

Every new symbol is selected by the Unicode code embedded in its `glyph_*` root
name, not by scene order or inconsistent mesh names. The importer keeps the
glyph root scale and child mesh translation but removes only the root translation
that arranges symbols into the GLB presentation grid. Importing mesh data without
its node hierarchy is forbidden because it changes size and pivot placement.

The existing runtime emissive material is applied to all generated symbol
renderers. The five equivalent GLB materials are not retained as distinct runtime
materials, and the GLB has no texture dependency.

Prefab geometry, wall offset, collider size, and icon framing are derived from
each normalized glyph's renderer bounds. The extension uses one uniform scale
anchored to Cyrillic `А` at 0.5 Unity units, preserving the GLB's relative sizes
so accents and punctuation are not independently inflated. This is required for
tall glyphs such as `Ё` and `Й`, wide symbols such as `+`, `-`, and `=`, and
baseline-sensitive `,` and `.`. Shared placement semantics remain the same as the
working A-Z build.

## Generated Assets and Runtime Binding

The Unity generator gains a data-driven extension manifest while retaining the
legacy A-Z generation path and names. It creates one prefab and one icon for each
new symbol and appends the required page backgrounds. Asset-bundle bindings are
extended additively and expose catalog-key lookup for all 80 definitions.

Blueprint registration continues to use the existing construction recipe,
ingredients, placement policy, shaders, and book-page coordinator. The
coordinator is generalized from English `char` assumptions to stable symbol keys
without changing the behavior of existing definitions.

## Runtime Behavior

Every added symbol must:

- appear in the Blueprints book in the specified order;
- preview and snap to walls with its face toward the player;
- build through the existing light-bulb and wire recipe;
- remain visible after completion;
- expose the existing Use-key color editor;
- persist its selected color through the existing save path;
- use the existing host-authoritative multiplayer color and persistence flow.

This iteration does not add sizes, lowercase characters, new ingredients, a new
color system, a custom book tab, or a new multiplayer protocol.

## Test Strategy

Implementation follows red-green-refactor cycles. Permanent automated tests
cover:

- the source checksum and exact 54-root inventory;
- the exact 80-symbol catalog order;
- byte-for-byte compatibility assertions for legacy A-Z identity and names;
- unique recipe IDs, crafting IDs, Unicode keys, prefab names, and icon names;
- exactly 40 complete two-recipe book pages;
- normalized per-glyph hierarchy, bounds, collider depth, and wall-facing pivot;
- all 80 prefabs and icons plus all 40 page textures in the bundle;
- color, save, multiplayer validation, and late-join policy accepting every new
  recipe ID without accepting unrelated structures;
- release packaging containing the updated DLL and bundle without source or
  temporary build artifacts.

Runtime Single Player acceptance samples at least one English letter, several
Cyrillic shapes, a digit, and punctuation, with screenshots of the book, wall
preview, completed build, color editing, and reload. Multiplayer code is covered
by automated protocol tests; no public multiplayer session is entered.

## Cleanup and Failure Handling

Generation runs in extension-specific source and generated directories. Temporary
conversion files are removed after a successful build and never packaged. A
missing root, duplicate Unicode code, unexpected source hash, invalid bounds, or
asset-name collision fails generation before replacing the release bundle.
