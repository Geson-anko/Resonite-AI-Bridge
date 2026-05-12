#!/usr/bin/env bash
# scripts/container-init.sh
#
# /workspace volume の bootstrap スクリプト。コンテナ内 dev ユーザーで実行する想定。
#
# 流れ:
#   1. /source (host repo の ro bind) が空でないことを確認
#   2. /workspace の状態確認 (空 / --force / 引数なし時は中断)
#   3. /source → /workspace に rsync (中間生成物は除外)
#   4. dotnet tool restore で .config/dotnet-tools.json を解決
#   5. python/ で uv sync --all-extras を実行
#   6. (best-effort) pre-commit install --install-hooks
#
# このスクリプトは bind 経由で /source/scripts/container-init.sh として実行されるため、
# rsync 前は /workspace/scripts/lib.sh が存在しない。よって lib.sh は **/source 側**を
# source する (SCRIPT_DIR 自動算出が /source/scripts を指す)。
#
# Usage:
#   /source/scripts/container-init.sh [--force]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="container-init"
# shellcheck source-path=SCRIPTDIR
# shellcheck source=lib.sh
source "$SCRIPT_DIR/lib.sh"

SOURCE_DIR="/source"
WORKSPACE_DIR="/workspace"

FORCE=0
for arg in "$@"; do
  case "$arg" in
    --force | -f) FORCE=1 ;;
    *) die "Unknown argument: $arg (supported: --force)" ;;
  esac
done

# ===== 1. /source が bind されているか確認 ===================================
check_source() {
  [[ -d "$SOURCE_DIR" ]] || die "$SOURCE_DIR が存在しません。host repo を ro bind してください。"
  if [[ -z "$(ls -A "$SOURCE_DIR" 2>/dev/null)" ]]; then
    die "$SOURCE_DIR が空です。docker compose の bind 設定を確認してください。"
  fi
  log "Source repo bind OK: $SOURCE_DIR"
}

# ===== 2. /workspace の状態確認 =============================================
check_workspace() {
  [[ -d "$WORKSPACE_DIR" ]] || die "$WORKSPACE_DIR が存在しません (named volume のマウント設定を確認)。"
  if [[ -z "$(ls -A "$WORKSPACE_DIR" 2>/dev/null)" ]]; then
    log "Workspace は空。bootstrap を続行します。"
    return
  fi
  if [[ "$FORCE" -eq 1 ]]; then
    warn "Workspace に既存ファイルあり。--force 指定のため上書きします (--delete 付き rsync)。"
    return
  fi
  die "$WORKSPACE_DIR に既存ファイルがあります。再 bootstrap するなら --force を付けて再実行してください。"
}

# ===== 3. rsync /source → /workspace ========================================
sync_source() {
  log "Syncing $SOURCE_DIR/ → $WORKSPACE_DIR/ (rsync -a --delete, 中間生成物は除外)"
  rsync -a --delete \
    --exclude='.venv' \
    --exclude='bin' \
    --exclude='obj' \
    --exclude='__pycache__' \
    --exclude='.pytest_cache' \
    --exclude='.ruff_cache' \
    "$SOURCE_DIR/" "$WORKSPACE_DIR/"
  log "rsync done."
}

# ===== 4. dotnet local tools の restore =====================================
restore_dotnet_tools() {
  if [[ ! -f "$WORKSPACE_DIR/.config/dotnet-tools.json" ]]; then
    log ".config/dotnet-tools.json が無いため dotnet tool restore は skip。"
    return
  fi
  have dotnet || die "dotnet が PATH に無い。Dockerfile のインストールを確認してください。"
  log "Restoring .NET local tools..."
  (cd "$WORKSPACE_DIR" && dotnet tool restore)
}

# ===== 5. uv sync ============================================================
sync_python() {
  if [[ ! -f "$WORKSPACE_DIR/python/pyproject.toml" ]]; then
    log "python/pyproject.toml が無いため uv sync は skip。"
    return
  fi
  have uv || die "uv が PATH に無い。Dockerfile のインストールを確認してください。"
  log "Running uv sync --all-extras (python/)..."
  (cd "$WORKSPACE_DIR/python" && uv sync --all-extras)
}

# ===== 6. pre-commit install (best-effort) ==================================
install_pre_commit_hooks() {
  if [[ ! -f "$WORKSPACE_DIR/.pre-commit-config.yaml" ]]; then
    log ".pre-commit-config.yaml が無いため pre-commit install は skip。"
    return
  fi
  if ! have pre-commit; then
    log "pre-commit が未インストール。uv tool install pre-commit を試行 (best-effort)。"
    if ! uv tool install pre-commit; then
      warn "pre-commit のインストールに失敗。hook 登録を skip して続行します。"
      return
    fi
  fi
  log "Installing pre-commit git hooks..."
  if ! (cd "$WORKSPACE_DIR" && pre-commit install --install-hooks); then
    warn "pre-commit install に失敗。hook 登録を skip して続行します。"
    return
  fi
}

main() {
  log "Bootstrapping /workspace from /source (force=$FORCE)"
  check_source
  check_workspace
  sync_source
  restore_dotnet_tools
  sync_python
  install_pre_commit_hooks
  log "Done. 'just --list' で利用可能レシピを確認してください。"
}

main "$@"
