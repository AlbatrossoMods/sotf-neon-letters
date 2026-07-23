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
dotnet_toolchain="$repo_root/.tools/dotnet-6"

cleanup() {
  rm -rf "$isolated_root"
}

trap cleanup EXIT

for required_tool in awk cmp cp find git ln mkdir mktemp rm shasum; do
  command -v "$required_tool" >/dev/null 2>&1 || \
    fail "Required tool is unavailable: $required_tool"
done

[[ -x "$dotnet_toolchain/dotnet" ]] || \
  fail "Repository-local .NET 6 toolchain is missing: $dotnet_toolchain/dotnet"

mkdir -p "$tracked_checkout"
while IFS= read -r -d '' tracked_path; do
  source_path="$repo_root/$tracked_path"
  destination_path="$tracked_checkout/$tracked_path"
  [[ -f "$source_path" ]] || fail "Tracked input is missing: $tracked_path"
  mkdir -p "$(dirname "$destination_path")"
  cp -p "$source_path" "$destination_path"
done < <(git -C "$repo_root" ls-files -z)

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

SOTF_NEON_COLD_RELEASE_ACTIVE=1 "$tracked_checkout/tools/test-all.sh"

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
