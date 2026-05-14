set dotenv-load := true
set shell := ["bash", "-c"]

# 既定で help を出す。
default:
    @just --list

# ===== 環境構築 =========================================================

# proto から Python 側の生成コードを再生成する。C# 側は dotnet build で自動生成。
gen-proto:
    bash scripts/gen_proto.sh

# Resonite の主要 first-party DLL を ILSpy で decompile し、
# プロジェクトルートの decompiled/ に project 形式で書き出す。
# 既存の decompiled/ は wipe される (idempotent)。要 .env の ResonitePath。
decompile:
    bash scripts/decompile.sh

# ===== Python (python/) =================================================

py-format:
    cd python && uv run ruff format . && uv run ruff check --fix .

py-test:
    cd python && uv run pytest -v --cov

py-type:
    cd python && uv run pyright

# ===== C# (mod/) ========================================================

mod-format:
    cd mod && dotnet csharpier format .

mod-build:
    cd mod && dotnet build -c Release

mod-test:
    cd mod && dotnet test

# Thunderstore 配布用 zip を build/ に生成 (mod/Directory.Build.targets の PackTS)。
# 公開時は `just mod-pack PublishTS=true` で `dotnet tcli publish` に切替わる。
mod-pack:
    cd mod && dotnet build ResoniteIO.sln -c Release -t:PackTS -v d

# ローカル開発成果物と Gale プロファイルに配置された plugin を撤去する。
# 配置先ディレクトリ自体は残す (compose の rw 子 bind が指す inode を維持する
# ため; ディレクトリごと消すと container 内 bind が stale になる)。
mod-clean:
    find mod -type d -name 'bin' -prune -exec rm -rf {} +
    find mod -type d -name 'obj' -prune -exec rm -rf {} +
    rm -rf mod/build
    @GALE_ROOT="${GalePath:-./gale}"; \
    PLUGIN_DIR="$GALE_ROOT/BepInEx/plugins/ResoniteIO"; \
    if [ -d "$PLUGIN_DIR" ]; then \
        find "$PLUGIN_DIR" -mindepth 1 -delete && \
        echo "Cleared $PLUGIN_DIR"; \
    fi

# ===== 横断 ==============================================================

format: py-format mod-format

test: py-test mod-test

type: py-type

build: mod-build

# `just mod-build` で csproj の PostBuild Target が
# $(GalePath)/BepInEx/plugins/ResoniteIO/ に DLL+PDB を Copy する。
# 名前で意図を表すために専用レシピを残す。
# 配置先は GalePath (container) / repo root の ./gale/ (host) を優先順で解決。
# gale/ が無効なら build は成功するが Copy がスキップされるためエラー扱い。
deploy-mod: mod-build
    @GALE_ROOT="${GalePath:-./gale}"; \
    DLL="$GALE_ROOT/BepInEx/plugins/ResoniteIO/ResoniteIO.dll"; \
    if [ -f "$DLL" ]; then \
        echo "Deployed to $GALE_ROOT/BepInEx/plugins/ResoniteIO/"; \
    else \
        echo "ERROR: 配置先に DLL が見当たりません ($DLL)。" >&2; \
        echo "       Gale (https://github.com/Kesomannen/gale) v1.5.4+ で" >&2; \
        echo "       '<repo>/gale' に profile を作り、BepisLoader を追加してください。" >&2; \
        exit 1; \
    fi

