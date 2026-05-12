set dotenv-load := true
set shell := ["bash", "-c"]

# 既定で help を出す。
default:
    @just --list

# ===== 環境構築 =========================================================

# scripts/setup.sh で .NET / uv / protoc / just / csharpier / pre-commit を一括導入。
setup:
    bash scripts/setup.sh

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

# ローカル開発成果物と Resonite に配置された plugin を撤去する。
# ResonitePath が空 / ディレクトリが無い場合は plugin 撤去を skip する (安全側)。
mod-clean:
    find mod -type d -name 'bin' -prune -exec rm -rf {} +
    find mod -type d -name 'obj' -prune -exec rm -rf {} +
    rm -rf mod/build
    if [ -n "${ResonitePath:-}" ] && [ -d "$ResonitePath/BepInEx/plugins/ResoniteIO" ]; then \
        rm -rf "$ResonitePath/BepInEx/plugins/ResoniteIO" && \
        echo "Removed $ResonitePath/BepInEx/plugins/ResoniteIO"; \
    fi

# ===== 横断 ==============================================================

format: py-format mod-format

test: py-test mod-test

type: py-type

build: mod-build

# `just mod-build` で csproj の PostBuild Target が
# $(ResonitePath)/BepInEx/plugins/ResoniteIO/ に DLL+PDB を Copy する。
# 名前で意図を表すために専用レシピを残す。
# 配置先は ResonitePath 優先、無ければ Steam デフォルトパス。
# どちらも無効なら build は成功するが Copy がスキップされるためエラー扱い。
deploy-mod: mod-build
    @TARGET_ROOT="${ResonitePath:-$HOME/.steam/steam/steamapps/common/Resonite}"; \
    DLL="$TARGET_ROOT/BepInEx/plugins/ResoniteIO/ResoniteIO.dll"; \
    if [ -f "$DLL" ]; then \
        echo "Deployed to $TARGET_ROOT/BepInEx/plugins/ResoniteIO/"; \
    else \
        echo "ERROR: 配置先に DLL が見当たりません ($DLL)。" >&2; \
        echo "       .env に ResonitePath=<Resonite 実行ディレクトリ> を設定してください。" >&2; \
        exit 1; \
    fi

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
