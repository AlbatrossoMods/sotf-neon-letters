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

temp_parent="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
temp_root="$(mktemp -d "$temp_parent/sotf-neon-build-order.XXXXXX")"
workspace="$temp_root/workspace"
properties_target="$temp_root/ReportBuildOrderProperties.targets"

cleanup() {
  case "$temp_root" in
    "$temp_parent"/sotf-neon-build-order.*)
      rm -rf "$temp_root"
      ;;
    *)
      printf 'Error: refusing to clean unexpected temporary path: %s\n' "$temp_root" >&2
      return 1
      ;;
  esac
}
trap cleanup EXIT

mkdir -p "$workspace"

root_sources=("$repo_root"/*.cs)
cp "${root_sources[@]}" "$workspace/"
cp \
  "$repo_root/SOTFNeonLetters.csproj" \
  "$repo_root/SOTFNeonLetters.Core.csproj" \
  "$repo_root/Directory.Build.targets" \
  "$repo_root/manifest.json" \
  "$workspace/"

for optional_build_file in Directory.Build.props SOTFNeonLetters.csproj.user; do
  if [[ -f "$repo_root/$optional_build_file" ]]; then
    cp "$repo_root/$optional_build_file" "$workspace/"
  fi
done

cat >"$properties_target" <<'EOF'
<Project>
  <Target Name="ReportBuildOrderProperties">
    <WriteLinesToFile
      File="$(BuildOrderPropertiesFile)"
      Lines="BaseIntermediateOutputPath=$(BaseIntermediateOutputPath)"
      Overwrite="true" />
    <WriteLinesToFile
      File="$(BuildOrderPropertiesFile)"
      Lines="MSBuildProjectExtensionsPath=$(MSBuildProjectExtensionsPath)"
      Overwrite="false" />
    <WriteLinesToFile
      File="$(BuildOrderPropertiesFile)"
      Lines="OutputPath=$(OutputPath)"
      Overwrite="false" />
    <WriteLinesToFile
      File="$(BuildOrderPropertiesFile)"
      Lines="TargetPath=$(TargetPath)"
      Overwrite="false" />
  </Target>
</Project>
EOF

export DOTNET_CLI_HOME="$temp_root/dotnet-cli"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$(dirname "$dotnet"):$PATH"

if [[ "$dotnet" == "$local_dotnet" || -n "${DOTNET_ROOT:-}" ]]; then
  export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$dotnet")}"
fi

msbuild_arguments=("-p:DisableCopyToGame=True")
if [[ -n "${SOTF_NEON_GAME_DIR:-}" ]]; then
  msbuild_arguments+=("-p:GameDir=$SOTF_NEON_GAME_DIR")
fi

read_property() {
  local properties_file="$1"
  local property_name="$2"

  sed -n "s/^${property_name}=//p" "$properties_file"
}

report_properties() {
  local project="$1"
  local properties_file="$2"

  "$dotnet" msbuild "$workspace/$project" \
    -nologo \
    -t:ReportBuildOrderProperties \
    -p:CustomAfterMicrosoftCommonTargets="$properties_target" \
    -p:BuildOrderPropertiesFile="$properties_file" \
    -p:Configuration=Debug \
    "${msbuild_arguments[@]}"
}

assert_equals() {
  local description="$1"
  local expected="$2"
  local actual="$3"

  if [[ "$actual" != "$expected" ]]; then
    printf 'Error: %s changed. Expected "%s", got "%s".\n' \
      "$description" \
      "$expected" \
      "$actual" >&2
    exit 1
  fi
}

main_properties="$temp_root/main.properties"
core_properties="$temp_root/core.properties"
report_properties "SOTFNeonLetters.csproj" "$main_properties"
report_properties "SOTFNeonLetters.Core.csproj" "$core_properties"

main_intermediate="$(read_property "$main_properties" "BaseIntermediateOutputPath")"
core_intermediate="$(read_property "$core_properties" "BaseIntermediateOutputPath")"
main_extensions="$(read_property "$main_properties" "MSBuildProjectExtensionsPath")"
core_extensions="$(read_property "$core_properties" "MSBuildProjectExtensionsPath")"
main_output="$(read_property "$main_properties" "OutputPath")"
core_output="$(read_property "$core_properties" "OutputPath")"
main_target="$(read_property "$main_properties" "TargetPath")"
core_target="$(read_property "$core_properties" "TargetPath")"

assert_equals "SOTFNeonLetters OutputPath" "bin/Debug/net6/" "$main_output"
assert_equals "SOTFNeonLetters TargetPath" \
  "$workspace/bin/Debug/net6/SOTFNeonLetters.dll" \
  "$main_target"
assert_equals "SOTFNeonLetters.Core OutputPath" "bin/Debug/net6.0/" "$core_output"
assert_equals "SOTFNeonLetters.Core TargetPath" \
  "$workspace/bin/Debug/net6.0/SOTFNeonLetters.Core.dll" \
  "$core_target"

reset_generated_state() {
  case "$workspace" in
    "$temp_root"/workspace)
      rm -rf "$workspace/obj" "$workspace/bin"
      ;;
    *)
      printf 'Error: refusing to clean unexpected workspace path: %s\n' "$workspace" >&2
      exit 1
      ;;
  esac
}

run_build_order() {
  local first_project="$1"
  local second_project="$2"
  local order_name="$3"

  reset_generated_state
  printf 'Testing build order: %s\n' "$order_name"

  "$dotnet" restore "$workspace/$first_project" \
    --force \
    --nologo \
    "${msbuild_arguments[@]}"
  "$dotnet" restore "$workspace/$second_project" \
    --force \
    --nologo \
    "${msbuild_arguments[@]}"

  "$dotnet" build "$workspace/$first_project" \
    --configuration Debug \
    --no-restore \
    --nologo \
    "${msbuild_arguments[@]}"
  "$dotnet" build "$workspace/$second_project" \
    --configuration Debug \
    --no-restore \
    --nologo \
    "${msbuild_arguments[@]}"
}

run_build_order \
  "SOTFNeonLetters.Core.csproj" \
  "SOTFNeonLetters.csproj" \
  "Core -> main"
run_build_order \
  "SOTFNeonLetters.csproj" \
  "SOTFNeonLetters.Core.csproj" \
  "main -> Core"

assert_equals \
  "SOTFNeonLetters BaseIntermediateOutputPath" \
  "obj/SOTFNeonLetters/" \
  "$main_intermediate"
assert_equals \
  "SOTFNeonLetters.Core BaseIntermediateOutputPath" \
  "obj/SOTFNeonLetters.Core/" \
  "$core_intermediate"
assert_equals \
  "SOTFNeonLetters MSBuildProjectExtensionsPath" \
  "$workspace/obj/SOTFNeonLetters/" \
  "$main_extensions"
assert_equals \
  "SOTFNeonLetters.Core MSBuildProjectExtensionsPath" \
  "$workspace/obj/SOTFNeonLetters.Core/" \
  "$core_extensions"

printf 'Build-order regression gate passed.\n'
