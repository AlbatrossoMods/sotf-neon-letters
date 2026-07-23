# SOTF Neon Letters

RedLoader/SonsSdk mod for buildable neon English letters.

The current playable checkpoint contains all 26 small cyan letters `A`–`Z`. They are registered as thirteen paired pages in the normal blueprint book and use the standard SonsSdk placement/crafting flow. Every letter costs exactly one Wire (item 418) and one Light Bulb (item 635). Completed letters have a working post-build `E`-key color picker. Medium and large variants and electrical power-grid behavior are separate iterations.

## Multiplayer through SOTFSDK

The host and every client must use exactly the same 0.2.0 DLL and asset bundle. Apply is host-authoritative: a client requests a color change, then the host validates it, applies the accepted color, and broadcasts that state. Current clients receive each accepted color, while late joiners request a snapshot of the current color state.

Multiplayer persistence is host-only. Restore uses the native `ScrewStructure` first; only when a saved letter is missing its native identity may the host use one Bolt fallback spawn. Single Player placement, color editing, and persistence remain available.

Dedicated-server compatibility is not runtime-certified. Real two-peer multiplayer acceptance and save/reload acceptance are still pending and must not be treated as tested.

## Current environment

- Target game: Sons of the Forest installed through Steam/CrossOver.
- Mod loader: RedLoader 0.8.6.
- C# project: RedLoader `sotfmod` template, targeting `net6`.
- Local SDKs: .NET SDK 6.0.428 for the project and .NET SDK 10.0.302 for tooling.
- Template package: `RedLoader.Templates@1.2.7`.
- RedLoader is installed in the detected game directory. The downloaded archive is retained under `.tools/redloader/` and is intentionally excluded from Git.
- BepInEx is not installed in this environment; the project uses the RedLoader stack only.
- Native ARM64 Unity 2022.2.16f1 is installed at `/Applications/Unity/Hub/Editor/2022.2.16f1/Unity.app`, separate from the `Steam` game bottle.
- Windows Build Support (Mono) is installed with Unity. Blender is not installed.

## Unity asset editor

The asset build uses the native Unity editor executable:

```text
/Applications/Unity/Hub/Editor/2022.2.16f1/Unity.app/Contents/MacOS/Unity
```

The editor version can be verified without touching the game bottle:

```bash
"/Applications/Unity/Hub/Editor/2022.2.16f1/Unity.app/Contents/MacOS/Unity" -version
```

## First local build prerequisite

Run the game once through CrossOver after installing RedLoader. On CrossOver/Wine, force the native RedLoader bootstrap DLL with `WINEDLLOVERRIDES=version=n,b`; RedLoader documents this override for Wine environments. RedLoader needs that first launch to generate the game-side assemblies under `_Redloader/Game`, which the generated project references.

The working launch command for the current Steam bottle is:

```bash
"$HOME/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/cxstart" \
  --bottle Steam \
  --no-wait \
  --dll 'version=n,b' \
  'C:\\Program Files (x86)\\Steam\\steam.exe' \
  -applaunch 1326470
```

After the first successful launch, `_Redloader/Latest.log` should contain `Chainloader initialized`, and `_Redloader/Game` should contain the generated interop assemblies.

## Build and test order

The complete local gate rebuilds the Windows bundle, validates the generated Unity assets and bundle manifest, runs the behavior contracts, and creates a Release package without installing it into the game:

```bash
./tools/test-all.sh
```

For a faster .NET-only check while changing pure contracts:

```bash
DOTNET_ROOT="$PWD/.tools/dotnet-6" \
DOTNET_CLI_HOME="$PWD/.tools/dotnet-cli" \
"$PWD/.tools/dotnet-6/dotnet" run \
  --project tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj
```

Release fails if `unity/SOTFNeonLetters.Assets/Build/AssetBundles/Windows/sotfneonletters` is absent. When present, the build targets copy it beside `Mods/SOTFNeonLetters/manifest.json`; the Release zip contains the same `Mods/SOTFNeonLetters/sotfneonletters` layout.

## References

- [RedLoader](https://github.com/ToniMacaroni/RedLoader)
- [RedLoader documentation](https://tonimacaroni.github.io/RedLoader/)
- [RedLoader templates](https://www.nuget.org/packages/RedLoader.Templates)
- [Signs 1.3.0 by SmokyAce](https://sotf-mods.com/mods/smokyace/signs) — behavior reference supplied locally under `$HOME/Downloads/Mods`; its code and assets are not copied into this project.
