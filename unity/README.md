# Unity Asset Pipeline

This repository contains the Unity 2022.2.16f1 asset project for the playable Small A-Z neon alphabet.
`Packages/manifest.json` pins `com.unity.render-pipelines.high-definition` 14.0.7, the HDRP package for Unity 2022.2.

From the repository root, run:

```bash
./tools/build-unity-assets.sh
```

The bundle is written to:

```text
unity/SOTFNeonLetters.Assets/Build/AssetBundles/Windows/sotfneonletters
```

The wrapper uses the native Unity 2022.2.16f1 editor at `/Applications/Unity/Hub/Editor/2022.2.16f1/Unity.app/Contents/MacOS/Unity`; `UNITY_EDITOR_PATH` can override that executable. The editor must have a valid activated Unity license. The wrapper copies the canonical DAE and white emission mask into the ignored `Assets/GeneratedSource` directory before starting Unity.

`Canonical/GeneratedAssets.zip` is a versioned identity snapshot containing `Assets/GeneratedSource`, `Assets/Generated`, and all corresponding `.meta`, prefab, material, and texture assets. Every build validates that the archive contains only regular files under those exact roots, extracts it through a temporary directory, replaces only the ignored generated trees, and then updates generated assets in place. This keeps GUIDs and serialized file IDs identical in a fresh checkout. If generated asset paths or hierarchy change intentionally, regenerate and commit the snapshot together with the generator change; the clean tracked-input release gate will fail until the snapshot and release bundle agree.

Every Small letter is independently normalized to a `0.5f` target height, matching the approximate diameter of one horizontal log. Its proportions remain unchanged and its root pivot is bottom-centered.

The bundle contains exactly 65 explicitly mapped assets, plus their material and mesh dependencies:

```text
Assets/Generated/Prefabs/NeonLetter_{A-Z}_Small.prefab
Assets/Generated/Textures/NeonLetter_{A-Z}_Small_Icon.asset
Assets/Generated/Textures/NeonLetters_Small_Page_{01-13}.asset
```

Their short runtime addresses match those names without the `.prefab` or `.asset` suffixes.

Each prefab has two top-level visible ingredient children. `Ingredient_LightBulb_{A-Z}` is the matching imported DAE letter subtree; `Ingredient_Wire_Lead` is the same short LineRenderer lead used by the working A prefab. Both materials require `HDRP/Lit`; there is no URP or built-in shader fallback. The builder sets HDRP 14 `_BaseColor`, `_Metallic`, and `_Smoothness`; the letter additionally uses `_EmissiveColorMap`, `_EmissiveColorLDR`, `_UseEmissiveIntensity`, `_EmissiveIntensity` in nits, `_EmissiveIntensityUnit`, and `_EmissiveExposureWeight`, enables double-sided rendering, then runs HDRP keyword validation and requires `_EMISSIVE_COLOR_MAP`. The prefab does not author a collider; SonsSdk creates the runtime collider, which the mod resizes to the visible local bounds.

Each 1024x1024 DXT1 page has 11 mip levels and contains two matching letter icons in the upper and lower recipe cards. Each 128x128 DXT1 icon has 8 mip levels and is rasterized from that letter's own DAE triangles with safe margins. The textures do not use imported fonts or Signs assets.

After the Unity build, the runtime bundle path is:

```text
Mods/SOTFNeonLetters/sotfneonletters
```
