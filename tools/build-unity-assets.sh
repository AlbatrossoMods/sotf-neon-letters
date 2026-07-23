#!/usr/bin/env bash

set -euo pipefail

fail() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd "$script_dir/.." && pwd -P)"

project_dir="$repo_root/unity/SOTFNeonLetters.Assets"
source_model="$repo_root/assets/processed/neon-letters/model/NeonLetters_NoBackground.dae"
source_mask="$repo_root/assets/processed/neon-letters/textures/NeonLetters_EmissionMask.png"
extension_model="$repo_root/assets/source/neon-symbols/neon_letters_extended_game_ready.glb"
symbol_inventory="$repo_root/assets/source/neon-symbols/symbol-inventory.json"
symbol_manifest="$repo_root/NeonSymbolManifest.cs"
canonical_generated_snapshot="$project_dir/Canonical/GeneratedAssets.zip"
extension_canonical_snapshot="$project_dir/Canonical/NeonSymbolExtensionGeneratedAssets.zip"
canonical_snapshot_extractor="$repo_root/tools/extract-canonical-unity-assets.py"
generated_source_dir="$project_dir/Assets/GeneratedSource"
shader_graph_settings="$project_dir/ProjectSettings/ShaderGraphSettings.asset"
build_log="$project_dir/Build/Logs/unity-asset-build.log"
bundle_path="$project_dir/Build/AssetBundles/Windows/sotfneonletters"
unity_editor_path="${UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/2022.2.16f1/Unity.app/Contents/MacOS/Unity}"

for required_tool in cmp cp mkdir mktemp python3 rm sed unzip; do
  command -v "$required_tool" >/dev/null 2>&1 || fail "Required tool is unavailable: $required_tool"
done

[[ -s "$source_model" ]] || fail "Canonical DAE is missing or empty: $source_model"
[[ -s "$source_mask" ]] || fail "Canonical emission mask is missing or empty: $source_mask"
[[ -s "$extension_model" ]] || fail "Extension GLB is missing or empty: $extension_model"
[[ -s "$symbol_inventory" ]] || fail "Symbol inventory is missing or empty: $symbol_inventory"
[[ -s "$symbol_manifest" ]] || fail "Shared symbol manifest is missing or empty: $symbol_manifest"
[[ -s "$canonical_generated_snapshot" ]] || \
  fail "Canonical generated-assets snapshot is missing or empty: $canonical_generated_snapshot"
[[ -s "$extension_canonical_snapshot" ]] || \
  fail "Extension canonical metadata snapshot is missing or empty: $extension_canonical_snapshot"
[[ -x "$canonical_snapshot_extractor" ]] || \
  fail "Canonical snapshot extractor is missing or not executable: $canonical_snapshot_extractor"
[[ -d "$project_dir" ]] || fail "Unity project is missing: $project_dir"
[[ -s "$project_dir/ProjectSettings/ProjectVersion.txt" ]] || fail "Unity project version file is missing or empty."
[[ -s "$project_dir/Packages/manifest.json" ]] || fail "Unity package manifest is missing or empty."
[[ -s "$project_dir/Assets/Editor/BuildNeonLetterA.cs" ]] || fail "Unity editor build script is missing or empty."
[[ -s "$shader_graph_settings" ]] || fail "Unity ShaderGraph settings are missing or empty."
[[ -x "$unity_editor_path" ]] || \
  fail "Unity 2022.2.16f1 is missing or not executable: $unity_editor_path"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/sotf-neon-unity-build.XXXXXX")"
shader_graph_backup="$temporary_root/ShaderGraphSettings.asset"
shader_graph_normalized="$temporary_root/ShaderGraphSettings.normalized.asset"
cp -p "$shader_graph_settings" "$shader_graph_backup"

cleanup() {
  rm -f \
    "$project_dir/Assets/Generated 2.meta" \
    "$project_dir/Assets/GeneratedSource 2.meta"
  rm -rf "$temporary_root"
}

trap cleanup EXIT

canonicalize_shader_graph_settings() {
  if cmp -s "$shader_graph_backup" "$shader_graph_settings"; then
    return
  fi

  sed \
    -e 's/^  m_Name: $/  m_Name:/' \
    -e 's/^  m_EditorClassIdentifier: $/  m_EditorClassIdentifier:/' \
    "$shader_graph_settings" > "$shader_graph_normalized"

  if ! cmp -s "$shader_graph_backup" "$shader_graph_normalized"; then
    fail "Unity changed ShaderGraph settings beyond the known whitespace serialization."
  fi

  cp -p "$shader_graph_backup" "$shader_graph_settings"
}

cleanup_targets=(
  "$project_dir/Library"
  "$project_dir/Temp"
  "$project_dir/Logs"
  "$project_dir/Obj"
  "$project_dir/UserSettings"
  "$project_dir/Build"
)

for cleanup_target in "${cleanup_targets[@]}"; do
  [[ "$cleanup_target" != "$project_dir" ]] || \
    fail "Refusing to clean Unity project root: $cleanup_target"
  [[ "$cleanup_target" == "$project_dir/"* ]] || \
    fail "Refusing to clean path outside Unity project: $cleanup_target"
done

for cleanup_target in "${cleanup_targets[@]}"; do
  rm -rf "$cleanup_target"
done

"$canonical_snapshot_extractor" "$canonical_generated_snapshot" "$project_dir"
[[ -s "$generated_source_dir/NeonLetters_NoBackground.dae.meta" ]] || \
  fail "Canonical snapshot did not restore the source model identity."
[[ -s "$project_dir/Assets/Generated/Prefabs/NeonLetter_A_Small.prefab.meta" ]] || \
  fail "Canonical snapshot did not restore generated prefab identities."

