#!/usr/bin/env bash
# scripts/gen_proto.sh
#
# `proto/` 配下の .proto から Python 側のコードを生成する。
#
# 設計方針:
#   - C# 側は csproj の <Protobuf> ItemGroup により `dotnet build` 時に
#     `Grpc.Tools` が自動生成するので、本スクリプトでは扱わない (no-op)。
#   - Python 側のみ `protoc-gen-python_betterproto2` プラグインで生成する。
#     プラグインは uv 管理下の dev 依存に含まれており、`uv run protoc ...`
#     経由で .venv の bin が PATH に乗る前提。
#   - 生成出力は `python/src/resobridge/_generated/` に置き、再生成のたびに
#     一旦削除して空から作り直す (冪等)。
#   - `python/pyproject.toml` がまだ無い段階 (Python implementer が後行する
#     ケース) では skip して exit 0 する。スケルトン構築の中で他者を
#     ブロックしないため。
#
# Usage:
#   scripts/gen_proto.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="gen-proto"
# shellcheck source-path=SCRIPTDIR
# shellcheck source=lib.sh
source "$SCRIPT_DIR/lib.sh"

REPO_ROOT="$(repo_root)"
PROTO_DIR="$REPO_ROOT/proto"
PYTHON_PROJECT="$REPO_ROOT/python"
PYTHON_OUT="$PYTHON_PROJECT/src/resobridge/_generated"

main() {
  log "Generating proto code from: $PROTO_DIR"

  if [[ ! -d "$PROTO_DIR" ]]; then
    die "proto directory not found: $PROTO_DIR"
  fi

  # .proto ファイルの収集 (proto/ root からの相対パス)。
  local -a proto_files=()
  while IFS= read -r -d '' f; do
    proto_files+=("${f#"$PROTO_DIR"/}")
  done < <(find "$PROTO_DIR" -type f -name '*.proto' -print0 | sort -z)

  if [[ ${#proto_files[@]} -eq 0 ]]; then
    warn "No .proto files found under $PROTO_DIR; nothing to generate."
    return 0
  fi

  log "Found ${#proto_files[@]} .proto file(s):"
  printf '        %s\n' "${proto_files[@]}"

  # Python 側 pyproject.toml が無ければ skip (implementer 並走のための猶予)。
  if [[ ! -f "$PYTHON_PROJECT/pyproject.toml" ]]; then
    warn "python/pyproject.toml not present yet; skipping Python code generation."
    log "Re-run this script after the Python skeleton is in place."
    return 0
  fi

  have uv || die "uv is required but not installed. Run scripts/setup.sh first."

  # 出力ディレクトリを毎回 wipe して再生成 (冪等性)。
  log "Cleaning $PYTHON_OUT"
  rm -rf "$PYTHON_OUT"
  mkdir -p "$PYTHON_OUT"

  # generated パッケージは Python パッケージとしてマーカーが必要。
  # pyright を strict で読ませるため、自動生成領域全体を py.typed にする。
  : >"$PYTHON_OUT/__init__.py"
  : >"$PYTHON_OUT/py.typed"

  log "Running protoc with python_betterproto2 plugin (server_generation=async)..."
  (
    cd "$PYTHON_PROJECT"
    uv run --with 'betterproto2[compiler]' -- \
      protoc \
      -I "$PROTO_DIR" \
      --python_betterproto2_out="$PYTHON_OUT" \
      --python_betterproto2_opt=server_generation=async \
      "${proto_files[@]}"
  )

  log "Python code generated under: $PYTHON_OUT"
}

main "$@"
