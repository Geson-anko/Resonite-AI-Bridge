set dotenv-load := true
set shell := ["bash", "-c"]
set export := true

# .NET global tools (csharpier 等) が PATH に載るよう DOTNET_ROOT/PATH を justfile 側で確実に解決する。
# scripts/setup.sh が ~/.bashrc に追加するエントリは新規シェルで効くが、`just` は子シェルなので
# 改めてここで上書きする。
DOTNET_ROOT := env("DOTNET_ROOT", home_directory() + "/.dotnet")
PATH := DOTNET_ROOT + "/tools:" + env("PATH")

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

# ===== Python (python/) =================================================

py-format:
    cd python && uv run ruff format . && uv run ruff check --fix .

py-test:
    cd python && uv run pytest -v --cov

py-type:
    cd python && uv run pyright

# ===== C# (mod/) ========================================================

mod-format:
    cd mod && csharpier format .

mod-build:
    cd mod && dotnet build -c Release

mod-test:
    cd mod && dotnet test

# ===== 横断 ==============================================================

format: py-format mod-format

test: py-test mod-test

type: py-type

build: mod-build

# RESONITE_PLUGIN_DIR (.env) が指す Resonite の plugins ディレクトリへ
# ResoniteAIBridge.dll をコピー。
deploy-mod:
    bash scripts/deploy_mod.sh

# format → gen-proto → build → test → type を直列実行。コミット前のゲート。
run: format gen-proto build test type

# ===== Clean =============================================================

clean: clean-py clean-mod

clean-py:
    rm -rf python/.venv
    rm -rf python/.pytest_cache
    rm -rf python/.ruff_cache
    rm -rf python/.pyright
    rm -rf python/.coverage
    find python -type d -name '__pycache__' -prune -exec rm -rf {} +
    find python -type d -name '*.egg-info' -prune -exec rm -rf {} +

clean-mod:
    find mod -type d -name 'bin' -prune -exec rm -rf {} +
    find mod -type d -name 'obj' -prune -exec rm -rf {} +
