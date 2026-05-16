set dotenv-load := true
set shell := ["bash", "-c"]

# 既定で help を出す。
default:
    @just --list

# ===== 環境構築 =========================================================

# 初回 setup: host tooling 検証 / .env セットアップ / Gale プロファイル確認を
# 1 コマンドで行う。host 上で実行する想定 (container は要らない)。冪等。
#
#   1. docker / docker compose v2 の存在確認
#   2. .env が無ければ .env.example をコピーし $EDITOR (既定 vi) で開かせる。
#      その場合は exit 0 で抜け、ユーザーに `just init` 再実行を促す
#      (set dotenv-load の解釈はパース時のため、同一実行内で再 source できないため)
#   3. ResonitePath が指すディレクトリの実在を検証
#   4. ./gale/BepisLoader.dll を見て Gale プロファイル設置を判定:
#      - 既設なら `check-gale` を呼び全部品を厳密チェック
#      - 未設なら手順を stderr に出して非 0 exit
init:
    @echo "[init] Checking host tooling ..."
    @command -v docker >/dev/null 2>&1 || { echo "ERROR: docker が見つかりません。" >&2; exit 1; }
    @docker compose version >/dev/null 2>&1 || { echo "ERROR: docker compose v2 が必要です。" >&2; exit 1; }
    @if [ ! -f .env ]; then \
        cp .env.example .env; \
        echo "[init] .env を .env.example から作成しました。"; \
        if [ -t 0 ] && [ -t 1 ]; then \
            echo "[init] '${EDITOR:-vi}' で開きます。保存後、もう一度 'just init' を実行してください。"; \
            "${EDITOR:-vi}" .env; \
        else \
            echo "[init] 非対話 shell のため editor は起動しません。.env を編集してから 'just init' を再実行してください。" >&2; \
        fi; \
        exit 0; \
    fi
    @echo "[init] .env exists."
    @: "${ResonitePath:?ResonitePath が .env に設定されていません。.env を編集してから 'just init' を再実行してください。}"
    @[ -d "$ResonitePath" ] || { echo "ERROR: ResonitePath=$ResonitePath はディレクトリではありません。" >&2; exit 1; }
    @echo "[init] ResonitePath OK: $ResonitePath"
    @if [ ! -f gale/BepisLoader.dll ]; then \
        echo "" >&2; \
        echo "ERROR: ./gale に Gale profile が未設置です。" >&2; \
        echo "" >&2; \
        echo "  以下を host 上で実施してください:" >&2; \
        echo "    1. Gale v1.5.4+ をインストール (https://github.com/Kesomannen/gale)" >&2; \
        echo "    2. Gale GUI で 'Create profile' を選び、パスに <repo>/gale を指定" >&2; \
        echo "       (このパスは EMPTY である必要があります — gale/ ディレクトリを" >&2; \
        echo "        事前に作らないでください)" >&2; \
        echo "    3. プロファイルに以下 3 つの mod を install:" >&2; \
        echo "         - ResoniteModding-BepisLoader (>=1.5.1)" >&2; \
        echo "         - ResoniteModding-BepInExResoniteShim (>=0.9.3)" >&2; \
        echo "         - ResoniteModding-BepisResoniteWrapper (>=1.0.2)" >&2; \
        echo "    4. 完了後 'just init' を再実行" >&2; \
        exit 1; \
    fi
    @just check-gale
    @echo ""
    @echo "[init] All preconditions satisfied. Next:"
    @echo "    just container-build      # 初回のみ"
    @echo "    just container-up"
    @echo "    just container-init       # container 内 deps 解決"

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

# ResonitePath 未設定だと /resonite bind が壊れるため事前に明示失敗させる。
# Gale プロファイルの設置確認は `just init` の責務 (本レシピは container 起動のみ)。
container-up:
    @: "${ResonitePath:?ResonitePath が未設定です。'just init' を先に実行してください。}"
    # UDS socket 用 host ディレクトリを 0700 で先に作る。Docker 任せだと root 所有
    # で生成され、mod (host UID) が bind できなくなる。
    # $XDG_RUNTIME_DIR が無い (systemd-logind セッション外) 環境では失敗させる。
    @: "${XDG_RUNTIME_DIR:?XDG_RUNTIME_DIR が未設定です。systemd-logind セッション内で実行してください (loginctl enable-linger は不要)。}"
    @SOCK_DIR="$XDG_RUNTIME_DIR/resonite-io"; \
        mkdir -p "$SOCK_DIR" && chmod 0700 "$SOCK_DIR"
    docker compose up -d

container-down:
    docker compose down

# container 内で deps を解決する冪等レシピ (dotnet tool restore + uv sync +
# pre-commit install + Claude settings symlink)。/workspace は host repo の bind
# なので rsync は不要。依存追加 / lock 更新後に再実行する。
container-init:
    docker compose exec dev bash scripts/container-init.sh

container-shell:
    docker compose exec dev bash

# 完全削除 (named volume の作業内容も消える, destructive)。
container-clean:
    docker compose down -v --rmi local --remove-orphans
