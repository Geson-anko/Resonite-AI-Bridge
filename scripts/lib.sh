# scripts/lib.sh
#
# resonite-ai-bridge の shell scripts で共有する汎用ユーティリティ。
# 各 script はファイル先頭で:
#
#   SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
#   SCRIPT_NAME="<your script name>"
#   # shellcheck source=lib.sh
#   source "$SCRIPT_DIR/lib.sh"
#
# のようにロードする。
#
# 注意:
#   - `set -euo pipefail` は呼び出し側で設定すること (lib.sh では設定しない)。
#   - lib.sh は **source 専用**。直接実行しないこと。

# shebang は付けない (source されるため)。直接実行されたら停止する。
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  printf 'lib.sh is meant to be sourced, not executed.\n' >&2
  exit 1
fi

# ===== Logging ===============================================================
# `SCRIPT_NAME` が呼び出し側で設定されていればプレフィクスに使う。未設定なら "script"。
log()  { printf '\n\033[1;36m[%s]\033[0m %s\n' "${SCRIPT_NAME:-script}" "$*"; }
warn() { printf '\033[1;33m[%s:warn]\033[0m %s\n' "${SCRIPT_NAME:-script}" "$*" >&2; }
die()  { printf '\033[1;31m[%s:err]\033[0m %s\n' "${SCRIPT_NAME:-script}" "$*" >&2; exit 1; }

# ===== Command / OS detection ================================================
have() { command -v "$1" >/dev/null 2>&1; }

require_linux() {
  [[ "$(uname -s)" == "Linux" ]] || die "This script supports Linux only (got $(uname -s))."
}

# Echo one of: debian / fedora / arch / suse / unknown
detect_distro() {
  if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    case " ${ID_LIKE:-} ${ID:-} " in
      *' debian '*|*' ubuntu '*) echo debian ;;
      *' fedora '*|*' rhel '*|*' centos '*) echo fedora ;;
      *' arch '*) echo arch ;;
      *' suse '*|*' opensuse '*) echo suse ;;
      *) echo unknown ;;
    esac
  else
    echo unknown
  fi
}

# Echo "sudo" if not running as root, otherwise empty.
sudo_prefix() {
  if [[ ${EUID:-$(id -u)} -eq 0 ]]; then echo ""; else echo "sudo"; fi
}

# ===== Shell profile / PATH management =======================================
# Append a single line to the user's shell rc file if not already present.
# Choice of file follows $SHELL (zsh → ~/.zshrc, bash → ~/.bashrc, else ~/.profile).
add_to_shell_profile() {
  local line="$1"
  local profile
  case "${SHELL:-}" in
    */zsh)  profile="$HOME/.zshrc" ;;
    */bash) profile="$HOME/.bashrc" ;;
    *)      profile="$HOME/.profile" ;;
  esac
  touch "$profile"
  if ! grep -qsF -- "$line" "$profile"; then
    printf '%s\n' "$line" >> "$profile"
    log "Added to $profile: $line"
  fi
}

# Ensure ~/.local/bin is on PATH for both the current shell and future shells.
ensure_local_bin_on_path() {
  case ":$PATH:" in
    *":$HOME/.local/bin:"*) ;;
    *)
      add_to_shell_profile 'export PATH="$HOME/.local/bin:$PATH"'
      export PATH="$HOME/.local/bin:$PATH"
      ;;
  esac
}

# ===== Repository root =======================================================
# lib.sh が `<repo>/scripts/lib.sh` に置かれている前提で、repo root を echo する。
repo_root() {
  cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd
}
