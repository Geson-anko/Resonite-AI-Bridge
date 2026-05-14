# resonite-io

Resonite を AI エージェントの実行環境として使うための双方向 IPC ブリッジ。Resonite クライアント側で動く C# Mod (`ResoniteIO`、BepisLoader) と Python パッケージ (`resoio`) を、gRPC over Unix Domain Socket で接続する monorepo。

設計の背景・スコープ・採用技術・実装計画は [.claude/resonite_io_plan.md](.claude/resonite_io_plan.md) に集約されている。Claude Code 向けのリポジトリ規約は [CLAUDE.md](CLAUDE.md) を参照。

## ディレクトリ構成

```text
proto/             単一の真実: .proto 定義 (resonite_io.v1)
mod/               C# 側 (BepisLoader mod, .NET 10)
python/            Python 側 (resoio, uv + betterproto2 + grpclib)
scripts/           gen_proto / decompile / container-init のシェルスクリプト
gale/              Gale (Resonite mod manager) profile 展開先 (gitignore、host で Gale が管理)
Dockerfile         開発コンテナ image (debian + .NET 10 + uv + protoc)
docker-compose.yml dev サービス定義 (host UID/GID 一致 / ResonitePath bind / Gale profile bind)
justfile           ルートタスクランナー (全レシピ)
```

## Quick Start

ホスト側に必要なもの: `docker` (24+) / `docker compose v2` / `just` / **[Gale](https://github.com/Kesomannen/gale) v1.5.4+** (Resonite mod manager)

開発ツール (.NET 10 SDK / uv / protoc / pre-commit など) はすべてコンテナ内に閉じている。

### 1. 事前準備: Gale プロファイル作成

mod は host Resonite を汚さず、repo root の `./gale/` を Gale のカスタムプロファイルとして使う。

1. host に Gale v1.5.4+ をインストール ([github.com/Kesomannen/gale](https://github.com/Kesomannen/gale))
2. Gale でプロファイルを新規作成し、パス指定欄に `<repo>/gale` を入力
3. profile に以下を install:
   - `ResoniteModding-BepisLoader` (>=1.5.1)
   - `ResoniteModding-BepInExResoniteShim` (>=0.9.3)
   - `ResoniteModding-BepisResoniteWrapper` (>=1.0.2)
4. Gale で Resonite を一度起動して `<repo>/gale/BepInEx/` の生成を確認
5. `just check-gale` で必要 DLL が揃っているか検証

> `./gale/` は `.gitignore` 済みで host 側の Gale が管理する。リポジトリにはコミットされない。

### 2. 環境設定

```sh
cp .env.example .env
# .env の ResonitePath を Resonite 実行ディレクトリの絶対パスに書き換える
# (FrooxEngine.dll の HintPath 参照専用; deploy 先ではない)
```

### 3. Docker image をビルド

```sh
just container-build
```

UID/GID は host user と一致した形で焼かれる。host user が変わったら再ビルドが必要。

### 4. コンテナ起動 + 初期化

```sh
just container-up      # サービス起動 (sleep infinity で常駐)
just container-init    # /workspace volume に repo を bootstrap + 依存解決
```

`container-init` は初回のみ必要。再実行する場合は `--force` を付ける。

### 5. 開発

```sh
just container-shell   # コンテナ内 bash に attach
# 以下、コンテナ内で:
just --list            # 利用可能なレシピ一覧
just gen-proto         # proto から Python 側コード生成
just build             # mod ビルド
just deploy-mod        # gale/BepInEx/plugins/ResoniteIO/ に DLL を配置
```

deploy された DLL は host user 所有になっている (UID/GID マッピング済み)。

### 6. 後片付け

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
| `just deploy-mod` | `gale/BepInEx/plugins/ResoniteIO/` へ DLL+PDB を配置 (Gale profile)    |
| `just check-gale` | Gale profile に BepisLoader / 必須 plugin が揃っているか検証           |
| `just clean`      | 各言語の build/cache 出力を削除                                        |

サブレシピ (`py-test` / `mod-build` 等) は片側だけ動かしたいときに利用する。Container 系レシピは [CLAUDE.md](CLAUDE.md) §コマンド 参照。

## 開発フロー

- 作業は `<種別>/<日付>/<内容>` のブランチで行う (例: `feature/20260510/skeleton`)。コミットは `<種別>(<スコープ>): <内容>` の形式。詳細は [CLAUDE.md](CLAUDE.md) §Git 運用 を参照。
- `.env` をリポジトリルートに置き、`.env.example` を参考に `ResonitePath` を設定する。`.env` は git 管理外。
- 各言語の固有のセットアップ・ツール詳細は [python/README.md](python/README.md) と [mod/README.md](mod/README.md) を参照。

## 実機 smoke test

mod が Gale 経由起動の Resonite に正しくロードされるかを確認する手順は [mod/tests/manual/load-verification.md](mod/tests/manual/load-verification.md) を参照。

## ライセンス

[MIT](LICENSE)
