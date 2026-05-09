#!/usr/bin/env bash
# scripts/deploy_mod.sh
#
# C# mod (`mod/src/ResoniteAIBridge`) を Release ビルドし、生成された .dll を
# Resonite の BepInEx plugins ディレクトリへコピーする。
#
# RESONITE_PLUGIN_DIR を **必須** とする。justfile が `set dotenv-load := true`
# で `.env` から読む想定だが、本スクリプトを直接実行する場合は環境変数で渡す。
#
# Usage:
#   RESONITE_PLUGIN_DIR=/path/to/Resonite/BepInEx/plugins scripts/deploy_mod.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="deploy-mod"
# shellcheck source-path=SCRIPTDIR
# shellcheck source=lib.sh
source "$SCRIPT_DIR/lib.sh"

REPO_ROOT="$(repo_root)"
MOD_PROJECT_DIR="$REPO_ROOT/mod/src/ResoniteAIBridge"
# Remora.Resonite.Sdk は build 出力を `bin/<Configuration>/mod/client/BepInEx/plugins/<AssemblyName>/`
# 配下に再配置する。TFM ディレクトリ (net10.0) には素の dll が出ない点に注意。
BUILD_OUTPUT_GLOB_DIR="$MOD_PROJECT_DIR/bin/Release"
DLL_NAME="ResoniteAIBridge.dll"

main() {
  if [[ -z "${RESONITE_PLUGIN_DIR:-}" ]]; then
    die "RESONITE_PLUGIN_DIR is not set; copy .env.example to .env and edit, or export it explicitly."
  fi

  if [[ ! -d "$RESONITE_PLUGIN_DIR" ]]; then
    die "RESONITE_PLUGIN_DIR does not exist: $RESONITE_PLUGIN_DIR"
  fi

  if [[ ! -d "$MOD_PROJECT_DIR" ]]; then
    die "mod project directory not found: $MOD_PROJECT_DIR (mod skeleton not yet created?)"
  fi

  # Remora SDK の出力配置を find で探す。`*/plugins/*` で絞り込み、TFM の差や
  # 将来の出力先変更に追随する。
  local dll_path
  dll_path="$(find "$BUILD_OUTPUT_GLOB_DIR" -type f -name "$DLL_NAME" -path '*/plugins/*' 2>/dev/null | head -n 1 || true)"

  if [[ -z "$dll_path" ]]; then
    log "DLL not found under $BUILD_OUTPUT_GLOB_DIR; running 'dotnet build -c Release' first."
    have dotnet || die "dotnet SDK is required but not installed. Run scripts/setup.sh first."
    (cd "$MOD_PROJECT_DIR" && dotnet build -c Release)
    dll_path="$(find "$BUILD_OUTPUT_GLOB_DIR" -type f -name "$DLL_NAME" -path '*/plugins/*' 2>/dev/null | head -n 1 || true)"
  fi

  if [[ -z "$dll_path" || ! -f "$dll_path" ]]; then
    die "Build completed but DLL still missing under $BUILD_OUTPUT_GLOB_DIR (expected '*/plugins/$DLL_NAME')."
  fi

  log "Resolved DLL: $dll_path"
  log "Copying -> $RESONITE_PLUGIN_DIR/"
  cp -f "$dll_path" "$RESONITE_PLUGIN_DIR/"
  log "Deployed $DLL_NAME to $RESONITE_PLUGIN_DIR"
}

main "$@"
