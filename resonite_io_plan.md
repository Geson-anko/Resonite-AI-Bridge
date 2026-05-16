# Resonite IO 実装計画

## 1. プロジェクト概要

**Resonite IO** は、Resonite を AI エージェントの実行環境として利用するための双方向 IPC ブリッジ。

設計思想は **強化学習的な抽象化ではなく、リアルタイムロボティクス的な設計**。`Observation/Action` の抽象レイヤーは持たず、`Camera` / `Audio` / `Locomotion` といったモダリティごとに独立した機能を提供する。RL の `step()` 同期はスコープ外で、強化学習インターフェイスは Python 側ライブラリで上に構築されるべきもの。

### 機能要件(初期スコープ)

モダリティ単位で実装する:

- **Camera**: エージェント一人称視点の RGB フレームストリーミング
- **Audio**: 音声の入出力ストリーミング
- **Locomotion**: 移動・姿勢制御
- **Manipulation**: Hand pose / Grab / Release
- (将来: 視線・proprioception・触覚など)

各モダリティは **独立した非同期ストリーム** として動作する。Camera と Audio は受信側、Locomotion と Manipulation は送信側、というように単方向に近いが、技術的にはすべて gRPC streaming。

### スコープ外

- RL の `step()` 同期インターフェイス (Python 側ライブラリの責務)
- ワールド作者定義 API (ProtoFlux Dynamic Impulse 経由)
- マルチユーザー・マルチエージェント (単一ユーザー操作のみ)
- ワールド固有の機能 (ワールド非依存に設計)

### 非機能要件

- Linux 開発環境を一級でサポート (ディストロ非依存)
- 通信データ型は **pyright strict** をクリアする型付け
- 各モダリティが他のモダリティに依存しない (片方だけ使う構成も可能)

### 設計レイヤー

実装は **コア層** と **mod 層** の二層に分離する。コア層は Resonite に一切依存しないピュアな C# / Python ライブラリとして実装し、mod 層は engine bridging のみを担う薄いアダプタとする。

- **コア層** (`ResoniteIO.Core` / `resoio`): gRPC server / client、UDS lifecycle、proto handler、各モダリティのドメインロジック。BepInEx / FrooxEngine / Renderite を参照しない。実機 Resonite なしで Kestrel ラウンドトリップを含む統合テストが書ける
- **mod 層** (`ResoniteIO`): BepInEx Plugin として Resonite に in-process でロードされ、コア層が要求する callback interface (`ISessionBridge`, `ICameraBridge`, …) を FrooxEngine API で実装する純粋な adapter。ドメインロジックは持たない
- **Python 層** (`resoio`): すでにピュア Python であり、gRPC client のみ。Resonite には依存しない

依存方向: **Core ← Mod** (Mod が Core を参照、逆は禁止)。これにより将来 Crystite 方式の独自ホストや軽量レンダラへ移植する際、Core 層をそのまま再利用できる。

______________________________________________________________________

## 2. アーキテクチャ概要

```text
            [Python Process]                    [Resonite Process]
   ┌────────────────────────────┐    ┌─────────────────────────────────────────┐
   │  resoio (Python pkg)       │    │  ResoniteIO (BepisLoader mod, adapter)  │
   │   ├ Camera client          │    │   ├ FrooxEngineCameraBridge             │
   │   ├ Audio client           │    │   ├ FrooxEngineAudioBridge              │
   │   ├ Locomotion client      │    │   ├ FrooxEngineLocomotionBridge         │
   │   ├ Manipulation client    │    │   ├ FrooxEngineManipulationBridge       │
   │   └ Session (gRPC base)    │    │   └ FrooxEngineSessionBridge            │
   │            ▲               │    │            │  (DI: ISessionBridge 等)   │
   │            │               │    │            ▼                            │
   │            │UDS gRPC       │    │  ResoniteIO.Core (pure C# library)      │
   │            │               │←──→│   ├ CameraService                       │
   │            │               │UDS │   ├ AudioService                        │
   │            │               │gRPC│   ├ LocomotionService                   │
   │            │               │    │   ├ ManipulationService                 │
   │            │               │    │   └ SessionService / SessionHost        │
   └────────────┘               │    └─────────in-process─────────────────────┘
                                                    │
                                              [FrooxEngine]
                                                    ↓ shmem IPC
                                              [Renderite (Unity)]
```

