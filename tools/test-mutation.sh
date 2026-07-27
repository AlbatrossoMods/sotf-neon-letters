#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
test_project_dir="$repo_root/tests/SOTFNeonLetters.ContractTests"
local_dotnet="$repo_root/.tools/dotnet-6/dotnet"
local_game_settings="$repo_root/SOTFNeonLetters.csproj.user"

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

if [[ -n "${SOTF_NEON_GAME_DIR:-}" ]]; then
  game_dir="$SOTF_NEON_GAME_DIR"
elif [[ -f "$local_game_settings" ]]; then
  game_dir="$(awk -F '[<>]' '/<GameDir>/ { print $3; exit }' "$local_game_settings")"
else
  printf 'Error: set SOTF_NEON_GAME_DIR or create SOTFNeonLetters.csproj.user from the provided template.\n' >&2
  exit 1
fi

[[ -n "$game_dir" ]] || {
  printf 'Error: set SOTF_NEON_GAME_DIR or create SOTFNeonLetters.csproj.user from the provided template.\n' >&2
  exit 1
}
[[ -d "$game_dir" ]] || {
  printf 'Error: configured game directory does not exist: %s\n' "$game_dir" >&2
  exit 1
}

# Stryker 3.10 has no arbitrary MSBuild-property option. MSBuild imports
# valid environment variable names as initial properties for its inner build.
export GameDir="$game_dir"

"$dotnet" tool restore

pushd "$test_project_dir" >/dev/null
"$dotnet" tool run dotnet-stryker -- --config-file stryker-config.json
popd >/dev/null
