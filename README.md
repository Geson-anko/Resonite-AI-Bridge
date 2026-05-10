# resonite-io

Resonite を AI エージェントの実行環境として使うための双方向 IPC ブリッジ。Resonite クライアント側で動く C# Mod (`ResoniteIO`、BepisLoader) と Python パッケージ (`resoio`) を、gRPC over Unix Domain Socket で接続する monorepo。

設計の背景・スコープ・採用技術・実装計画は [.claude/resonite_io_plan.md](.claude/resonite_io_plan.md) に集約されている。Claude Code 向けのリポジトリ規約は [CLAUDE.md](CLAUDE.md) を参照。

## ディレクトリ構成

```
proto/        単一の真実: .proto 定義 (resonite_io.v1)
mod/          C# 側 (BepisLoader mod, .NET 10)
python/       Python 側 (resoio, uv + betterproto2 + grpclib)
scripts/      setup / gen_proto / deploy_mod のシェルスクリプト
justfile      ルートタスクランナー (全レシピ)
```

## クイックスタート (Linux)

```sh
# 1. 開発ツール一式 (.NET / uv / protoc / just / csharpier / pre-commit) を導入
just setup

# 2. proto から Python 側コードを生成 (C# 側は dotnet build 時に自動生成)
just gen-proto

# 3. format → gen-proto → build → test → type を直列実行 (コミット前のゲート)
just run
```

レシピ一覧は `just` で確認できる。

| レシピ            | 役割                                                                   |
| ----------------- | ---------------------------------------------------------------------- |
| `just setup`      | scripts/setup.sh を実行 (任意の Linux ディストリで一発インストール)    |
| `just gen-proto`  | proto から Python 生成コードを再生成 (`python/src/resoio/_generated/`) |
| `just format`     | Python (ruff) と C# (csharpier) の両側をフォーマット                   |
| `just test`       | Python (pytest+cov) と C# (dotnet test) の両側を実行                   |
| `just type`       | Python の pyright を strict モードで実行                               |
| `just build`      | C# mod を `dotnet build -c Release`                                    |
| `just run`        | format → gen-proto → build → test → type を直列実行                    |
| `just deploy-mod` | `RESONITE_PLUGIN_DIR` (.env) へ ResoniteIO.dll をコピー                |
| `just clean`      | 各言語の build/cache 出力を削除                                        |

サブレシピ (`py-test` / `mod-build` 等) は片側だけ動かしたいときに利用する。

## 開発フロー

- 作業は `<種別>/<日付>/<内容>` のブランチで行う (例: `feature/20260510/skeleton`)。コミットは `<種別>(<スコープ>): <内容>` の形式。詳細は [CLAUDE.md](CLAUDE.md) §Git 運用 を参照。
- `.env` をリポジトリルートに置き、`.env.example` を参考に `RESONITE_PLUGIN_DIR` を設定する (`just deploy-mod` 用)。`.env` は git 管理外。
- 各言語の固有のセットアップ・ツール詳細は [python/README.md](python/README.md) と [mod/README.md](mod/README.md) を参照。

## ライセンス

[MIT](LICENSE)