依存方向: **Python client → UDS gRPC → Core ← Mod (Bridge 注入)**。Core は Resonite を知らない。

### 採用方針

- **Mod 方式** (BepisLoader) として実装。Resonite の認証・同期・アセットを丸ごと利用
- 通常クライアント上で動作 (描画が必要なため Headless は不可)
- C# 側のモジュール構造と Python 側のモジュール構造を **モダリティ単位でミラーリング**
- **コア機能は Resonite 非依存** (`ResoniteIO.Core`)。BepInEx / FrooxEngine / Renderite に依存するコードは `ResoniteIO` (mod) に局所化する
- **mod 層は engine bridging のみ**: コアが要求する Bridge インターフェイスを FrooxEngine API で実装し、`OnEngineReady` でコアを起動・shutdown で停止する純粋なアダプタ
- **Bridge インターフェイスはモダリティ単位で分割**: `ISessionBridge` / `ICameraBridge` / `IAudioBridge` / `ILocomotionBridge` / `IManipulationBridge` のように独立 IF を保ち、肥大化を防ぐ

### モダリティ別の実装方針

| モダリティ   | Core 側 Service                  | Mod 側 Bridge 実装                                                       | 通信パターン  |
| ------------ | -------------------------------- | ------------------------------------------------------------------------ | ------------- |
| Session      | `SessionService` / `SessionHost` | `FrooxEngineSessionBridge` (`FocusedWorld` / `LocalUser` を露出)         | unary         |
| Camera       | `CameraService`                  | `FrooxEngineCameraBridge` (`Camera` 生成 + `RenderTextureProvider` 読出) | server-stream |
| Audio        | `AudioService`                   | `FrooxEngineAudioBridge` (要調査: Audio Output / Mic 経路)               | bidi-stream   |
| Locomotion   | `LocomotionService`              | `FrooxEngineLocomotionBridge` (`LocalUser.Root` 直接駆動)                | client-stream |
| Manipulation | `ManipulationService`            | `FrooxEngineManipulationBridge` (Hand Slot Pose + `Grabber`)             | client-stream |

### 同期戦略

**完全非同期・各モダリティ独立**。

- Camera は描画フレームが出来次第 push
- Audio は音声サンプルバッファが満ちた段階で push
- Locomotion / Manipulation は Python 側のタイミングで送信
- グローバルな clock や barrier は持たない

各ストリームに **タイムスタンプ** を付与し、必要な同期は受信側 (Python) で行う。

______________________________________________________________________

## 3. Step 0: 開発環境・プロジェクトセットアップ

> **ステータス: 完了**。Docker ベースの開発環境に切り替わったため、当初想定していた host 直インストールの `setup.sh` は廃止。

### A. Resonite 実行環境 (Linux)

- [x] Steam で Resonite をインストール (Linux ネイティブ FrooxEngine + Proton 経由 Renderite)
- [x] BepisLoader を導入
- [x] Sunshine + Moonlight でリモートデスクトップ動作確認
- ~~開発用プライベートワールド~~ (不要: ワールド非依存に設計)

### B. 開発ツールチェーン (Docker 化)

ホスト側に必要なのは **`docker` / `docker compose v2` / `just` の 3 つだけ**。
.NET SDK / uv / protoc / pre-commit はすべて `debian:bookworm-slim` ベースの単一 image に同梱。

