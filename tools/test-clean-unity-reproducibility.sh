#!/usr/bin/env bash

set -euo pipefail

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
isolated_root="$(mktemp -d "${TMPDIR:-/tmp}/sotf-neon-clean-checkout.XXXXXX")"
tracked_checkout="$isolated_root/checkout"
expected_bundle="$isolated_root/sotfneonletters.expected"
actual_bundle="$tracked_checkout/unity/SOTFNeonLetters.Assets/Build/AssetBundles/Windows/sotfneonletters"
tracked_generated="$tracked_checkout/unity/SOTFNeonLetters.Assets/Assets/Generated"
tracked_generated_source="$tracked_checkout/unity/SOTFNeonLetters.Assets/Assets/GeneratedSource"

cleanup() {
  rm -rf "$isolated_root"
}

trap cleanup EXIT

for required_tool in cmp cp git mkdir mktemp rm shasum unzip; do
  command -v "$required_tool" >/dev/null 2>&1 || \
    fail "Required tool is unavailable: $required_tool"
done

mkdir -p "$tracked_checkout"
while IFS= read -r -d '' tracked_path; do
  source_path="$repo_root/$tracked_path"
  destination_path="$tracked_checkout/$tracked_path"
  [[ -f "$source_path" ]] || fail "Tracked input is missing: $tracked_path"
  mkdir -p "$(dirname "$destination_path")"
  cp -p "$source_path" "$destination_path"
done < <(git -C "$repo_root" ls-files -z)

[[ ! -e "$tracked_generated" ]] || \
  fail "Isolated checkout unexpectedly contains ignored Assets/Generated state."
[[ ! -e "$tracked_generated_source" ]] || \
  fail "Isolated checkout unexpectedly contains ignored Assets/GeneratedSource state."

unzip -p \
  "$tracked_checkout/ReleaseBuild/SOTFNeonLetters.zip" \
  Mods/SOTFNeonLetters/sotfneonletters \
  > "$expected_bundle"
[[ -s "$expected_bundle" ]] || fail "Tracked release ZIP contains no asset bundle."

"$tracked_checkout/tools/build-unity-assets.sh"
[[ -s "$actual_bundle" ]] || fail "Isolated tracked-input build produced no asset bundle."

if ! cmp -s "$expected_bundle" "$actual_bundle"; then
  printf 'Canonical and isolated bundle hashes:\n' >&2
  shasum -a 256 "$expected_bundle" "$actual_bundle" >&2
  fail "A clean tracked-input checkout produced different Unity bundle bytes."
fi

printf 'Clean tracked-input Unity reproducibility test passed.\n'
