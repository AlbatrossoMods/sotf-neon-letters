# Neon Letters Material Specification

This directory defines the material data contract for the neon-letter assets. It does not contain a Unity shader and does not claim that an asset bundle has been built.

## Texture assignments

Use the prepared textures from `../textures/` as follows:

| Material input | Texture | Import settings | Use |
| --- | --- | --- | --- |
| Emission mask | `NeonLetters_EmissionMask.png` | sRGB on; white 1x1 image | Required. The white pixel enables emission across the letter material. |
| Normal | `NeonLetters_Normal_Source.png` | Texture Type: Normal map; sRGB off | Optional surface detail, subject to Unity import validation. |
| Smoothness | `NeonLetters_Smoothness.png` | sRGB off; data texture | Optional. This is the prepared inverse of the source roughness data. |

The glow color is a material parameter. It must not be baked into the emission mask: the mask remains solid white, and the final material chooses the neon color at runtime or in the material instance.

## Source textures that must not be used

Do not assign these source maps to the final material:

- `NeonLetters_Albedo_Source.png` — contains the brick background.
- `NeonLetters_Emissive_Source.png` — contains black emissive data and is not the intended neon mask.
- `NeonLetters_Metallic_Source.png` — contains black data and is not a reliable final metallic map.
- `NeonLetters_Roughness_Source.png` — source input only; use the prepared `NeonLetters_Smoothness.png` if smoothness data is required.

Do not use the original brick-background material from the downloaded model. The final base color and glow color are material parameters, with the white emission mask controlling where the glow is applied.

## Import requirements

- Color textures and the white emission mask use sRGB.
- Normal and numeric/data maps use sRGB disabled.
- The normal texture must be imported as a Normal map, not as a color texture.
- No shader name is prescribed here; the material must be implemented with a shader confirmed to work with the target Sons of the Forest/Unity runtime.

## Letter sizes

Use one canonical letter mesh and uniform scaling for the three gameplay sizes. The reference is a horizontal full-length log:

| Size | Relative scale | Target height |
| --- | ---: | --- |
| Small | 1x | Approximately the height of one horizontal log |
| Medium | 2x | Approximately the height of two horizontal logs |
| Large | 3x | Approximately the height of three horizontal logs |

The three sizes share the same texture set and material parameters. Exact world-unit dimensions and placement pivots must be validated after importing the model into the target Unity version.
