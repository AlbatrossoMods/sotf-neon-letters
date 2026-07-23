# SOTF Neon Symbols

RedLoader/SonsSdk mod for buildable small neon symbols in Sons of the Forest.

Version 0.3.0 contains 80 small neon symbols: English A-Z, Cyrillic А-Я (including Ё), digits 0-9, and `! # $ & * + , - . = ?`. They are available as forty paired pages in the game's Blueprints book under the `Neon Symbols` page title, in this order: English alphabet, Cyrillic alphabet, digits, then punctuation.

Each symbol uses the standard SonsSdk placement and crafting flow, costs exactly one Wire (item 418) and one Light Bulb (item 635), and can be mounted on a wall. Completed symbols have a post-build `E`-key color picker. Medium and large variants and electrical power-grid behavior are separate iterations.

## Multiplayer through SOTFSDK

The host and every client must use exactly the same 0.3.0 DLL and asset bundle. Apply is host-authoritative: a client requests a color change, then the host validates it, applies the accepted color, and broadcasts that state. Current clients receive each accepted color, while late joiners request a snapshot of the current color state.

Multiplayer persistence is host-only. Restore uses the native `ScrewStructure` first; only when a saved letter is missing its native identity may the host use one Bolt fallback spawn. Single Player placement, color editing, and persistence remain available.

Dedicated-server compatibility is not runtime-certified. Real two-peer multiplayer acceptance and save/reload acceptance are still pending and must not be treated as tested.

## Local setup

- Install RedLoader in the Sons of the Forest game directory and run the game once, so RedLoader generates `_Redloader/Game` interop assemblies.
- Install .NET SDK 6 and Unity 2022.2.16f1 with Windows Build Support (Mono).
- Create the ignored local game-path file from the template:

```bash
cp SOTFNeonLetters.csproj.user.example SOTFNeonLetters.csproj.user
```

Set `GameDir` in `SOTFNeonLetters.csproj.user` to the game directory inside the Steam or CrossOver installation. The file is intentionally excluded from Git.

## Unity asset editor

The asset build uses Unity 2022.2.16f1. Set `UNITY_EDITOR_PATH` if the editor executable is not discoverable at the default macOS location.

```text
/Applications/Unity/Hub/Editor/2022.2.16f1/Unity.app/Contents/MacOS/Unity
```

The editor version can be verified without touching the game bottle:

```bash
"/Applications/Unity/Hub/Editor/2022.2.16f1/Unity.app/Contents/MacOS/Unity" -version
```

## Build and test order

The complete local gate rebuilds the Windows bundle, validates the generated Unity assets and bundle manifest, runs the behavior contracts, and creates a Release package without installing it into the game:

```bash
./tools/test-all.sh
```

For a faster .NET-only check while changing pure contracts, set `SOTF_NEON_DOTNET` to the .NET 6 executable when it is not already available on `PATH`:

```bash
"${SOTF_NEON_DOTNET:-dotnet}" run \
  --project tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
```

Release fails if `unity/SOTFNeonLetters.Assets/Build/AssetBundles/Windows/sotfneonletters` is absent. When present, the build targets copy it beside `Mods/SOTFNeonLetters/manifest.json`; the Release zip contains the same `Mods/SOTFNeonLetters/sotfneonletters` layout.

## References

- [RedLoader](https://github.com/ToniMacaroni/RedLoader)
- [RedLoader documentation](https://tonimacaroni.github.io/RedLoader/)
- [RedLoader templates](https://www.nuget.org/packages/RedLoader.Templates)
- [Signs 1.3.0 by SmokyAce](https://sotf-mods.com/mods/smokyace/signs) — behavior reference; its code and assets are not copied into this project.
