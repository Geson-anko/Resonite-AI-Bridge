# CLAUDE.md

このファイルは Claude Code (claude.ai/code) がこのリポジトリを扱う際のガイダンスを提供する。

## プロジェクト概要

`resonite-io` は **Resonite を AI エージェントの実行環境として使うための双方向 IPC ブリッジ**。Resonite クライアント側で動く C# Mod (`ResoniteIO`、BepisLoader) と Python パッケージ (`resoio`) を、gRPC over Unix Domain Socket で接続する monorepo。

設計思想は **強化学習的な抽象化ではなく、リアルタイムロボティクス的な設計**。`Observation/Action` の抽象は持たず、`Camera` / `Audio` / `Locomotion` / `Manipulation` といったモダリティ単位で独立した非同期ストリームを提供する。RL の `step()` 同期はスコープ外で、Python 側ライブラリで上に構築されるべきもの。

C# 実装は **Core/Mod 二層構成**: コア機能 (gRPC server / Service / proto handler / 各モダリティのドメインロジック) は **Resonite に一切依存しないピュアライブラリ `ResoniteIO.Core`** に置き、BepInEx Plugin `ResoniteIO` は engine bridging のみを担う薄いアダプタとする。依存方向は **Core ← Mod** で逆参照禁止。Python (`resoio`) も Resonite 非依存。詳細は [.claude/memory/feedback_core_mod_layering.md](.claude/memory/feedback_core_mod_layering.md) 参照。

詳細な背景・スコープ・採用技術・段階的実装計画は [.claude/resonite_io_plan.md](.claude/resonite_io_plan.md) を **必ず** 参照すること（Step 0〜7、決定事項一覧、リスク欄を含む）。

## メモリ参照

プロジェクト固有の規約・知見・ユーザーの好みは `.claude/memory/` に保存する（git 管理対象）。harness が自動ロードする `~/.claude/projects/.../memory/` パスは **使わない**（プロジェクト内の git 管理を優先する方針）。

セッション開始時、または規約が関係しそうなタスクに着手する前に [.claude/memory/MEMORY.md](.claude/memory/MEMORY.md) のインデックスを確認すること（まだ存在しない可能性あり。初回は作成する）。新しい規約・フィードバック・ユーザー像が判明した場合は同ディレクトリにファイルを足し、`MEMORY.md` から 1 行リンクを張る。

## プロジェクト状況

