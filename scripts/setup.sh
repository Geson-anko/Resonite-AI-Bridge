#!/usr/bin/env bash
# scripts/setup.sh
#
# resonite-ai-bridge の開発環境を「任意の Linux ディストリ」で一発構築するスクリプト。
# 公式バイナリインストーラを優先し、ディストロ依存のパッケージマネージャは
# git / curl / unzip など最低限のシステム前提のみに留める。
#
# 冪等性: 全ステップ再実行可能。既に入っているコンポーネントは skip する。
#
# Usage:
#   scripts/setup.sh
#
# 環境変数で上書き可能:
#   DOTNET_CHANNEL          .NET SDK のチャネル (default: 10.0)
#   DOTNET_INSTALL_DIR      .NET SDK のインストール先 (default: ~/.dotnet)
#   PROTOC_VERSION          protoc のバージョン (default: 29.3)
#   PROTOC_INSTALL_DIR      protoc の prefix (default: ~/.local) → bin/protoc が置かれる
#   JUST_INSTALL_DIR        just のインストール先 (default: ~/.local/bin)
#   SKIP_SYSTEM_PREREQS=1   sudo を使う system package インストールを skip

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="setup"
# shellcheck source-path=SCRIPTDIR
# shellcheck source=lib.sh
source "$SCRIPT_DIR/lib.sh"

# ===== Configuration =========================================================
DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"
DOTNET_INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
PROTOC_VERSION="${PROTOC_VERSION:-29.3}"
PROTOC_INSTALL_DIR="${PROTOC_INSTALL_DIR:-$HOME/.local}"
JUST_INSTALL_DIR="${JUST_INSTALL_DIR:-$HOME/.local/bin}"

REPO_ROOT="$(repo_root)"

# ===== 1. System prerequisites (minimal, distro-detected) ====================
install_system_prereqs() {
  if [[ "${SKIP_SYSTEM_PREREQS:-0}" == "1" ]]; then
    log "SKIP_SYSTEM_PREREQS=1 → skipping system package install."
    return
  fi

  local missing=()
  for cmd in git curl unzip tar; do
    have "$cmd" || missing+=("$cmd")
  done

  if [[ ${#missing[@]} -eq 0 ]]; then
    log "System prereqs (git, curl, unzip, tar) already present."
    return
  fi

  local distro
  distro=$(detect_distro)
  log "Installing system prereqs: ${missing[*]} (distro: $distro)"

  local sudo_cmd
  sudo_cmd=$(sudo_prefix)

  case "$distro" in
    debian)
      $sudo_cmd apt-get update
      $sudo_cmd apt-get install -y --no-install-recommends "${missing[@]}" ca-certificates
      ;;
    fedora)
      $sudo_cmd dnf install -y "${missing[@]}" ca-certificates
      ;;
    arch)
      $sudo_cmd pacman -Sy --needed --noconfirm "${missing[@]}" ca-certificates
      ;;
    suse)
      $sudo_cmd zypper --non-interactive install "${missing[@]}" ca-certificates
      ;;
    *)
      die "Unknown distro. Please install manually: ${missing[*]}"
      ;;
  esac
}

# ===== 2. uv (Python package manager) ========================================
install_uv() {
  if have uv; then
    log "uv already installed: $(uv --version)"
    return
  fi
  log "Installing uv via official installer..."
  curl -LsSf https://astral.sh/uv/install.sh | sh
  ensure_local_bin_on_path
  have uv || die "uv installation failed."
  log "uv installed: $(uv --version)"
}

# ===== 3. just (task runner) =================================================
install_just() {
  if have just; then
    log "just already installed: $(just --version)"
    return
  fi
  log "Installing just via official installer to $JUST_INSTALL_DIR..."
  mkdir -p "$JUST_INSTALL_DIR"
  curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh \
    | bash -s -- --to "$JUST_INSTALL_DIR"
  ensure_local_bin_on_path
  have just || die "just installation failed."
  log "just installed: $(just --version)"
}

