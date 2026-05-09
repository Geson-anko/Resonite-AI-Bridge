# CLAUDE.md

このファイルは Claude Code (claude.ai/code) がこのリポジトリを扱う際のガイダンスを提供する。

## プロジェクト概要

`resonite-ai-bridge` は **Resonite を AI エージェントの実行環境として使うための双方向 IPC ブリッジ**。Resonite クライアント側で動く C# Mod (`ResoniteAIBridge`、BepisLoader) と Python パッケージ (`resobridge`) を、gRPC over Unix Domain Socket で接続する monorepo。

設計思想は **強化学習的な抽象化ではなく、リアルタイムロボティクス的な設計**。`Observation/Action` の抽象は持たず、`Camera` / `Audio` / `Locomotion` / `Manipulation` といったモダリティ単位で独立した非同期ストリームを提供する。RL の `step()` 同期はスコープ外で、Python 側ライブラリで上に構築されるべきもの。

詳細な背景・スコープ・採用技術・段階的実装計画は [.claude/resonite_ai_bridge_plan.md](.claude/resonite_ai_bridge_plan.md) を **必ず** 参照すること（Step 0〜7、決定事項一覧、リスク欄を含む）。

## メモリ参照

プロジェクト固有の規約・知見・ユーザーの好みは `.claude/memory/` に保存する（git 管理対象）。harness が自動ロードする `~/.claude/projects/.../memory/` パスは **使わない**（プロジェクト内の git 管理を優先する方針）。

セッション開始時、または規約が関係しそうなタスクに着手する前に [.claude/memory/MEMORY.md](.claude/memory/MEMORY.md) のインデックスを確認すること（まだ存在しない可能性あり。初回は作成する）。新しい規約・フィードバック・ユーザー像が判明した場合は同ディレクトリにファイルを足し、`MEMORY.md` から 1 行リンクを張る。

## プロジェクト状況

**現状: 計画フェーズ完了直後。実装は Step 0 (環境構築) から開始する**。リポジトリには `.claude/` 配下のドキュメントと `LICENSE` のみ存在し、`mod/` / `python/` / `proto/` / `scripts/` / `justfile` はまだない。

予定しているモノレポ構造は以下（[.claude/resonite_ai_bridge_plan.md §3.C](.claude/resonite_ai_bridge_plan.md) と一致させること）:

```
resonite-ai-bridge/
├── justfile                 # ルートタスクランナー (C# + Python + proto を一括)
├── proto/                   # 単一の真実: .proto 定義
│   └── resonite_ai_bridge/v1/{bridge,camera,audio,locomotion,manipulation}.proto
├── mod/                     # C# 側 (BepisLoader mod, .NET 10)
│   ├── ResoniteAIBridge.sln
│   ├── src/ResoniteAIBridge/{Bridge,Camera,Audio,Locomotion,Manipulation}/
│   └── tests/
├── python/                  # Python 側 (uv + betterproto2 + grpclib)
│   ├── pyproject.toml
│   └── src/resobridge/{bridge,camera,audio,locomotion,manipulation}.py
│       └── _generated/      # protoc 出力 (commit する)
├── scripts/{setup.sh, gen_proto.sh, deploy_mod.sh}
└── docs/  .github/workflows/  README.md
```

C# 側のモジュール構造と Python 側のモジュール構造は **モダリティ単位でミラーリング** する（plan §5 決定事項）。新しいモダリティを追加するときは両側に同名の単位を切ること。

## ツーリング

### タスクランナー

- **`just`** をリポジトリルートに置く `justfile` で運用する
- `justfile` は `set dotenv-load := true` を有効化し、`.env`（gitignore 済み・`.env.example` をコピー）から環境変数 (`RESONITE_PLUGIN_DIR` など) を読む
- レシピは Unix シェル前提で書く（Linux 一級サポートの方針と一致）
- C# / Python / proto をまたぐ作業を 1 コマンドにまとめるのが `just` 採用の目的。生のコマンドを直接叩くのは troubleshooting 時のみ

### C# (mod 側)

