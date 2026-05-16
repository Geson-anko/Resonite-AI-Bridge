#!/usr/bin/env bash
# scripts/container-init.sh
#
# container 内 dev ユーザーで実行する冪等な deps restore レシピ。
#
# 流れ:
#   1. dotnet tool restore で .config/dotnet-tools.json を解決
#   2. python/ で uv sync --all-extras を実行
#   3. (best-effort) pre-commit install --install-hooks
#   4. ~/.claude/settings.json を /workspace/.claude/settings.container.json への symlink にする
#
# Usage:
#   scripts/container-init.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="container-init"
# shellcheck source-path=SCRIPTDIR
# shellcheck source=lib.sh
source "$SCRIPT_DIR/lib.sh"

WORKSPACE_DIR="/workspace"

if [[ $# -gt 0 ]]; then
  die "Unknown argument(s): $* (本レシピは引数を取りません)"
fi

# ===== 1. dotnet local tools の restore =====================================
restore_dotnet_tools() {
  if [[ ! -f "$WORKSPACE_DIR/.config/dotnet-tools.json" ]]; then
    log ".config/dotnet-tools.json が無いため dotnet tool restore は skip。"
    return
  fi
  have dotnet || die "dotnet が PATH に無い。Dockerfile のインストールを確認してください。"
  log "Restoring .NET local tools..."
  (cd "$WORKSPACE_DIR" && dotnet tool restore)
}

# ===== 2. uv sync ============================================================
sync_python() {
  if [[ ! -f "$WORKSPACE_DIR/python/pyproject.toml" ]]; then
    log "python/pyproject.toml が無いため uv sync は skip。"
    return
  fi
  have uv || die "uv が PATH に無い。Dockerfile のインストールを確認してください。"
  log "Running uv sync --all-extras (python/)..."
  (cd "$WORKSPACE_DIR/python" && uv sync --all-extras)
}

# ===== 3. pre-commit install (best-effort) ==================================
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

# ===== 4. ~/.claude/settings.json を repo の container 用 settings に symlink =====
link_claude_settings() {
  local target="$WORKSPACE_DIR/.claude/settings.container.json"
  local link="$HOME/.claude/settings.json"
  if [[ ! -f "$target" ]]; then
    warn "$target が無いため Claude Code 設定の symlink は skip。"
    return
  fi
  mkdir -p "$(dirname "$link")"
  ln -sfn "$target" "$link"
  log "Linked $link → $target"
}

main() {
  log "Resolving dependencies in $WORKSPACE_DIR"
  restore_dotnet_tools
  sync_python
  install_pre_commit_hooks
  link_claude_settings
  log "Done. 'just --list' で利用可能レシピを確認してください。"
}

main "$@"
