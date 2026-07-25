#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
test_project_dir="$repo_root/tests/SOTFNeonLetters.ContractTests"
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

"$dotnet" tool restore

pushd "$test_project_dir" >/dev/null
"$dotnet" tool run dotnet-stryker -- --config-file stryker-config.json
popd >/dev/null