- ランタイム/SDK: **.NET 10 SDK**
- 依存: `Remora.Resonite.Sdk` (NuGet)、`BepisLoader`
- フォーマッタ: `csharpier` (`dotnet tool install -g csharpier`)
- 静的解析: Roslyn analyzers + `Nullable=enable` + `TreatWarningsAsErrors=true` (StyleCop は不採用)
- テスト: `xunit`
- Hot reload: 将来検討 (BepisLoader debugger attach 時)

### Python (resobridge 側)

- パッケージ・環境管理: `uv`（ロックファイル `python/uv.lock` をコミット）
- Python: **`>=3.10`** 必須 (betterproto2 の要件)
- gRPC スタック: **`betterproto2` + `grpclib`** (async)。`pip install "betterproto2[grpclib,compiler]"`、生成コードは Python dataclass + type hints ネイティブで pyright strict をそのまま通す想定
- 型チェッカー: `pyright` を `python/src/` に対し **strict** モードで実行（`tests/` は除外）
- リンター/フォーマッター: `ruff`（line-length 88、ダブルクォート、isort + `combine-as-imports`）
- テスト: `pytest`
- pre-commit: ruff、pyupgrade (`--py310-plus`)、docformatter、mdformat、codespell、`uv-lock`、pygrep checks

### proto

- スキーマファイル: `proto/resonite_ai_bridge/v1/*.proto`
- 生成: `just gen-proto` (内部で `scripts/gen_proto.sh`) で C# / Python の両側を出力
- Python 側出力は `python/src/resobridge/_generated/` に書き、commit する
- スキーマは **Step ごとに incremental に詰める**（plan §5）

### Linux ディストロ非依存

- `scripts/setup.sh` は **任意の Linux ディストリ** で動くこと。公式バイナリインストーラ優先 (`dotnet-install.sh`、`uv` の curl installer、`protoc` の GitHub releases バイナリ)
- ディストロ依存のパッケージマネージャ (apt/dnf/pacman) は最小限に留める
- `csharpier` 等は `dotnet tool install -g`、`betterproto2_compiler` 等は `uv` 経由

## コマンド

`just` レシピを使う（`uv run` / `dotnet` / `protoc` をラップ）。具体的な recipe は `justfile` を実装するときに固める想定だが、最低限以下の名前を提供する:

| レシピ            | 役割                                                                                             |
| ----------------- | ------------------------------------------------------------------------------------------------ |
| `just setup`      | `scripts/setup.sh` を呼んで .NET SDK / protoc / uv / Python deps / pre-commit を一発インストール |
| `just gen-proto`  | `scripts/gen_proto.sh` で `.proto` から C# / Python の両側コードを生成                           |
| `just deploy-mod` | `scripts/deploy_mod.sh` で `dotnet build` → `.dll` を Resonite の BepInEx/plugins/ にコピー      |
| `just format`     | C# (`csharpier`) と Python (`ruff format` + `ruff check --fix`) を両方走らせる                   |
| `just test`       | C# (`dotnet test`) と Python (`pytest -v --cov`) を両方走らせる                                  |
| `just type`       | Python の `pyright` を `python/src/` に対し strict 実行                                          |
| `just build`      | C# mod を `dotnet build -c Release`                                                              |
| `just run`        | `format` → `gen-proto` (proto に変更があれば) → `build` → `test` → `type` を直列実行             |
| `just clean`      | `dist/`、`__pycache__`、`.pytest_cache`、`bin/`、`obj/` 等を削除                                 |

サブコマンド分離が必要な場合の補助レシピ（実装時に追加）:

- `just py-test` / `just py-type` / `just py-format` — Python 側のみ
- `just mod-build` / `just mod-test` / `just mod-format` — C# 側のみ

細かい制御が必要な場合のフォールバック:

- 単一 Python テスト: `cd python && uv run pytest tests/resobridge/test_bridge.py -v`
- 単一パスへの pyright: `cd python && uv run pyright src/resobridge/bridge.py`
- C# 単一プロジェクトのビルド: `cd mod && dotnet build src/ResoniteAIBridge/ResoniteAIBridge.csproj`

