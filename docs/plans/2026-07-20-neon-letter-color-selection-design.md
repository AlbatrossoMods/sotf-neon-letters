# Neon Letter Color Selection Design

## Status

Approved by the user on 2026-07-20. This iteration starts from checkpoint commit `0e6e19e`, where the complete A-Z small-letter alphabet is buildable and covered by the existing test gate.

## Goal

Allow a Single Player user to aim at an already-built neon letter, open a native-feeling color editor, choose any color, preview it on that one letter, and persist the applied color with the game save.

## User Experience

- A completed neon letter exposes a `Change color` interaction while it is focused.
- The interaction uses the game's existing Use action instead of introducing a hard-coded `V` key.
- Pressing Use opens an SUI panel with a color wheel, current-color preview, hexadecimal readout, and Apply, Cancel, and Reset actions.
- Color-wheel changes update the selected letter as a live preview.
- Apply keeps the previewed color and records it for persistence.
- Cancel restores the color that was active when the panel opened.
- Reset previews the original neon color and can be committed with Apply.
- Closing the world or losing the selected target closes the editor without leaving an uncommitted preview.

## Interaction Architecture

Each registered built-letter prefab receives a SonsSdk interaction proxy. The proxy participates in the game's normal focus system, displays the custom link prompt, and identifies the parent `ScrewStructure`. A handler for `GlobalInput.OnUsePerformed` opens the editor only when the focused proxy belongs to a completed A-Z neon-letter recipe.

This is preferred over a custom camera raycast because the game already owns focus selection and Use-key remapping. It is also preferred over construction-time color selection because it supports the requested post-build editing flow without changing blueprint placement.

## Rendering

Only renderers under the selected letter's `Ingredient_LightBulb_{Letter}` subtree are recolored. The wire subtree remains unchanged.

The editor applies `_EmissiveColor` through a per-renderer, per-material-slot `MaterialPropertyBlock`. Existing property-block values are read before the emissive override is written. Shared materials are never mutated, so changing one built letter cannot recolor other instances or prefabs.

The existing emissive intensity remains unchanged. The selected RGB color is converted to the linear HDRP emissive value expected by the installed Unity 2022.2/HDRP 14 shader.

## Persistence

One SonsSdk `ICustomSaveable` stores a versioned envelope containing entries with:

- native `ScrewStructure` `SaveId`;
- neon-letter recipe ID for validation and migration;
- selected RGBA color.

The game already persists and restores `SaveId` for screw structures. After save loading completes, the mod resolves each saved ID through `ScrewStructureManager`, verifies that the resolved structure still uses a neon-letter recipe, and reapplies the color. Destroyed or dismantled structures remove their corresponding color entry.

No transform, Unity instance ID, raw pointer, or hierarchy path is used as persistent identity. Bolt state and network prefab state are not modified.

## Scope

This iteration supports Single Player only. It does not add multiplayer color replication, construction-preview coloring, fixed palette presets, textual RGB/HEX input, animated neon effects, or changes to blueprint placement and recipes.

## Test Strategy

Development follows strict red-green-refactor cycles. Permanent tests cover observable behavior at testable boundaries:

- only A-Z neon recipes are editable;
- one `SaveId` maps to one persisted color entry;
- applying commits the previewed color;
- cancelling restores the original color and does not change persisted state;
- reset selects the original neon color;
- serialization round-trips recipe ID, save ID, and RGBA values;
- stale or mismatched save entries are ignored;
- recoloring targets only the selected letter's light-bulb renderers and never shared materials.

The full existing contract, Unity asset, build, and packaging gates must remain green.

Single Player smoke testing must capture screenshots for the interaction prompt, open color wheel, changed letter, and unchanged neighboring letter. The game must not enter Multiplayer and the existing test save must not be saved. Because of the no-save constraint, an actual game save/reload persistence smoke test requires separate user permission; persistence is otherwise covered by automated state and serializer tests.

