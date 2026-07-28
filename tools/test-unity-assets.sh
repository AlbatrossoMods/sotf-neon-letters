#!/usr/bin/env bash

set -euo pipefail

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"
project_dir="$repo_root/unity/SOTFNeonLetters.Assets"
build_script="$repo_root/tools/build-unity-assets.sh"
test_source="$project_dir/Assets/Editor/NeonAlphabetAssetTests.cs"
test_log="$project_dir/Build/Logs/unity-asset-tests.log"
bundle_path="$project_dir/Build/AssetBundles/Windows/sotfneonletters"
unity_editor_path="${UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/2022.2.16f1/Unity.app/Contents/MacOS/Unity}"
reproducibility_dir="$(mktemp -d "${TMPDIR:-/tmp}/sotf-neon-reproducibility.XXXXXX")"
first_bundle="$reproducibility_dir/sotfneonletters.first"
tracked_input_index="$reproducibility_dir/tracked-input.index"
git_directory="$(git -C "$repo_root" rev-parse --absolute-git-dir)"
cp "$git_directory/index" "$tracked_input_index"
GIT_INDEX_FILE="$tracked_input_index" \
  git -C "$repo_root" add --renormalize .
tracked_input_tree="$(
  GIT_INDEX_FILE="$tracked_input_index" \
    git -C "$repo_root" write-tree
)"

assert_tracked_inputs_unchanged() {
  if ! git -C "$repo_root" diff --quiet "$tracked_input_tree" --; then
    printf 'Unity build modified tracked inputs:\n' >&2
    git -C "$repo_root" diff --name-only "$tracked_input_tree" -- >&2
    fail "Unity asset builds must leave tracked inputs unchanged."
  fi
}

cleanup() {
  rm -rf "$reproducibility_dir"
}

trap cleanup EXIT

[[ -d "$project_dir" ]] || fail "Unity project is missing: $project_dir"
[[ -x "$build_script" ]] || fail "Unity asset build script is missing: $build_script"
[[ -s "$test_source" ]] || fail "Unity asset test entrypoint is missing: $test_source"
[[ -x "$unity_editor_path" ]] || \
  fail "Unity 2022.2.16f1 is missing or not executable: $unity_editor_path"

export SOTF_NEON_UNITY_BUILD_STARTED_EPOCH="$(date -u +%s)"
"$build_script"
[[ -s "$bundle_path" ]] || fail "Unity asset build did not produce a bundle: $bundle_path"
assert_tracked_inputs_unchanged
cp "$bundle_path" "$first_bundle"

"$build_script"
[[ -s "$bundle_path" ]] || fail "Repeated Unity asset build did not produce a bundle: $bundle_path"
assert_tracked_inputs_unchanged
cmp -s "$first_bundle" "$bundle_path" || \
  fail "Two clean Unity asset builds produced different bundle bytes."

printf 'Unity asset bundle reproducibility test passed.\n'

mkdir -p "$(dirname "$test_log")"

if ! "$unity_editor_path" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$project_dir" \
  -buildTarget StandaloneWindows64 \
  -executeMethod SOTFNeonLetters.Editor.NeonAlphabetAssetTests.Run \
  -logFile "$test_log"; then
  fail "Unity asset tests failed. See $test_log"
fi

printf 'Unity asset tests passed. Log: %s\n' "$test_log"