### CI 整合性

- `.proto` を変更した場合は **必ず** `just gen-proto` を再実行し、生成物の差分も同じ commit に含める。CI は再生成して diff を取るチェックを入れる予定（plan §3.E）

## 実行環境の注意点

### Resonite クライアント

- **通常クライアント上で動作** (Camera 描画が必要なため Headless は不可)
- Steam で Resonite をインストール: Linux ネイティブ FrooxEngine + Proton 経由 Renderite
- リモート開発時は Sunshine + Moonlight を想定 (plan §3.A)
- 開発用ワールドは不要 (ワールド非依存に設計)

### Renderite IPC のドキュメント不足

Camera readback の実装は **decompile を読みながら**進める前提（plan §7 リスク）。手探りになる箇所はその場の発見をコメントでは残さず、`.claude/memory/` に feedback として残すこと。

### ライセンス・ToS

Resonite は明示的な研究用 bot 規定なし。慣習的には黙認〜歓迎（plan §7）。商用化や派手な公開実験を始める前にユーザーに確認する。

## コーディング規約

### 共通

- 通信データ型は **pyright strict をクリアする型付け**（plan §1 非機能要件）
- 各モダリティは他のモダリティに依存しない（片方だけ使う構成も可能）
- グローバルな clock や barrier は持たない。各ストリームに **タイムスタンプ** を付与し、必要な同期は受信側で行う（plan §2 同期戦略）

### C# 側

- 名前空間は `ResoniteAIBridge.<Modality>`
- `Nullable=enable` + `TreatWarningsAsErrors=true` を `.csproj` で必ず有効にする
- gRPC server は **別スレッドで動作** させ、FrooxEngine 本体スレッドをブロックしない（plan Step 2）
- LocalUser 駆動など FrooxEngine API を呼ぶ箇所は engine の update tick 上にディスパッチする必要がある可能性大。スレッド要件はモジュールごとに調査して `.claude/memory/` に書き残すこと

### Python 側

- パッケージ名は `resobridge`、import 名は `resobridge`
- PEP 561 typed (`py.typed` 同梱)
- バージョンは `pyproject.toml` の `[project].version` を真値とし、`resobridge.__version__` は `importlib.metadata` 経由で読む。他の場所にバージョンをハードコードしない
- カプセル化: クラスの内部実装の詳細や `__init__` で設定される属性は原則 private (`_` prefix)。外部から参照する必要があるものだけ public にする
- private モジュール規約: テストを書かないモジュールは `_` prefix、書くモジュールは prefix なし。外部公開は親 `__init__.py` の `__all__` で別軸として集約

## テスト方針

### 基本原則

- 必要十分なテストのみ。過剰なテストは避ける
- 内部実装の詳細はテストせず、公開インターフェースと振る舞いをテストする
- Python のテスト関数に戻り値の型アノテーションは不要

### テストレイアウト (Python)

`python/tests/` は `python/src/resobridge/` の構造を 1 対 1 でミラーリングする:

- `src/resobridge/foo.py` ↔ `tests/resobridge/test_foo.py`
- `tests/` 直下に置くのは `__init__.py` / `helpers.py` / `conftest.py` / `manual/` のみ
- 1 ファイル 1 テストを原則とする

### 実践的なテスト

- 実オブジェクト・実入出力で振る舞いを検証する
- できる限りモックを使わない。テスト用の実データ（一時ファイル等）で代替できるなら実データを使う
- モック許容範囲: 外部 API（Resonite クライアント本体・gRPC peer）、ファイルシステム/DB など再現困難な依存
- 内部モジュール同士の結合はモックせず実結合
- 複数パラメータは `@pytest.mark.parametrize`
- `pytest_mock` を使用 (`mocker.Mock`)。`unittest.mock` は使わない
- 共有モックは `tests/conftest.py` のフィクスチャに集約

### gRPC のテスト

