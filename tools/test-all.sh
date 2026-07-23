#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
local_dotnet="$repo_root/.tools/dotnet-6/dotnet"

if [[ -n "${SOTF_NEON_DOTNET:-}" ]]; then
  dotnet="$SOTF_NEON_DOTNET"
elif [[ -x "$local_dotnet" ]]; then
  dotnet="$local_dotnet"
elif command -v dotnet >/dev/null 2>&1; then
  dotnet="$(command -v dotnet)"
else
  printf 'Error: install .NET SDK 6 or set SOTF_NEON_DOTNET to its executable.\n' >&2
  exit 1
fi

[[ -x "$dotnet" ]] || {
  printf 'Error: .NET executable is not runnable: %s\n' "$dotnet" >&2
  exit 1
}

export DOTNET_CLI_HOME="$repo_root/.tools/dotnet-cli"
export PATH="$(dirname "$dotnet"):$PATH"
export SOTF_NEON_DOTNET="$dotnet"

if [[ "$dotnet" == "$local_dotnet" || -n "${DOTNET_ROOT:-}" ]]; then
  export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$dotnet")}"
fi

contract_arguments=(
  run
  --project "$repo_root/tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj"
)
build_arguments=(
  build "$repo_root/SOTFNeonLetters.csproj"
  --configuration Release
  -p:DisableCopyToGame=True
)
if [[ -n "${SOTF_NEON_GAME_DIR:-}" ]]; then
  contract_arguments+=("-p:GameDir=$SOTF_NEON_GAME_DIR")
  build_arguments+=("-p:GameDir=$SOTF_NEON_GAME_DIR")
fi

"$dotnet" "${contract_arguments[@]}"

"$repo_root/tools/test-unity-assets.sh"

"$dotnet" "${build_arguments[@]}"

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
