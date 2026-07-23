# Prepared neon letter textures

The `textures/` directory contains PNG working copies generated from the downloaded JPEG maps. The source archive and original JPEG files are kept under `assets/source/neon-letters`.

The downloaded albedo and normal maps contain the brick background, while the downloaded emissive and metallic maps are black. They are kept with a `_Source` suffix for reference and are not the final letter material.

`NeonLetters_Smoothness.png` is the inverted copy of the source roughness map for shaders that expect smoothness. `NeonLetters_EmissionMask.png` is a 1×1 white mask; the actual neon color belongs in the material.

Before the asset is packaged for the game:

- remove the `BG` mesh from the model;
- use `NeonLetters_EmissionMask.png` with a chosen neon color for the letter material;
- import the normal map as a Normal Map;
- disable sRGB for metallic, roughness, normal, and smoothness maps;
- verify the final material with the Sons of the Forest shader in Unity 2022.2.16f1.