- [x] `Dockerfile` (.NET 10 SDK / uv / just / protoc + shellcheck/shfmt)
- [x] `docker-compose.yml` (`name: resonite-io-${USER}` で user 単位の名前空間、host repo を `/workspace` に rw bind、`${ResonitePath}` を `/resonite` に ro bind、Gale プロファイルは `/workspace/gale` 経由で参照)
- [x] `scripts/container-init.sh` (container 内 deps restore のみ: `dotnet tool restore` + `uv sync` + `pre-commit install` + Claude settings symlink。bind mount のため rsync は無い)
- [x] `just init` (host 側 one-time setup: docker / `.env` / Gale プロファイル確認)
- [x] dotnet local tools (`.config/dotnet-tools.json`): `csharpier`, `tcli` (Thunderstore packaging), `ilspycmd` (decompile)
- [x] `pre-commit` (ruff / pyupgrade / docformatter / mdformat / codespell / uv-lock / pygrep / shellcheck / shfmt)
- [x] VSCode 推奨拡張一覧 (`.vscode/extensions.json`): C# Dev Kit / Pylance / Ruff / csharpier / buf / docker など
- ~~`scripts/setup.sh`~~ (廃止: Docker 環境に置き換え)
- [x] **UDS socket 共有ディレクトリの bind**: `$XDG_RUNTIME_DIR/resonite-io/` (= `/run/user/$UID/resonite-io/`) を host / container 双方で同一絶対パスとして bind 共有。`docker-compose.yml` に long-form bind (`/run/user/${HOST_UID}/resonite-io:/run/user/${HOST_UID}/resonite-io:rw`、`create_host_path: false`) と `environment.XDG_RUNTIME_DIR: /run/user/${HOST_UID}` を追加。host 側ディレクトリは `just container-up` が `0700` で事前作成する。socket ファイル名は mod 側で `resonite-{pid}.sock` を自動命名 (Step 2 で実装) し、Python client は `RESONITE_IO_SOCKET` / `RESONITE_IO_SOCKET_DIR` / 既定 (`$XDG_RUNTIME_DIR/resonite-io/`) の優先順で探索する (`.env` への記述は通常不要)。

### C. モノレポ構造 (目標形)

- **リポジトリ名**: `resonite-io`
- **C# コアライブラリ アセンブリ名**: `ResoniteIO.Core` (Step 2 で新設)
- **C# Mod アセンブリ名**: `ResoniteIO` (mod アダプタ層)
- **Python パッケージ名**: `resoio`

下記は Core/Mod 分離後の目標構造。現状 (Step 1 完了時点) では `ResoniteIO.Core` プロジェクトはまだ存在せず、Session/Camera/... のソースは `mod/src/ResoniteIO/` の `.gitkeep` 配下に留まる。Step 2 以降で Core 側にロジックを移していく。

```text
resonite-io/
├── Dockerfile                     # 開発コンテナ image (debian + .NET 10 + uv + protoc)
├── docker-compose.yml             # dev サービス定義 (UID/GID 一致 / repo を /workspace に bind / ResonitePath / XDG_RUNTIME_DIR + UDS bind)
├── justfile                       # ルートタスクランナー (build / test / container-*)
├── buf.yaml                       # proto lint/breaking (modules: proto/)
├── .pre-commit-config.yaml
├── .env.example                   # ResonitePath / UDS override 等の雛形 (.env は gitignore)
│
├── proto/                         # 単一の真実: .proto 定義
│   └── resonite_io/v1/
│       └── session.proto          # Step 1 完了 (Ping RPC)
│                                  # camera/audio/locomotion/manipulation は後続 Step で追加
│
├── mod/                           # C# 側 (.NET 10、二層構成)
│   ├── ResoniteIO.sln
│   ├── Directory.Build.{props,targets}
│   ├── NuGet.config
│   ├── thunderstore.toml          # Thunderstore メタデータ (tcli が読む)
│   ├── icon.png
│   ├── src/
│   │   ├── ResoniteIO.Core/        # ◆ Core 層 (Step 2 で新設、Resonite 非依存)
│   │   │   ├── ResoniteIO.Core.csproj   # Protobuf <Server> + Grpc.AspNetCore.Server
│   │   │   ├── Logging/ILogSink.cs       # BepInEx 非依存のロギング abstraction
│   │   │   ├── Bridge/                   # mod から注入される engine callback IF
│   │   │   │   ├── ISessionBridge.cs
│   │   │   │   ├── ICameraBridge.cs      # Step 3+
│   │   │   │   ├── IAudioBridge.cs       # Step 6+
│   │   │   │   ├── ILocomotionBridge.cs  # Step 4+
│   │   │   │   └── IManipulationBridge.cs# Step 5+
│   │   │   └── Session/
│   │   │       ├── SessionService.cs     # Session.SessionBase 実装
│   │   │       └── SessionHost.cs        # Kestrel UDS host
│   │   └── ResoniteIO/             # ◆ Mod 層 (BepInEx adapter, ProjectReference: Core)
│   │       ├── ResoniteIO.csproj
│   │       ├── ResoniteIOPlugin.cs # BasePlugin + OnEngineReady で Core を起動
│   │       ├── Logging/
│   │       │   └── BepInExLogSink.cs    # ILogSink → ManualLogSource adapter
│   │       └── Bridge/             # FrooxEngine 依存実装 (Core IF の実装)
│   │           ├── FrooxEngineSessionBridge.cs
│   │           ├── FrooxEngineCameraBridge.cs       # Step 3+
│   │           ├── FrooxEngineAudioBridge.cs        # Step 6+
│   │           ├── FrooxEngineLocomotionBridge.cs   # Step 4+
│   │           └── FrooxEngineManipulationBridge.cs # Step 5+
│   └── tests/
│       ├── ResoniteIO.Core.Tests/  # ◆ Kestrel ラウンドトリップ含む統合テスト (Resonite 不要)
│       └── ResoniteIO.Tests/       # mod adapter smoke test (BepInEx 依存)
│
├── python/                        # Python 側 (Resonite 非依存)
│   ├── pyproject.toml             # requires-python >=3.12, deps: betterproto2[grpclib]
│   ├── uv.lock
│   ├── src/resoio/
│   │   ├── __init__.py            # importlib.metadata で __version__ を露出
│   │   ├── py.typed
│   │   ├── session.py             # placeholder (Step 2 で SessionClient 実装)
│   │   └── _generated/            # protoc 出力 (commit)
│   │       └── resonite_io/v1/
│   └── tests/resoio/
│
├── scripts/
│   ├── gen_proto.sh               # .proto → Python コード生成 (C# 側は csproj が build-time に生成)
│   ├── decompile.sh               # ilspycmd で Resonite first-party DLL を decompiled/ に展開
│   ├── container-init.sh          # container 内 deps restore (rsync は無い、bind mount のため)
│   └── lib.sh                     # 共通シェルユーティリティ
│
├── decompiled/                    # ILSpy 出力 (gitignore、`just decompile` で再生成)
├── .claude/                       # Claude Code 規約 + memory
├── .github/workflows/             # (未整備)
└── README.md
```