python3 - "$extension_canonical_snapshot" "$symbol_inventory" <<'PY'
import json
from pathlib import PurePosixPath
import stat
import sys
import zipfile

archive_path, inventory_path = sys.argv[1:]
with open(inventory_path, encoding="utf-8") as inventory_file:
    inventory = json.load(inventory_file)

asset_keys = [entry["assetKey"] for entry in inventory]
if len(asset_keys) != 54 or len(set(asset_keys)) != 54:
    raise SystemExit("Extension inventory must contain exactly 54 unique asset keys.")

payloads = []
for asset_key in asset_keys:
    payloads.extend(
        (
            f"Assets/Generated/Prefabs/NeonLetter_{asset_key}_Small.prefab",
            f"Assets/Generated/Textures/NeonLetter_{asset_key}_Small_Icon.asset",
        )
    )
payloads.extend(
    f"Assets/Generated/Textures/NeonLetters_Small_Page_{page}.asset"
    for page in range(14, 41)
)

source_metadata = {
    "Assets/GeneratedSource/NeonLetters_Extended.glb.meta",
    "Assets/GeneratedSource/NeonSymbolManifest.cs.meta",
}
expected_entries = sorted(
    source_metadata | set(payloads) | {f"{payload}.meta" for payload in payloads}
)

with zipfile.ZipFile(archive_path) as archive:
    entries = archive.infolist()
    names = [entry.filename for entry in entries]
    if len(names) != len(set(names)):
        raise SystemExit("Extension snapshot contains duplicate entries.")
    if names != expected_entries:
        missing = sorted(set(expected_entries) - set(names))
        unexpected = sorted(set(names) - set(expected_entries))
        raise SystemExit(
            "Extension snapshot entries do not match the exact extension contract. "
            f"Missing: {missing}; unexpected: {unexpected}"
        )
    for entry in entries:
        path = PurePosixPath(entry.filename)
        if path.is_absolute() or any(part in ("", ".", "..") for part in path.parts):
            raise SystemExit(f"Extension snapshot contains an unsafe path: {entry.filename}")
        unix_mode = entry.external_attr >> 16
        if entry.create_system != 3 or not stat.S_ISREG(unix_mode):
            raise SystemExit(f"Extension snapshot entry is not a regular Unix file: {entry.filename}")
        if unix_mode & 0o777 != 0o644:
            raise SystemExit(f"Extension snapshot entry must use mode 0644: {entry.filename}")
        if entry.date_time != (2000, 1, 1, 0, 0, 0):
            raise SystemExit(f"Extension snapshot entry has a non-canonical timestamp: {entry.filename}")
        if entry.extra or entry.comment:
            raise SystemExit(f"Extension snapshot entry contains non-canonical metadata: {entry.filename}")

metadata_count = sum(name.endswith(".meta") for name in names)
payload_count = len(names) - metadata_count
if len(names) != 272 or metadata_count != 137 or payload_count != 135:
    raise SystemExit(
        "Extension snapshot must contain exactly 272 files "
        f"(135 payloads + 137 metadata), found {len(names)} "
        f"({payload_count} payloads + {metadata_count} metadata)."
    )
PY
unzip -qq -o "$extension_canonical_snapshot" -d "$project_dir"
[[ -s "$generated_source_dir/NeonLetters_Extended.glb.meta" ]] || \
  fail "Extension snapshot did not restore the GLB source identity."
[[ -s "$project_dir/Assets/Generated/Prefabs/NeonLetter_CYR_U0410_Small.prefab" ]] || \
  fail "Extension snapshot did not restore generated prefab payloads."
[[ -s "$project_dir/Assets/Generated/Prefabs/NeonLetter_CYR_U0410_Small.prefab.meta" ]] || \
  fail "Extension snapshot did not restore generated prefab identities."

mkdir -p "$generated_source_dir" "$(dirname "$build_log")"
cp "$source_model" "$generated_source_dir/NeonLetters_NoBackground.dae"
cp "$source_mask" "$generated_source_dir/NeonLetters_EmissionMask.png"
cp "$extension_model" "$generated_source_dir/NeonLetters_Extended.glb"
cp "$symbol_manifest" "$generated_source_dir/NeonSymbolManifest.cs"

if ! "$unity_editor_path" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$project_dir" \
  -buildTarget StandaloneWindows64 \
  -executeMethod SOTFNeonLetters.Editor.BuildNeonAlphabet.Build \
  -logFile "$build_log"; then
  canonicalize_shader_graph_settings
  fail "Unity asset build failed. See $build_log"
fi

canonicalize_shader_graph_settings
[[ -s "$bundle_path" ]] || fail "Unity did not produce a non-empty bundle: $bundle_path"

python3 - \
  "$canonical_generated_snapshot" \
  "$extension_canonical_snapshot" \
  "$project_dir" <<'PY'
from pathlib import Path
import sys
import zipfile

legacy_archive_path, extension_archive_path, project_path = sys.argv[1:]
project_root = Path(project_path)

for archive_path in (legacy_archive_path, extension_archive_path):
    stale = []
    with zipfile.ZipFile(archive_path) as archive:
        for entry_name in archive.namelist():
            generated_path = project_root / entry_name
            if not generated_path.is_file():
                stale.append(f"missing generated file: {entry_name}")
                continue
            if generated_path.read_bytes() != archive.read(entry_name):
                stale.append(f"generated bytes differ: {entry_name}")

    if stale:
        details = "\n".join(f"- {message}" for message in stale)
        raise SystemExit(
            f"Canonical snapshot {Path(archive_path).name} is stale relative to "
            f"the current generator:\n{details}"
        )
PY

printf 'Built %s\n' "$bundle_path"
