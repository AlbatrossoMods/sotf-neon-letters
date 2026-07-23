#!/usr/bin/env bash

set -euo pipefail

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
isolated_root="$(mktemp -d "${TMPDIR:-/tmp}/sotf-neon-clean-release.XXXXXX")"
tracked_checkout="$isolated_root/checkout"
expected_zip="$isolated_root/SOTFNeonLetters.expected.zip"
actual_zip="$tracked_checkout/ReleaseBuild/SOTFNeonLetters.zip"
index_tree_file="$isolated_root/index-tree"
dotnet_executable="${SOTF_NEON_DOTNET:-$repo_root/.tools/dotnet-6/dotnet}"
dotnet_toolchain="$(dirname "$dotnet_executable")"
local_game_settings="$repo_root/SOTFNeonLetters.csproj.user"

cleanup() {
  rm -rf "$isolated_root"
}

trap cleanup EXIT

for required_tool in awk cmp find git ln mkdir mktemp rm shasum tar; do
  command -v "$required_tool" >/dev/null 2>&1 || \
    fail "Required tool is unavailable: $required_tool"
done

[[ -x "$dotnet_executable" ]] || \
  fail "A runnable .NET SDK executable is required; set SOTF_NEON_DOTNET or install $repo_root/.tools/dotnet-6/dotnet"

if [[ -n "${SOTF_NEON_GAME_DIR:-}" ]]; then
  game_dir="$SOTF_NEON_GAME_DIR"
elif [[ -f "$local_game_settings" ]]; then
  game_dir="$(awk -F '[<>]' '/<GameDir>/ { print $3; exit }' "$local_game_settings")"
else
  fail "Set SOTF_NEON_GAME_DIR or create SOTFNeonLetters.csproj.user from the provided template."
fi

[[ -d "$game_dir" ]] || \
  fail "Configured game directory does not exist: $game_dir"

mkdir -p "$tracked_checkout"
git -C "$repo_root" write-tree > "$index_tree_file"
read -r index_tree < "$index_tree_file"
git -C "$repo_root" archive --format=tar "$index_tree" | \
  tar -xf - -C "$tracked_checkout"

cp -p "$tracked_checkout/ReleaseBuild/SOTFNeonLetters.zip" "$expected_zip"

git -C "$tracked_checkout" init -q
git -C "$tracked_checkout" add -f .

compiled_output="$(
  find "$tracked_checkout" -type d \( -name bin -o -name obj \) -print -quit
)"
[[ -z "$compiled_output" ]] || \
  fail "Isolated tracked-input checkout contains compiled output: $compiled_output"
printf 'Cold tracked-input checkout contains no bin/obj directories.\n'

mkdir -p "$tracked_checkout/.tools/dotnet-cli"
ln -s "$dotnet_toolchain" "$tracked_checkout/.tools/dotnet-6"

SOTF_NEON_COLD_RELEASE_ACTIVE=1 \
SOTF_NEON_DOTNET="$dotnet_executable" \
SOTF_NEON_GAME_DIR="$game_dir" \
"$tracked_checkout/tools/test-all.sh"

[[ -s "$actual_zip" ]] || fail "Cold full release gate produced no release ZIP."
if ! cmp -s "$expected_zip" "$actual_zip"; then
  printf 'Committed and cold-gate release ZIP hashes:\n' >&2
  shasum -a 256 "$expected_zip" "$actual_zip" >&2
  fail "Cold full release gate produced different release ZIP bytes."
fi
printf 'Cold release ZIP SHA-256: '
shasum -a 256 "$actual_zip" | awk '{print $1}'

if ! git -C "$tracked_checkout" diff --quiet --; then
  printf 'Cold full release gate modified tracked inputs:\n' >&2
  git -C "$tracked_checkout" diff --name-only -- >&2
  fail "Cold full release gate must leave tracked inputs unchanged."
fi

printf 'Cold tracked-input full release gate passed.\n'