### D. ビルド・デプロイサイクル

| 経路                          | 役割                                                                            |
| ----------------------------- | ------------------------------------------------------------------------------- |
| `just init`                   | host 側 one-time setup (docker / `.env` / Gale プロファイル確認、冪等)          |
| `just container-build`        | 開発 image をビルド (debian + .NET 10 SDK + uv + protoc + dotnet local tools)   |
| `just container-up` / `-init` | サービス起動 + container 内 deps 解決 (`/workspace` は host repo の bind mount) |
| `just gen-proto`              | Python 側コード生成 (C# は csproj `<Protobuf>` で build-time 生成)              |
| `just deploy-mod`             | `dotnet build` → csproj の PostBuild Target で `$(GalePath)/BepInEx/plugins/`   |
| `just decompile`              | ILSpy で Resonite アセンブリを project 形式で `decompiled/` に展開              |
| `just log`                    | `$(GalePath)/BepInEx/LogOutput.log` を host で `tail -F` (debug 主経路)         |
| `just mod-pack`               | `dotnet build -t:PackTS` で Thunderstore zip を `mod/build/` に生成             |

Python 側は `uv sync` で editable install 含めて完結。

**Debug 戦略**: mod は Resonite (host プロセス) に in-process でロードされるため、container 内から直接 attach する経路はない。Step 2 までは `ResoniteIOPlugin.Log` (BepInEx `ManualLogSource`) からの **print-debug + `just log` でのログ tailing** を主経路とする。`deploy-mod` で PDB も配置済みのため、Step 3 以降で必要になったら host IDE (Rider / VSCode C# Dev Kit) から Resonite プロセスに .NET debugger を attach できる。

将来: BepisLoader の .NET Hot Reload (debugger attach 時)。

### E. CI (GitHub Actions)

**C# 側**:

- ビルド (`resonite-modding-group/setup-resonite-env-action` 利用)
- テスト (xunit)
- Formatter チェック (csharpier)
- Linter (Roslyn analyzers + warnings-as-errors)

**Python 側**:

- pytest
- Formatter (ruff format)
- Linter (ruff check)
- Type-check (**pyright strict**)

**Proto 整合性**:

- `.proto` 変更後に `gen_proto.sh` を再実行した結果が commit 済み生成物と一致するかチェック

______________________________________________________________________

## 4. 残った論点

すべての論点が解決済み。詳細は `§5 決定事項` を参照。

______________________________________________________________________

## 5. 決定事項

- ✅ モノレポ (GitHub 単一リポジトリ)
- ✅ リポジトリ名 `resonite-io` / C# Mod `ResoniteIO` / **Python pkg `resoio`**
- ✅ Python パッケージマネージャ: `uv`
- ✅ IPC: gRPC over Unix Domain Socket
- ✅ Python gRPC スタック: `betterproto2` + `grpclib` (async)
  - **Python 3.12+ 必須** (`python/pyproject.toml` の `requires-python` と pyright `pythonVersion` で固定)
  - 依存は `betterproto2[grpclib]`。**`betterproto2_compiler` は別 distribution として配布されており `[compiler]` extra は存在しない** (PyPI metadata 2026-05 で確認済み)。dev グループに固定し、`uv run protoc --python_betterproto2_out=...` で呼び出す
  - 生成コードは Python dataclass + type hints ネイティブで pyright strict をそのまま通る想定
- ✅ C# / Python のモジュール構造はモダリティ単位でミラーリング
- ✅ 各モダリティは独立非同期ストリーム (RL `step()` なし)
- ✅ ワールド非依存・単一ユーザー操作スコープ
- ✅ 通信データ型は pyright strict 準拠
- ✅ **開発環境は Docker 化** (`debian:bookworm-slim` ベース単一 image)。ホストには `docker` / `docker compose v2` / `just` の 3 つだけ要求。当初想定していた `setup.sh` は廃止
- ✅ 補助ツール: ライセンス MIT、formatter (csharpier / ruff)、type-check (pyright strict)、test (xunit / pytest)
- ✅ **C# Linter/Analyzer**: csharpier + Roslyn analyzers + `Nullable=enable` + `TreatWarningsAsErrors=true` (StyleCop は不採用)
- ✅ **C# Mod SDK**: `Microsoft.NET.Sdk` + BepisLoader 公式 Template の NuGet 群 (`BepInEx.ResonitePluginInfoProps` / `ResoniteModding.BepInExResoniteShim` / `ResoniteModding.BepisResoniteWrapper`)。当初検討した `Remora.Resonite.Sdk` は不採用
- ✅ **C# 側 proto 生成**: `Grpc.Tools` の `<Protobuf>` ItemGroup で `dotnet build` 時に自動生成 (Server スタブのみ)。`gen_proto.sh` は Python 側のみを扱う
- ✅ **dotnet local tools** (`.config/dotnet-tools.json`): `csharpier` / `tcli` / `ilspycmd`。global tool + PATH 操作は採らない
- ✅ **proto lint**: `buf` (`buf.yaml`、`SERVICE_SUFFIX` は除外)
- ✅ **mod deploy**: csproj の PostBuild Target が `$(ResonitePath)/BepInEx/plugins/ResoniteIO/` に Copy する一本化。`scripts/deploy_mod.sh` は廃止
- ✅ **mod 配布**: Thunderstore zip を `dotnet build -t:PackTS` (`tcli` ラップ) で生成
- ✅ **proto スキーマは Step ごとに incremental に詰める** (Step 1 で `session.proto`、Step 3 で `camera.proto`、…)
- ✅ **BepInEx PluginGuid**: `net.mlshukai.resonite-io`
- ✅ **コア機能は Resonite 非依存**。BepInEx / FrooxEngine / Renderite に依存するコードは `mod/src/ResoniteIO/` (mod 層) に局所化する
- ✅ **C# は二層構成**: `ResoniteIO.Core` (pure library) と `ResoniteIO` (BepInEx mod アダプタ)。Mod は Core を `ProjectReference` し、Bridge インターフェイス経由で engine 依存処理を注入する
- ✅ **C# proto 生成は Core 側に集約**。`<Protobuf GrpcServices="Server" />` は `ResoniteIO.Core.csproj` に置く。Mod 側 csproj は Core への ProjectReference のみで proto 直接参照は持たない
- ✅ **C# gRPC server**: `Grpc.AspNetCore.Server` (Kestrel + UDS) を Core 側で使用、`WebApplication.CreateSlimBuilder()` で最小構成 (Reflection 等のオマケは含めない)
- ✅ **UDS socket path**: host と container で `/run/user/${HOST_UID}/resonite-io/` を同一絶対パスで rw bind 共有 (`docker-compose.yml` long-form bind + container 側 `XDG_RUNTIME_DIR` env)。socket ファイル名は mod が `resonite-{pid}.sock` を採用し、1 host 上で複数 Resonite が共存可能。Python client は `RESONITE_IO_SOCKET` (フルパス) → `RESONITE_IO_SOCKET_DIR` → 既定 `$XDG_RUNTIME_DIR/resonite-io/` の順で解決し、ディレクトリ探索時は 1 個なら自動採用 / 複数なら明示指定を要求。host 側ディレクトリは `just container-up` が 0700 で先に作成 (`create_host_path: false` で fail-fast)。
- ✅ **テスト戦略の二層化**:
  - Core 単体: Kestrel ラウンドトリップ含む統合テストを xunit で (Resonite 不要)
  - Mod adapter: BepInEx 依存があるため smoke test のみ
  - Python: in-process server + UDS round-trip で contract を検証
- ✅ **Bridge インターフェイスはモダリティ単位で分割**: `ISessionBridge` / `ICameraBridge` / `IAudioBridge` / `ILocomotionBridge` / `IManipulationBridge` のように独立 IF とし、肥大化を防ぐ

______________________________________________________________________

## 6. 今後のステップ

### Step 1: スケルトン構築 — **完了**

- [x] BepisLoader mod として最小構成で起動確認 (`mod/src/ResoniteIO/ResoniteIOPlugin.cs` — `BasePlugin.Load()` で `ResoniteHooks.OnEngineReady` を購読し起動ログを出力)
- [x] Python `resoio` パッケージのスケルトン (`session.py` は placeholder、`_generated/` に空の package marker、`__version__` は `importlib.metadata` 経由)
- [x] `proto/resonite_io/v1/session.proto` (Ping RPC) を追加
- [x] xunit smoke test (`mod/tests/ResoniteIO.Tests/`) + pytest scaffolding (`python/tests/resoio/`)
- [x] モダリティ別ディレクトリの `.gitkeep` を C# / Python 両側に配置 (Camera / Audio / Locomotion / Manipulation / Session)
- [ ] `Engine.Current.WorldManager.FocusedWorld` から `LocalUser` を引いて Console にログ出力 (Step 2 と合わせて実装する)

### Step 2: gRPC Session — **次に着手**

各 Step は Core/Mod 二層構成を前提に分割する (§設計レイヤー / §5 決定事項)。

- **Core** (`ResoniteIO.Core`): プロジェクト新設、`Grpc.AspNetCore.Server` + `<Protobuf>` を Core 側に集約、`SessionService` (Ping echo + Unix nanos) と `SessionHost` (Kestrel + UDS lifecycle: `$XDG_RUNTIME_DIR/resonite-io/` を `0700` で `mkdir`、`resonite-{Process.GetCurrentProcess().Id}.sock` で bind、起動時に stale socket を `File.Delete`、`AppDomain.ProcessExit` で best-effort `unlink`) を実装。Kestrel ラウンドトリップで xunit 統合テスト (Resonite 不要)
- **Mod** (`ResoniteIO`): `ResoniteIOPlugin` から `SessionHost` を起動、`ISessionBridge` を FrooxEngine 実装 (`FrooxEngineSessionBridge`) で注入、Step 1 宿題である `FocusedWorld` / `LocalUser` のログ出力を Bridge 経由で実現、`AppDomain.ProcessExit` / `Engine.OnShutdown` で graceful stop
- **Python** (`resoio`): `SessionClient` (async context manager) と in-process server (`SessionBase` 継承の echo 実装) を tmp_path UDS で繋ぐ round-trip テスト

### Step 3: Camera モジュール

- **Core**: `CameraService` (server-streaming RGB frame) と `ICameraBridge` 定義、フレーム供給はタイムスタンプ付き
- **Mod**: `FrooxEngineCameraBridge` でエージェント頭部 Slot に `Camera` コンポーネント生成、`RenderTextureProvider` から byte\[\] を取り出す
- **Python**: `CameraClient` で server-stream を受信し、`cv2.imshow` で目視確認 ← **最初の難関**

### Step 4: Locomotion モジュール

- **Core**: `LocomotionService` (client-streaming Pose 更新) と `ILocomotionBridge` 定義
- **Mod**: `FrooxEngineLocomotionBridge` で `LocalUser.Root` の Position/Rotation を engine update tick 上で設定
- **Python**: `LocomotionClient` から制御して動くことを確認

### Step 5: Manipulation モジュール

- **Core**: `ManipulationService` (Hand Pose / Grab / Release) と `IManipulationBridge` 定義
- **Mod**: `FrooxEngineManipulationBridge` で Hand Slot Pose 制御 + `Grabber` の Pick/Release

### Step 6: Audio モジュール

- **Core**: `AudioService` (bidi-streaming) と `IAudioBridge` 定義
- **Mod**: `FrooxEngineAudioBridge` で音声出力 (世界 → Python) と音声入力 (Python → 自分の声) の経路実装 — Audio Output / Mic 経路は要調査

### Step 7 (将来): 独自クライアント / 並列化

- Crystite 方式の独自ホスト検討 — `ResoniteIO.Core` を別 host から再利用 (BepInEx 不要なため移植が容易)
- 軽量レンダラへの置き換え (PJB blog 参照)

______________________________________________________________________

## 7. リスク・未解決事項

- **Renderite IPC のドキュメント不足**: Camera readback の実装は decompile 読みが前提
- **Audio 取得経路が未調査**: FrooxEngine 側でユーザーが聞いている音声を取得する API があるか要調査 (なければ Renderite 側のフックが必要かも)
- **ライセンス・ToS**: Resonite は明示的な研究用 bot 規定なし。慣習的には黙認〜歓迎
- **マルチエージェント**: スコープ外だが、将来は 1 Resonite インスタンス = 1 エージェントのコスト問題が出てくる
- **Kestrel が引き連れる依存と Resonite 同梱 DLL の version skew**: `Grpc.AspNetCore.Server` は `Microsoft.AspNetCore.*` / `Microsoft.Extensions.*` / `System.IO.Pipelines` を芋づる式に持ち込む。Core に閉じ込めても mod ロード時に同一 AppDomain で Resonite 同梱バージョンと衝突しうる。Step 2 着手時に `just decompile` で Resonite 同梱バージョンを確認し `.claude/memory/` に記録する。`PrivateAssets="all"` / `Private="False"` の慎重な設定が必要
- **UDS socket の host ↔ container 共有**: `/run/user/${HOST_UID}/resonite-io/` を両側同一絶対パスで rw bind し、`HOST_UID` 一致 (justfile が `id -u` から注入) で perms を揃える。host 側ディレクトリは `just container-up` が 0700 で事前作成 (Docker 任せだと root 所有になる)。`$XDG_RUNTIME_DIR` 未設定環境 (非 systemd-logind セッション) では fail-fast し、`/tmp` fallback は当面持たない。stale socket は SessionHost が bind 直前に `File.Delete` で除去 (PID 一致や mtime チェックは Step 2 ではやらず、`unlink` 前提で十分)。SIGKILL 等で `unlink` を逃した socket も次回起動時に上書き除去される。マルチユーザー環境では `HOST_UID` 名前空間で自然に分離される。
- **Bridge インターフェイスの粒度**: モダリティが増えるにつれ IF が肥大化する懸念。各モダリティで独立 IF (`ISessionBridge`, `ICameraBridge`, …) として分割する方針 (§2 採用方針)
- **`BasePlugin` に Unload 相当が無い**: BepInEx 6 の `BasePlugin` には mod 終了時 hook が無い。`Engine.OnShutdown` 系 API の有無を Step 2 着手時に decompile で確認し、無ければ `AppDomain.ProcessExit` で best-effort 停止する (SIGKILL されたら socket file は残る)

______________________________________________________________________

## 8. 参考リンク

- BepisLoader / Resonite Modding: <https://modding.resonite.net/>
- Remora.Resonite.Sdk: <https://www.nuget.org/packages/Remora.Resonite.Sdk>
- Crystite (custom headless): <https://github.com/Nihlus/Crystite>
- 独自レンダラ実装記録 (PJB blog): <https://slugcat.systems/post/25-04-25-making-a-custom-resonite-renderer/>
- Camera コンポーネント wiki: <https://wiki.resonite.com/Component:Camera>
- betterproto2: <https://github.com/betterproto/python-betterproto2>
- grpclib: <https://github.com/vmagamedov/grpclib>
- Grpc.AspNetCore.Server (Kestrel + UDS): <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore>
