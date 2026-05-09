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
DLL_RELATIVE="bin/Release/net10.0/ResoniteAIBridge.dll"

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

  local dll_path="$MOD_PROJECT_DIR/$DLL_RELATIVE"

  if [[ ! -f "$dll_path" ]]; then
    log "DLL not found at $dll_path; running 'dotnet build -c Release' first."
    have dotnet || die "dotnet SDK is required but not installed. Run scripts/setup.sh first."
    (cd "$MOD_PROJECT_DIR" && dotnet build -c Release)
  fi

  if [[ ! -f "$dll_path" ]]; then
    die "Build completed but DLL still missing: $dll_path"
  fi

  log "Copying $dll_path -> $RESONITE_PLUGIN_DIR/"
  cp -f "$dll_path" "$RESONITE_PLUGIN_DIR/"
  log "Deployed ResoniteAIBridge.dll to $RESONITE_PLUGIN_DIR"
}

main "$@"
