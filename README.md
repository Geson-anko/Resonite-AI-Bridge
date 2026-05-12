# resonite-io

Resonite を AI エージェントの実行環境として使うための双方向 IPC ブリッジ。Resonite クライアント側で動く C# Mod (`ResoniteIO`、BepisLoader) と Python パッケージ (`resoio`) を、gRPC over Unix Domain Socket で接続する monorepo。

設計の背景・スコープ・採用技術・実装計画は [.claude/resonite_io_plan.md](.claude/resonite_io_plan.md) に集約されている。Claude Code 向けのリポジトリ規約は [CLAUDE.md](CLAUDE.md) を参照。

## ディレクトリ構成

```
proto/             単一の真実: .proto 定義 (resonite_io.v1)
mod/               C# 側 (BepisLoader mod, .NET 10)
python/            Python 側 (resoio, uv + betterproto2 + grpclib)
scripts/           gen_proto / decompile / container-init のシェルスクリプト
Dockerfile         開発コンテナ image (debian + .NET 10 + uv + protoc)
docker-compose.yml dev サービス定義 (host UID/GID 一致 / ResonitePath bind)
justfile           ルートタスクランナー (全レシピ)
```

## Quick Start

ホスト側に必要なもの: `docker` (24+) / `docker compose v2` / `just`

開発ツール (.NET 10 SDK / uv / protoc / pre-commit など) はすべてコンテナ内に閉じている。

### 1. 環境設定

```sh
cp .env.example .env
# .env の ResonitePath を Resonite 実行ディレクトリの絶対パスに書き換える
```

### 2. Docker image をビルド

```sh
just container-build
```

UID/GID は host user と一致した形で焼かれる。host user が変わったら再ビルドが必要。

### 3. コンテナ起動 + 初期化

```sh
just container-up      # サービス起動 (sleep infinity で常駐)
just container-init    # /workspace volume に repo を bootstrap + 依存解決
```

`container-init` は初回のみ必要。再実行する場合は `--force` を付ける。

### 4. 開発

```sh
just container-shell   # コンテナ内 bash に attach
# 以下、コンテナ内で:
just --list            # 利用可能なレシピ一覧
just gen-proto         # proto から Python 側コード生成
just build             # mod ビルド
just deploy-mod        # ${ResonitePath}/BepInEx/plugins/ResoniteIO/ に DLL を配置
```

deploy された DLL は host user 所有になっている (UID/GID マッピング済み)。

### 5. 後片付け

```sh
just container-down    # コンテナ停止 (volume は残す)
just container-clean   # volume も含めて完全削除 (destructive)
```

## 主なレシピ

| レシピ            | 役割                                                                   |
| ----------------- | ---------------------------------------------------------------------- |
| `just gen-proto`  | proto から Python 生成コードを再生成 (`python/src/resoio/_generated/`) |
| `just format`     | Python (ruff) と C# (csharpier) の両側をフォーマット                   |
| `just test`       | Python (pytest+cov) と C# (dotnet test) の両側を実行                   |
| `just type`       | Python の pyright を strict モードで実行                               |
| `just build`      | C# mod を `dotnet build -c Release`                                    |
| `just run`        | format → gen-proto → build → test → type を直列実行                    |
| `just deploy-mod` | `${ResonitePath}/BepInEx/plugins/ResoniteIO/` へ DLL+PDB を配置        |
| `just clean`      | 各言語の build/cache 出力を削除                                        |

サブレシピ (`py-test` / `mod-build` 等) は片側だけ動かしたいときに利用する。Container 系レシピは [CLAUDE.md](CLAUDE.md) §コマンド 参照。

## 開発フロー

- 作業は `<種別>/<日付>/<内容>` のブランチで行う (例: `feature/20260510/skeleton`)。コミットは `<種別>(<スコープ>): <内容>` の形式。詳細は [CLAUDE.md](CLAUDE.md) §Git 運用 を参照。
- `.env` をリポジトリルートに置き、`.env.example` を参考に `ResonitePath` を設定する。`.env` は git 管理外。
- 各言語の固有のセットアップ・ツール詳細は [python/README.md](python/README.md) と [mod/README.md](mod/README.md) を参照。

## ライセンス

[MIT](LICENSE)