# Gale プロファイル (./gale/) に BepisLoader と必須プラグインが揃っているか検証する。
# ホスト上で実行する想定 (container でも GalePath があれば動く)。
# 検査対象 (実プロファイルの配置に追従):
#   - $GALE_ROOT/BepisLoader.dll              (Gale が profile root に置く)
#   - $GALE_ROOT/BepInEx/core/BepInEx.Core.dll
#   - $GALE_ROOT/BepInEx/core/BepInEx.NET.Common.dll
#   - $GALE_ROOT/BepInEx/core/0Harmony.dll
#   - $GALE_ROOT/BepInEx/plugins/ResoniteModding-BepInExResoniteShim*/**/BepInExResoniteShim.dll
#   - $GALE_ROOT/BepInEx/plugins/ResoniteModding-BepisResoniteWrapper*/**/BepisResoniteWrapper.dll
# 不足あれば非 0 exit。version 表示は best-effort。
check-gale:
    @GALE_ROOT="${GalePath:-./gale}"; \
    echo "[check-gale] Checking Gale profile at $GALE_ROOT ..."; \
    fail=0; \
    check_file() { \
        local label="$1" path="$2"; \
        if [ -f "$path" ]; then \
            printf "  %-40s ✓\n" "$label"; \
        else \
            printf "  %-40s ✗  (expected at %s)\n" "$label" "$path" >&2; \
            fail=1; \
        fi; \
    }; \
    check_glob() { \
        local label="$1" pattern="$2"; \
        local match; \
        match=$(find $pattern 2>/dev/null | head -n 1); \
        if [ -n "$match" ]; then \
            printf "  %-40s ✓  (%s)\n" "$label" "$match"; \
        else \
            printf "  %-40s ✗  (no match for %s)\n" "$label" "$pattern" >&2; \
            fail=1; \
        fi; \
    }; \
    check_file "BepisLoader.dll"          "$GALE_ROOT/BepisLoader.dll"; \
    check_file "BepInEx.Core.dll"         "$GALE_ROOT/BepInEx/core/BepInEx.Core.dll"; \
    check_file "BepInEx.NET.Common.dll"   "$GALE_ROOT/BepInEx/core/BepInEx.NET.Common.dll"; \
    check_file "0Harmony.dll"             "$GALE_ROOT/BepInEx/core/0Harmony.dll"; \
    check_glob "BepInExResoniteShim.dll"  "$GALE_ROOT/BepInEx/plugins/ResoniteModding-BepInExResoniteShim*/BepInExResoniteShim/BepInExResoniteShim.dll"; \
    check_glob "BepisResoniteWrapper.dll" "$GALE_ROOT/BepInEx/plugins/ResoniteModding-BepisResoniteWrapper*/BepisResoniteWrapper/BepisResoniteWrapper.dll"; \
    if [ "$fail" -ne 0 ]; then \
        echo "[check-gale] ERROR: 必要な Gale 部品が見つかりません。" >&2; \
        echo "  Gale (https://github.com/Kesomannen/gale) で profile を更新し、" >&2; \
        echo "  少なくとも ResoniteModding-BepisLoader,"  >&2; \
        echo "  ResoniteModding-BepInExResoniteShim,"     >&2; \
        echo "  ResoniteModding-BepisResoniteWrapper を install してください。" >&2; \
        exit 1; \
    fi; \
    echo "[check-gale] All required Gale components present."

# Resonite (host 側プロセス) の BepInEx ログを追従する。print-debug の主経路。
# `tail -F` は inode 切り替え (ローテーション / Resonite 再起動) を跨いで再追従する。
# host 側で起動する想定 (Resonite が動いているのは container ではなく host)。
# Gale 経由起動時のログは profile 側 (gale/BepInEx/LogOutput.log) に出る。
# Gale が将来仕様変更する場合は要再確認。
log:
    @GALE_ROOT="${GalePath:-./gale}"; \
    LOG="$GALE_ROOT/BepInEx/LogOutput.log"; \
    if [ ! -f "$LOG" ]; then \
        echo "NOTE: $LOG はまだ存在しません。Gale から Resonite を起動すると tail が自動的に追従します。" >&2; \
    fi; \
    tail -F "$LOG"

# format → gen-proto → build → test → type を直列実行。コミット前のゲート。
run: format gen-proto build test type

# ===== Clean =============================================================

clean: clean-py mod-clean

clean-py:
    rm -rf python/.venv
    rm -rf python/.pytest_cache
    rm -rf python/.ruff_cache
    rm -rf python/.pyright
    rm -rf python/.coverage
    find python -type d -name '__pycache__' -prune -exec rm -rf {} +
    find python -type d -name '*.egg-info' -prune -exec rm -rf {} +

# ===== Container ============================================================

# docker-compose.yml の ${HOST_UID} / ${HOST_GID} 解釈に必要。export 付きの just 変数は
# 全レシピに環境変数として注入される。
export HOST_UID := `id -u`
export HOST_GID := `id -g`

container-build:
    docker compose build --no-cache

# bind マウント先を host 側で先に作っておく (Docker 任せだと root 所有で作られる)。
# ResonitePath 未設定だと /resonite bind が壊れるため事前に明示失敗させる。
# Gale プロファイル (./gale/) も同様に事前検証 (BepisLoader 不在だと mod が読まれない)。
container-up:
    @: "${ResonitePath:?ResonitePath が未設定です。.env に Resonite 実行ディレクトリを設定してください。}"
    @if [ ! -f gale/BepisLoader.dll ]; then \
        echo "ERROR: ./gale に Gale profile が見当たりません。" >&2; \
        echo "  Gale (https://github.com/Kesomannen/gale) v1.5.4+ で" >&2; \
        echo "  '<repo>/gale' に profile を作り、BepisLoader を追加してください。" >&2; \
        exit 1; \
    fi
    @mkdir -p gale/BepInEx/plugins/ResoniteIO
    docker compose up -d

container-down:
    docker compose down

# /workspace volume へ host repo を bootstrap copy + 依存解決。--force で再実行可能。
container-init *ARGS:
    docker compose exec dev bash /source/scripts/container-init.sh {{ARGS}}

container-shell:
    docker compose exec dev bash

# 完全削除 (named volume の作業内容も消える, destructive)。
container-clean:
    docker compose down -v --rmi local --remove-orphans