# ===== 4. .NET SDK ===========================================================
install_dotnet() {
  if have dotnet && dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL%.*}\."; then
    log ".NET SDK channel ${DOTNET_CHANNEL} already installed:"
    dotnet --list-sdks | sed 's/^/        /'
    return
  fi
  log "Installing .NET SDK channel ${DOTNET_CHANNEL} into ${DOTNET_INSTALL_DIR}..."
  local installer="/tmp/dotnet-install.sh"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  chmod +x "$installer"
  "$installer" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_INSTALL_DIR"
  rm -f "$installer"

  # `$DOTNET_INSTALL_DIR` は install-time に展開して rc に書き、`$PATH` のみ
  # 起動時展開させる ($DOTNET_INSTALL_DIR は呼び出し側 shell で必ず set されているとは限らないため)。
  add_to_shell_profile "export DOTNET_ROOT=\"$DOTNET_INSTALL_DIR\""
  add_to_shell_profile "export PATH=\"$DOTNET_INSTALL_DIR:$DOTNET_INSTALL_DIR/tools:\$PATH\""

  export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
  export PATH="$DOTNET_INSTALL_DIR:$DOTNET_INSTALL_DIR/tools:$PATH"
  have dotnet || die ".NET SDK installation failed."
  log ".NET SDK installed: $(dotnet --version)"
}

# ===== 5. protoc =============================================================
install_protoc() {
  if have protoc; then
    local current
    current=$(protoc --version 2>/dev/null | awk '{print $2}')
    if [[ -n "$current" ]]; then
      log "protoc already installed: ${current}"
      return
    fi
  fi
  local arch
  case "$(uname -m)" in
    x86_64) arch="x86_64" ;;
    aarch64 | arm64) arch="aarch_64" ;;
    *) die "Unsupported architecture for protoc binary: $(uname -m)" ;;
  esac
  log "Installing protoc ${PROTOC_VERSION} (linux-${arch}) into ${PROTOC_INSTALL_DIR}..."
  local zip="/tmp/protoc-${PROTOC_VERSION}-${arch}.zip"
  curl -fsSL \
    "https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VERSION}/protoc-${PROTOC_VERSION}-linux-${arch}.zip" \
    -o "$zip"
  mkdir -p "$PROTOC_INSTALL_DIR"
  unzip -oq "$zip" -d "$PROTOC_INSTALL_DIR"
  rm -f "$zip"
  ensure_local_bin_on_path
  have protoc || die "protoc installation failed (is ${PROTOC_INSTALL_DIR}/bin on PATH?)."
  log "protoc installed: $(protoc --version)"
}

# ===== 6. dotnet local tools (csharpier ほか) =================================
# csharpier はリポジトリ内 `.config/dotnet-tools.json` に local tool として固定
# されている。`dotnet tool restore` が manifest を読んで `.dotnet/toolResolverCache`
# 経由で解決するため、global tools (`-g`) や PATH 操作は不要。
restore_dotnet_tools() {
  if [[ ! -f "$REPO_ROOT/.config/dotnet-tools.json" ]]; then
    log ".config/dotnet-tools.json not present; skipping local tool restore."
    return
  fi
  log "Restoring .NET local tools from .config/dotnet-tools.json..."
  (cd "$REPO_ROOT" && dotnet tool restore)
}

# ===== 7. pre-commit (uv tool) ===============================================
install_pre_commit() {
  if have pre-commit; then
    log "pre-commit already installed: $(pre-commit --version)"
  else
    log "Installing pre-commit via uv tool..."
    uv tool install pre-commit
  fi
  ensure_local_bin_on_path

  if [[ -f "$REPO_ROOT/.pre-commit-config.yaml" ]]; then
    log "Installing pre-commit git hooks..."
    (cd "$REPO_ROOT" && pre-commit install --install-hooks)
  else
    log "No .pre-commit-config.yaml yet. Skipping hook installation (re-run after creating it)."
  fi
}

# ===== 8. Python project deps ================================================
sync_python_project() {
  if [[ -f "$REPO_ROOT/python/pyproject.toml" ]]; then
    log "Syncing Python deps via uv (python/)..."
    (cd "$REPO_ROOT/python" && uv sync --all-extras)
  else
    log "python/pyproject.toml not yet present. Skipping uv sync."
  fi
}

# ===== Main ==================================================================
main() {
  require_linux
  log "Setting up resonite-ai-bridge dev environment."
  log "Repo root: $REPO_ROOT"

  install_system_prereqs
  install_uv
  install_just
  install_dotnet
  install_protoc
  restore_dotnet_tools
  install_pre_commit
  sync_python_project

  log "Done."
  log "If your shell PATH was updated, run: source ~/.bashrc  (or restart your shell)."
}

main "$@"