- 単体: 生成された dataclass メッセージに対する純粋関数のテスト (mock 不要)
- 結合: in-process で gRPC server/client を立てて UDS で繋ぐパターンを優先 (`grpclib` で簡単に書ける)。Resonite 本体には依存させない
- Resonite 接続を伴う end-to-end は `python/tests/e2e/` に置き、`pytest --ignore=python/tests/e2e` で自動収集対象外にする

### C# 側

- xunit。FrooxEngine に依存するロジックは Resonite を起動しないと真に検証できないため、ユニットテスト対象は **純粋なバイト列処理 / proto 変換 / 状態機械** に絞る
- C# の e2e は手順書として `mod/tests/manual/` に Markdown で残す方針 (Step 1〜2 で確定させる)

## Git 運用

### ブランチ

- `main`: 開発の主軸
- 作業用ブランチの命名規則: `<種別>/<日付>/<内容>`
  - 例: `feature/20260509/grpc-skeleton`、`fix/20260509/uds-permission`
  - 種別: `feature`, `fix`, `refactor`, `docs`, `chore`
- 必ずブランチ上で commit する（`main` に直接 commit しない）
- 作業ブランチは `main` から分岐する
- `main` へのマージはユーザーが判断・実行する

### コミットメッセージ

`<種別>(<スコープ>): <内容>` の形式に従う。

- 種別: `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`
- スコープ: `mod`, `python`, `proto`, `scripts`, `ci`, `docs` などの top-level、または `mod/camera`、`python/locomotion` のようなモダリティ単位
- 例:
  - `feat(proto): bridge.proto に Ping RPC を追加`
  - `feat(mod/bridge): UDS gRPC server をエンジン起動時に bind`
  - `feat(python/camera): server-streaming で RGB フレームを受信`

## 自走開発フロー

Claude Code が自律的に実装・検証・コミットを行うためのフロー。

### 基本サイクル

1. **要件確認**: [.claude/resonite_ai_bridge_plan.md](.claude/resonite_ai_bridge_plan.md) の該当 Step を読み、スコープと未解決事項を把握する
2. **作業ブランチ作成**: `main` から `<種別>/<日付>/<内容>` で作業ブランチを切る
3. **実装**: コードを書く（C# / Python / proto のどれか、もしくは複数）
4. **検証**:
   - proto を変更した場合は `just gen-proto` を再実行し生成物を含めて差分を確認
   - `just run` がすべてパスすることを確認する
5. **コミット**: 細かい単位でコミットし、1 コミットに複数の関心事を混ぜない
6. **繰り返し**: 3-5 を機能単位で繰り返す

### 検証の原則

- **コミット前に必ず `just run` を実行する**
- テストが失敗したらコミットせず修正してから再検証
- 新しいモジュールを追加した場合はテストも書く
- 型チェックエラー (pyright strict / C# warnings-as-errors) を放置しない

### 判断基準

- plan に明記されている内容はそのまま実装する
- plan に記載がない実装の詳細（アルゴリズム選択、内部設計等）は自分で判断してよい
- plan の未決事項に関わる部分は、合理的なデフォルトで実装し、コミットメッセージに判断理由を記載する
- スコープ外の機能（RL `step()`、マルチエージェント、ワールド作者向け API 等）は実装しない

## エージェントチーム戦略

「エージェントチームで行う」という指示があり具体的な手順が示されていない場合、以下のサイクルに従う。利用可能なエージェントは [.claude/agents/](.claude/agents/) のもの。

### 実装サイクル

1. **spec-planner**: 要件を分析し、インターフェース設計と実装計画を策定する（コードは書かない）
2. **spec-driven-implementer → code-quality-reviewer**: 計画に基づき実装し、リファクタリングする。品質が十分になるまで繰り返す
3. **docstring-author**: 最後にコメント・ドキュメントの追加・更新が必要か確認する

### 並列化

- 変更規模に応じて並列に動作するエージェント数を増やす
- 並列化の対象: spec-driven-implementer、code-quality-reviewer
- 分割可能なタスク数だけ並列に実行する（モダリティが独立しているので並列化との相性が良い: Camera と Locomotion を別々の implementer に投げる、など）
