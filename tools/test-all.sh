#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
dotnet="$repo_root/.tools/dotnet-6/dotnet"

export DOTNET_ROOT="$repo_root/.tools/dotnet-6"
export DOTNET_CLI_HOME="$repo_root/.tools/dotnet-cli"

"$dotnet" run \
  --project "$repo_root/tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj"

"$repo_root/tools/test-unity-assets.sh"

"$dotnet" build "$repo_root/SOTFNeonLetters.csproj" \
  --configuration Release \
  -p:DisableCopyToGame=True

"$dotnet" run \
  --project "$repo_root/tests/SOTFNeonLetters.ReleaseTests/SOTFNeonLetters.ReleaseTests.csproj" \
  -- \
  "$repo_root/bin/Release/net6/SOTFNeonLetters.dll" \
  "$repo_root/manifest.json" \
  "$repo_root/README.md" \
  "$repo_root/unity/SOTFNeonLetters.Assets/Build/AssetBundles/Windows/sotfneonletters" \
  "$repo_root/ReleaseBuild/SOTFNeonLetters.zip"

if [[ "${SOTF_NEON_COLD_RELEASE_ACTIVE:-}" != "1" ]]; then
  "$repo_root/tools/test-clean-release-gate.sh"
fi

printf 'All SOTF Neon Letters test gates passed.\n'
