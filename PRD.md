# Product Brief: SOTF Neon Letters

Status: Brief only. No feature implementation has been approved or started.

## Product idea

Add craftable and placeable neon English letters to Sons of the Forest. Each letter is an independent object that the player can build, position, rotate, destroy, and keep in the world as part of a base or sign.

The existing custom-sign mod is the reference for the player-facing placement and construction flow. The new objects are individual neon letters, not wooden signs and not a text-board replacement.

## In-scope requirements

### Letter set

- Provide one asset for every letter of the English alphabet: A–Z.
- The initial brief assumes uppercase letters. Lowercase letters are an open decision.
- Every letter must be available in three physical sizes:
  - Small: approximately the height of one horizontal full-length log.
  - Medium: approximately the height of two horizontal full-length logs.
  - Large: approximately the height of three horizontal full-length logs.
- The log dimensions are a visual/gameplay reference. The letters do not need to contain or display wooden logs.

This implies 78 placeable variants before any future lowercase, number, or punctuation expansion.

### Construction and placement

- Letters must be buildable by the player rather than spawned only through a debug command.
- Use the same general interaction model as the existing Signs mod: discoverable through the game's construction flow, placeable in the world, rotatable, removable, and compatible with normal building placement behavior.
- The exact UI location—book category, custom menu, or another RedLoader-supported entry point—will follow the technical constraints found when the reference mod is inspected.
- Each letter is placed as a separate object so players can compose words and logos manually.

### Crafting

- Proposed starting recipe: one lamp/light component plus one wire component.
- The exact vanilla item IDs, quantities, and whether the recipe is shared by all sizes remain to be verified.
- Crafting should be exposed through the normal in-game crafting/construction flow rather than a developer-only command.

### Visual behavior

- Letters must use an emissive neon material so that the tube/face appears self-lit.
- A nearby light source may be added to illuminate surrounding objects.
- Color variants, animation, flicker, bloom, and power-state controls are future extensions unless explicitly added to the next revision of this brief.

### Persistence and multiplayer

- Placed letters should persist in the save in the same way as the chosen construction flow.
- Multiplayer behavior must be tested for host, client, and dedicated-server scenarios before a release claim is made.
- A client-only visual prototype is acceptable during development, but it is not the final product behavior.

## Out of scope for this brief

- Bears or any other new animals.
- A full text-entry sign editor.
- Lowercase letters, numbers, punctuation, icons, or non-English alphabets.
- Automatic word generation or a font renderer.
- Neon animation, flicker, dimming, remote power switches, or electricity simulation.
- Reusing the existing Signs mod's code or assets without checking its permissions.

## Suggested product shape

The first release should prioritize a reliable placeable letter over visual variety:

1. One letter, one size, one emissive material, and one working recipe.
2. All A–Z letters at that size.
3. Medium and large variants.
4. Save and multiplayer validation.
5. Optional colors and animation in a later revision.

## Open decisions

- Uppercase only for the first release, or uppercase plus lowercase?
- One recipe per letter/size, or one selector that chooses the letter and size?
- Fixed neon color, or several craftable colors?
- Should the letters be tubes, solid illuminated panels, or a hybrid style?
- Should all three sizes use the same crafting cost?
- Is nearby illumination required, or is emissive appearance sufficient?

## Technical baseline

- Loader/SDK: RedLoader 0.8.6.
- Project template: `RedLoader.Templates@1.2.7`.
- Target framework from the generated template: `net6`.
- Asset pipeline: custom model/material assets loaded through RedLoader's supported asset workflow.
- The existing Signs mod is a behavior reference, not an automatic dependency of this project.