**現状: Step 0 (Docker 化された開発環境構築) と Step 1 (mod / python / proto のスケルトン) が完了。次は Step 2 (gRPC `Session.Ping` の C# サーバ実装 + Python クライアント接続)**。Step 2 で `ResoniteIO.Core` プロジェクトを新設し、現状 `mod/src/ResoniteIO/` 配下にある `.gitkeep` ディレクトリ群のロジック実装は Core 側に移す。

実装済みの主要要素:

- 開発環境: `Dockerfile` / `docker-compose.yml` / `justfile` / `scripts/container-init.sh`
- C# Mod: `mod/src/ResoniteIO/ResoniteIOPlugin.cs` (BasePlugin で `ResoniteHooks.OnEngineReady` を購読してログ出力するのみ)
- Python: `python/src/resoio/session.py` は **placeholder**（`PLACEHOLDER` 定数のみ。Step 2 で `SessionClient` に差し替え）
- proto: `proto/resonite_io/v1/session.proto` のみ (`Ping` RPC)。他モダリティの proto は後続 Step で追加
- 補助スクリプト: `scripts/gen_proto.sh` (Python 生成専用) / `scripts/decompile.sh` (ilspycmd) / `scripts/lib.sh`
- proto lint: `buf.yaml` (`SERVICE_SUFFIX` 除外)
- mod Thunderstore packaging: `thunderstore.toml` + `tcli` local tool + `dotnet build -t:PackTS`
- Core/Mod 二層構成は **Step 2 で導入予定** (現状は単一 `ResoniteIO` プロジェクトのみ存在)

リポジトリ実構造:

```text
resonite-io/
├── Dockerfile                 # 開発コンテナ image (debian + .NET 10 + uv + protoc)
├── docker-compose.yml         # dev サービス定義 (host UID/GID 一致 / ResonitePath bind / Gale profile bind)
├── justfile                   # ルートタスクランナー
├── buf.yaml                   # proto lint/breaking 設定
├── .pre-commit-config.yaml
├── .env.example               # `.env` の雛形 (ResonitePath 等)
├── proto/                     # 単一の真実: .proto 定義
│   └── resonite_io/v1/session.proto   # 他モダリティ proto は Step 3+ で追加
├── mod/                       # C# 側 (.NET 10、Step 2 以降は Core/Mod 二層構成)
│   ├── ResoniteIO.sln
│   ├── Directory.Build.{props,targets}
│   ├── NuGet.config / thunderstore.toml / icon.png
│   ├── src/ResoniteIO/        # mod 層 (BepInEx adapter)。Step 2 で ResoniteIO.Core が同階層に追加される
│   │   ├── ResoniteIO.csproj
│   │   ├── ResoniteIOPlugin.cs        # BasePlugin + OnEngineReady フック
│   │   └── {Session,Camera,Audio,Locomotion,Manipulation}/   # .gitkeep のみ (Step 2 で Bridge 実装に置換)
│   └── tests/ResoniteIO.Tests/        # xunit smoke test
├── python/                    # Python 側 (uv + betterproto2 + grpclib)
│   ├── pyproject.toml         # requires-python >=3.12
│   ├── uv.lock
│   ├── src/resoio/
│   │   ├── __init__.py        # importlib.metadata で __version__
│   │   ├── py.typed
│   │   ├── session.py         # ← Step 2 で実装に差し替える placeholder
│   │   └── _generated/        # protoc 出力 (commit する)
│   └── tests/resoio/
├── scripts/{gen_proto.sh, decompile.sh, container-init.sh, lib.sh}   # container-init.sh は container 内の deps restore のみ (bind mount のため rsync 無し)
├── decompiled/                # ILSpy 出力 (gitignore、`just decompile` で再生成)
├── gale/                      # Gale (Resonite mod manager) profile 展開先 (gitignore、host で Gale が管理)
├── .claude/                   # 規約・memory (本ファイル / resonite_io_plan.md / agents/)
├── .github/workflows/         # (未整備)
└── README.md
```

C# 側のモジュール構造と Python 側のモジュール構造は **モダリティ単位でミラーリング** する（plan §5 決定事項）。新しいモダリティを追加するときは両側に同名の単位を切ること。C# 側では各モダリティを **Core 側 Service + Mod 側 Bridge** のペアで実装する (Core に `<Modality>Service`、Mod に `FrooxEngine<Modality>Bridge`)。

## ツーリング

### タスクランナー

- **`just`** をリポジトリルートに置く `justfile` で運用する
- `justfile` は `set dotenv-load := true` を有効化し、`.env`（gitignore 済み・`.env.example` をコピー）から環境変数 (`ResonitePath` など) を読む
- レシピは Unix シェル前提で書く（Linux 一級サポートの方針と一致）
- ビルド系・テスト系・gen-proto 等のレシピは **コンテナ内で実行する前提**。host 側で叩くのは `container-*` レシピと `deploy-mod` のような host パスに触るレシピのみ
- C# / Python / proto をまたぐ作業を 1 コマンドにまとめるのが `just` 採用の目的。生のコマンドを直接叩くのは troubleshooting 時のみ

### C# (mod 側)

以下のツール群はすべて **コンテナ内に閉じている**。host へのインストールは不要。

- ランタイム/SDK: **.NET 10 SDK**
- 依存: BepisLoader 公式 Template (`dotnet new bep6resonite`) 準拠。`mod/src/ResoniteIO/ResoniteIO.csproj` で以下を明示参照:
  - `BepInEx.ResonitePluginInfoProps` (csproj メタデータから `PluginMetadata.*` を build-time 生成)
  - `ResoniteModding.BepInExResoniteShim` (`[ResonitePlugin]` 属性と `ResoniteHooks.OnEngineReady`)
  - `ResoniteModding.BepisResoniteWrapper` (Harmony 等のラッパ)
- フォーマッタ: `csharpier` (`.config/dotnet-tools.json` 配下の local tool、`dotnet csharpier ...` で呼ぶ)
- 配布: `tcli` (`.config/dotnet-tools.json` 配下の local tool) で Thunderstore zip 生成。`just mod-pack` がラップ
- 静的解析: Roslyn analyzers + `Nullable=enable` + `TreatWarningsAsErrors=true` (StyleCop は不採用)
- テスト: `xunit`
- Hot reload: 将来検討 (BepisLoader debugger attach 時)

### Python (resoio 側)

以下も **コンテナ内に閉じている**。host へのインストールは不要。

- パッケージ・環境管理: `uv`（ロックファイル `python/uv.lock` をコミット）
- Python: **`>=3.12`** 必須 (`python/pyproject.toml` の `requires-python` と pyright の `pythonVersion` で固定)
- gRPC スタック: **`betterproto2` + `grpclib`** (async)。依存は `betterproto2[grpclib]`。**`betterproto2_compiler` は別 distribution で配布されており `[compiler]` extra は存在しない** (PyPI metadata 2026-05 で確認済み)。dev グループに固定し `uv run protoc` から呼ぶ。生成コードは Python dataclass + type hints ネイティブで pyright strict をそのまま通す想定
- 型チェッカー: `pyright` を `python/src/` に対し **strict** モードで実行（`tests/` は除外）
- リンター/フォーマッター: `ruff`（line-length 88、ダブルクォート、isort + `combine-as-imports`）
- テスト: `pytest` (+ `pytest-asyncio` / `pytest-cov` / `pytest-mock`)
- pre-commit: ruff、pyupgrade、docformatter、mdformat、codespell、`uv-lock`、pygrep checks、shellcheck、shfmt

### proto

- スキーマファイル: `proto/resonite_io/v1/*.proto`
- C# 側生成は `<Protobuf>` ItemGroup により **`dotnet build` 時に `Grpc.Tools` が自動生成** する (Server スタブのみ、`obj/` に出力するため commit しない)。配置は **Core 側 `mod/src/ResoniteIO.Core/ResoniteIO.Core.csproj`** (Step 2 で新設)。Mod 側 csproj は Core を `ProjectReference` するだけで proto を直接参照しない
- Python 側生成は `just gen-proto` (内部で `scripts/gen_proto.sh`) で `python/src/resoio/_generated/` に書き、commit する。**コンテナ内で実行する**
- proto lint: `buf` (`buf.yaml`)。`SERVICE_SUFFIX` は除外 (service 名はモダリティ名そのもの)
- スキーマは **Step ごとに incremental に詰める**（plan §5）

### Docker 開発環境

開発ツール (.NET 10 SDK / uv / protoc / dotnet local tools / pre-commit) は
**`debian:bookworm-slim` ベースの単一 image** に同梱し、host にはインストールしない。

- `docker-compose.yml` は `name: resonite-io-${USER}` で **user 単位の名前空間** に分離 (同一ホストの複数アカウント / 複数 worktree が衝突しない)
- 作業ディレクトリは **host repo を `/workspace` に直接 rw bind**。host 側で編集したファイルが即座に container 側に反映される。build 成果物 (`bin/`, `obj/`, `python/.venv/` 等) は host 側にも現れるが、すべて `.gitignore` 済み。`/source` ro bind + rsync 方式は廃止 (`dev` user の UID/GID が host と一致しているため所有権問題は発生しない)
- Resonite フォルダは `/resonite` に **read-only bind** のみ (FrooxEngine.dll 等の HintPath 参照専用; mod の deploy 先ではない)
- Gale プロファイル (`./gale/`) は **`/workspace/gale` 経由で参照** する (専用 bind は廃止)。`docker-compose.yml` の `environment.GalePath: /workspace/gale` が csproj の deploy 先を解決
- コンテナ内 `dev` user の **UID/GID を host user に一致** させて build (`HOST_UID` / `HOST_GID` を build-arg で渡す)。これにより `deploy-mod` で出力された DLL/PDB が host user 所有になり、host 側 git からそのまま見える
- NuGet / uv のキャッシュは **named volume** にマウントして再ビルドを高速化 (`/home/dev/.nuget` / `/home/dev/.cache/uv`)
- `csharpier` / `tcli` 等の .NET CLI ツールは **`.config/dotnet-tools.json` の local tool** として固定し、`dotnet tool restore` + `dotnet <tool>` で呼び出す (global tool + PATH 操作は採らない)。`betterproto2_compiler` 等は `uv` 経由

## コマンド

`just` レシピを使う（`uv run` / `dotnet` / `protoc` をラップ）。具体的な recipe は `justfile` を実装するときに固める想定だが、最低限以下の名前を提供する:

| レシピ                 | 役割                                                                                                                                           |
| ---------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `just gen-proto`       | `scripts/gen_proto.sh` で `.proto` から Python 側コードを生成 (C# 側は csproj の `<Protobuf>` が build-time に生成するためノータッチ)          |
| `just decompile`       | `scripts/decompile.sh` で Resonite first-party DLL を ILSpy (`ilspycmd`) で project 形式で `decompiled/` に展開。`.env` の `ResonitePath` 必須 |
| `just log`             | host 側で `gale/BepInEx/LogOutput.log` (Gale 経由起動時) を `tail -F` で追従 (Resonite 再起動を跨いで再追従)。print-debug の主経路             |
| `just deploy-mod`      | `just mod-build` を呼び、csproj の PostBuild Target 経由で `gale/BepInEx/plugins/ResoniteIO/` に DLL+PDB を配置 (DLL 未配置なら exit 1)        |
| `just init`            | host 側で初回 setup。docker / docker compose v2 検出 → `.env` セットアップ → `ResonitePath` 検証 → Gale プロファイル設置確認を順に実施 (冪等)  |
| `just check-gale`      | Gale プロファイル (`./gale/`) に必須 plugin (BepisLoader / BepInExResoniteShim / BepisResoniteWrapper) が揃っているか検証 (不足あれば exit 1)  |
| `just mod-pack`        | `dotnet build -c Release -t:PackTS` で Thunderstore 配布用 zip を `mod/build/` に生成 (`tcli` ラップ)                                          |
| `just format`          | C# (`csharpier`) と Python (`ruff format` + `ruff check --fix`) を両方走らせる                                                                 |
| `just test`            | C# (`dotnet test`) と Python (`pytest -v --cov`) を両方走らせる                                                                                |
| `just type`            | Python の `pyright` を `python/src/` に対し strict 実行                                                                                        |
| `just build`           | C# mod を `dotnet build -c Release`                                                                                                            |
| `just run`             | `format` → `gen-proto` → `build` → `test` → `type` を直列実行                                                                                  |
| `just clean`           | `clean-py` と `mod-clean` を実行                                                                                                               |
| `just mod-clean`       | `mod/{bin,obj,build}` を削除し、`gale/BepInEx/plugins/ResoniteIO/` の中身も撤去 (ディレクトリ自体は deploy 先として残す)                       |
| `just container-build` | Docker image をビルド (debian + .NET 10 SDK + uv + protoc + dotnet local tools)。`--no-cache` 固定                                             |
| `just container-up`    | サービスをバックグラウンド起動。`ResonitePath` / `XDG_RUNTIME_DIR` が未設定なら fail (Gale プロファイル確認は `just init` の責務)              |
| `just container-init`  | container 内で deps 解決 (`dotnet tool restore` + `uv sync` + `pre-commit install` + Claude settings symlink)。bind mount なので rsync は無い  |
| `just container-shell` | コンテナ内 bash に attach (`/workspace` カレント)                                                                                              |
| `just container-down`  | サービス停止 (volume は保持)                                                                                                                   |
| `just container-clean` | image / cache volume / network 完全削除 (destructive、host repo には影響しない)                                                                |

サブコマンド分離が必要な場合の補助レシピ:

- `just py-test` / `just py-type` / `just py-format` — Python 側のみ
- `just mod-build` / `just mod-test` / `just mod-format` / `just mod-pack` / `just mod-clean` — C# 側のみ

細かい制御が必要な場合のフォールバック (いずれも **コンテナ内 shell** で実行する):

- 単一 Python テスト: `cd python && uv run pytest tests/resoio/test_session.py -v`
- 単一パスへの pyright: `cd python && uv run pyright src/resoio/session.py`
- C# 単一プロジェクトのビルド: `cd mod && dotnet build src/ResoniteIO/ResoniteIO.csproj`

### CI 整合性

- `.proto` を変更した場合は **必ず** `just gen-proto` を再実行し、生成物の差分も同じ commit に含める。CI は再生成して diff を取るチェックを入れる予定（plan §3.E）

## 実行環境の注意点

**ホスト側に必要なものは `docker` / `docker compose v2` / `just` の 3 つだけ**。
.NET / uv / protoc / pre-commit はすべてコンテナ内に閉じている。

### Resonite クライアント

- **通常クライアント上で動作** (Camera 描画が必要なため Headless は不可)
- **Resonite 自体は host で起動** (Steam)。コンテナは build / deploy 専用で、Resonite を中で動かすことはしない
- Steam で Resonite をインストール: Linux ネイティブ FrooxEngine + Proton 経由 Renderite
- `.env` の **`ResonitePath`** に Resonite の実行ファイルディレクトリ (`Resonite.exe` / `FrooxEngine.dll` が置かれている場所) を絶対パスで指定する。これは **FrooxEngine.dll の HintPath 参照専用**で、mod の deploy 先ではない
- リモート開発時は Sunshine + Moonlight を想定 (plan §3.A)
- 開発用ワールドは不要 (ワールド非依存に設計)

#### mod loader = Gale プロファイル方式

**ホスト Resonite には BepisLoader を直接インストールしない** (Vanilla 維持)。代わりに [Gale](https://github.com/Kesomannen/gale) (v1.5.4+) のカスタムプロファイル機能で repo root の `./gale/` を mod sandbox にする。`just init` がこのセットアップを案内するが、手動で進める場合の手順は以下:

1. Gale で profile を新規作成し、パスを `<repo>/gale` に指定 (**指定先は EMPTY である必要があり、`./gale/` を事前に作らない**)
2. profile に以下を install:
   - `ResoniteModding-BepisLoader` (>=1.5.1)
   - `ResoniteModding-BepInExResoniteShim` (>=0.9.3)
   - `ResoniteModding-BepisResoniteWrapper` (>=1.0.2)
3. Gale で Resonite を起動すると `LinuxBootstrap.sh` がプロファイル版に差し替わり、BepInEx が有効化される
4. `just check-gale` (または `just init`) で必須 DLL の在中を検証
5. `just deploy-mod` で `gale/BepInEx/plugins/ResoniteIO/` に DLL+PDB が配置される (deploy 先 dir は csproj の `<Copy>` が自動 mkdir する)

ホスト Resonite を Vanilla で起動 (Gale を介さず Steam から直接起動) した場合は mod は読み込まれない。注意点: Gale 経由起動後にホスト Resonite ディレクトリへ `hookfxr.ini` (`enable=true`) 等が残る場合がある。Vanilla 復帰時は確認すること。

実機での mod load 検証手順は [mod/tests/manual/load-verification.md](mod/tests/manual/load-verification.md) を参照。

### Renderite IPC のドキュメント不足

Camera readback の実装は **decompile を読みながら**進める前提（plan §7 リスク）。`just decompile` で `decompiled/` 配下に Resonite first-party DLL を ILSpy (`ilspycmd`) で project 形式に展開できる (gitignore 済み)。手探りになる箇所はその場の発見をコメントでは残さず、`.claude/memory/` に feedback として残すこと。

### Debug 経路

mod は Resonite (host プロセス) に in-process でロードされるため、container 内から直接 attach する経路はない。基本戦略は **print-debug + ログ tailing**:

- C# 側は `ResoniteIOPlugin.Log`（BepInEx `ManualLogSource`）から `LogInfo` / `LogDebug` 等を出す。出力先は Gale 経由起動時に **`gale/BepInEx/LogOutput.log`** (プロファイル側) になる想定 (Gale が起動時に profile の `BepInEx/` を Resonite に差し向ける)。Phase 6 で実機確認次第確定
- host 側で `just log` を別ターミナルで走らせ、`tail -F` で追従する (Resonite 再起動・ログローテーションを跨いで再 attach)
- Python 側は通常の `logging` でクライアント側の挙動を確認する

.NET debugger attach (host IDE → Resonite プロセス) は Step 3 以降で必要になった時に整備する。PDB は `deploy-mod` 時に DLL と一緒に配置済みなのでシンボル解決の前提は満たしている。

### ライセンス・ToS

Resonite は明示的な研究用 bot 規定なし。慣習的には黙認〜歓迎（plan §7）。商用化や派手な公開実験を始める前にユーザーに確認する。

## コーディング規約

### 共通

- 通信データ型は **pyright strict をクリアする型付け**（plan §1 非機能要件）
- 各モダリティは他のモダリティに依存しない（片方だけ使う構成も可能）
- グローバルな clock や barrier は持たない。各ストリームに **タイムスタンプ** を付与し、必要な同期は受信側で行う（plan §2 同期戦略）

### C# 側

- **Core/Mod 層の責務を混ぜない**: `ResoniteIO.Core` (pure library) は BepInEx / FrooxEngine / Renderite を一切参照せず、`ResoniteIO` (mod) は engine bridging のみ。新規モダリティは Core に `<Modality>Service` + `I<Modality>Bridge`、Mod に `FrooxEngine<Modality>Bridge` を 1 ペアで追加する
- 名前空間: Core 側は `ResoniteIO.Core.<Modality>`、Mod 側は `ResoniteIO.Bridge` (engine 実装) と `ResoniteIO` (Plugin 本体)
- `Nullable=enable` + `TreatWarningsAsErrors=true` を `.csproj` で必ず有効にする
- gRPC server は **別スレッドで動作** させ、FrooxEngine 本体スレッドをブロックしない（plan Step 2）。Service 実装は Core 側にあり engine を知らないため、engine 依存処理は Bridge IF 経由で同期/非同期にディスパッチする
- LocalUser 駆動など FrooxEngine API を呼ぶ箇所 (Mod 側 Bridge 実装) は engine の update tick 上にディスパッチする必要がある可能性大。スレッド要件はモジュールごとに調査して `.claude/memory/` に書き残すこと

### Python 側

- パッケージ名は `resoio`、import 名は `resoio`
- PEP 561 typed (`py.typed` 同梱)
- バージョンは `pyproject.toml` の `[project].version` を真値とし、`resoio.__version__` は `importlib.metadata` 経由で読む。他の場所にバージョンをハードコードしない
- カプセル化: クラスの内部実装の詳細や `__init__` で設定される属性は原則 private (`_` prefix)。外部から参照する必要があるものだけ public にする
- private モジュール規約: テストを書かないモジュールは `_` prefix、書くモジュールは prefix なし。外部公開は親 `__init__.py` の `__all__` で別軸として集約

## テスト方針

### 基本原則

- 必要十分なテストのみ。過剰なテストは避ける
- 内部実装の詳細はテストせず、公開インターフェースと振る舞いをテストする
- Python のテスト関数に戻り値の型アノテーションは不要

### テストレイアウト (Python)

`python/tests/` は `python/src/resoio/` の構造を 1 対 1 でミラーリングする:

- `src/resoio/foo.py` ↔ `tests/resoio/test_foo.py`
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

- xunit
- **Core 側** (`ResoniteIO.Core.Tests`): Resonite 非依存なので **Kestrel ラウンドトリップを含む統合テストを書ける**。tmp_path UDS に SessionHost を bind、`Grpc.Net.Client` から実 RPC を投げて echo + timestamp を検証する。proto 変換 / 状態機械 / Service ロジックもここで検証
- **Mod 側** (`ResoniteIO.Tests`): FrooxEngine 依存があるため smoke test と Bridge adapter ロジック (engine API を呼ばない範囲) のみ。実 engine を要するシナリオは `mod/tests/manual/` に Markdown 手順書として残す

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
  - `feat(proto): session.proto に Ping RPC を追加`
  - `feat(mod/session): UDS gRPC server をエンジン起動時に bind`
  - `feat(python/camera): server-streaming で RGB フレームを受信`

## 自走開発フロー

Claude Code が自律的に実装・検証・コミットを行うためのフロー。

### 基本サイクル

1. **要件確認**: [.claude/resonite_io_plan.md](.claude/resonite_io_plan.md) の該当 Step を読み、スコープと未解決事項を把握する
2. **作業ブランチ作成**: `main` から `<種別>/<日付>/<内容>` で作業ブランチを切る
3. **作業はコンテナ内で行う** (`just container-shell` で attach。ビルド/テスト/gen-proto はすべてコンテナ内で実行)
4. **実装**: コードを書く（C# / Python / proto のどれか、もしくは複数）
5. **検証**:
   - proto を変更した場合は `just gen-proto` を再実行し生成物を含めて差分を確認
   - `just run` がすべてパスすることを確認する
6. **コミット**: 細かい単位でコミットし、1 コミットに複数の関心事を混ぜない
7. **繰り返し**: 4-6 を機能単位で繰り返す

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
