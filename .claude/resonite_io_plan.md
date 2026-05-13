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

______________________________________________________________________

## 2. アーキテクチャ概要

```text
            [Python Process]                    [Resonite Process]
   ┌────────────────────────────┐    ┌────────────────────────────────┐
   │  resoio (Python pkg)       │    │  ResoniteIO (BepisLoader mod)  │
   │   ├ Camera client          │    │   ├ Camera                     │
   │   ├ Audio client           │    │   ├ Audio                      │
   │   ├ Locomotion client      │←──→│   ├ Locomotion                 │
   │   ├ Manipulation client    │UDS │   ├ Manipulation               │
   │   └ Session (gRPC base)    │gRPC│   └ Session (gRPC server)      │
   └────────────────────────────┘    └─────────in-process─────────────┘
                                                    │
                                              [FrooxEngine]
                                                    ↓ shmem IPC
                                              [Renderite (Unity)]
```

### 採用方針

- **Mod 方式** (BepisLoader) として実装。Resonite の認証・同期・アセットを丸ごと利用
- 通常クライアント上で動作 (描画が必要なため Headless は不可)
- C# 側のモジュール構造と Python 側のモジュール構造を **モダリティ単位でミラーリング**

### モダリティ別の実装方針

| モダリティ   | 取得方法 (C# 側)                                             | 通信パターン  |
| ------------ | ------------------------------------------------------------ | ------------- |
| Camera       | `Camera` コンポーネント → `RenderTextureProvider` → byte\[\] | server-stream |
| Audio        | (要調査: FrooxEngine の Audio Output / Mic 経路)             | bidi-stream   |
| Locomotion   | `LocalUser.Root` 直接駆動 (Position/Rotation)                | client-stream |
| Manipulation | Hand Slot Pose 制御 + `Grabber`                              | client-stream |

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
- [x] `docker-compose.yml` (`name: resonite-io-${USER}` で user 単位の名前空間、`/source` ro bind + `/workspace` named volume、`${ResonitePath}` の重ね bind)
- [x] `scripts/container-init.sh` (`/workspace` への rsync bootstrap + `dotnet tool restore` + `uv sync`)
- [x] dotnet local tools (`.config/dotnet-tools.json`): `csharpier`, `tcli` (Thunderstore packaging), `ilspycmd` (decompile)
- [x] `pre-commit` (ruff / pyupgrade / docformatter / mdformat / codespell / uv-lock / pygrep / shellcheck / shfmt)
- [x] VSCode 推奨拡張一覧 (`.vscode/extensions.json`): C# Dev Kit / Pylance / Ruff / csharpier / buf / docker など
- ~~`scripts/setup.sh`~~ (廃止: Docker 環境に置き換え)

### C. モノレポ構造 (実装済み)

- **リポジトリ名**: `resonite-io`
- **C# Mod アセンブリ名**: `ResoniteIO`
- **Python パッケージ名**: `resoio`

```text
resonite-io/
├── Dockerfile                     # 開発コンテナ image (debian + .NET 10 + uv + protoc)
├── docker-compose.yml             # dev サービス定義 (host UID/GID 一致 / ResonitePath bind)
├── justfile                       # ルートタスクランナー (build / test / container-*)
├── buf.yaml                       # proto lint/breaking (modules: proto/)
├── .pre-commit-config.yaml
├── .env.example                   # ResonitePath 等の雛形 (.env は gitignore)
│
├── proto/                         # 単一の真実: .proto 定義
│   └── resonite_io/v1/
│       └── session.proto          # Step 1 完了 (Ping RPC)
│                                  # camera/audio/locomotion/manipulation は後続 Step で追加
│
├── mod/                           # C# 側 (BepisLoader mod, .NET 10)
│   ├── ResoniteIO.sln
│   ├── Directory.Build.{props,targets}
│   ├── NuGet.config
│   ├── thunderstore.toml          # Thunderstore メタデータ (tcli が読む)
│   ├── icon.png
│   ├── src/ResoniteIO/
│   │   ├── ResoniteIO.csproj
│   │   ├── ResoniteIOPlugin.cs     # BasePlugin + OnEngineReady フック
│   │   ├── Session/                # (.gitkeep のみ)  Step 2 で実装
│   │   ├── Camera/                 # (.gitkeep のみ)  Step 3 で実装
│   │   ├── Audio/                  # (.gitkeep のみ)
│   │   ├── Locomotion/             # (.gitkeep のみ)
│   │   └── Manipulation/           # (.gitkeep のみ)
│   └── tests/ResoniteIO.Tests/    # xunit (smoke test のみ)
│
├── python/                        # Python 側
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
│   ├── container-init.sh          # /workspace への bootstrap + 依存解決
│   └── lib.sh                     # 共通シェルユーティリティ
│
├── decompiled/                    # ILSpy 出力 (gitignore、`just decompile` で再生成)
├── .claude/                       # Claude Code 規約 + memory
├── .github/workflows/             # (未整備)
└── README.md
```

### D. ビルド・デプロイサイクル

| 経路                          | 役割                                                                              |
| ----------------------------- | --------------------------------------------------------------------------------- |
| `just container-build`        | 開発 image をビルド (debian + .NET 10 SDK + uv + protoc + dotnet local tools)     |
| `just container-up` / `-init` | サービス起動 + `/workspace` への bootstrap                                        |
| `just gen-proto`              | Python 側コード生成 (C# は csproj `<Protobuf>` で build-time 生成)                |
| `just deploy-mod`             | `dotnet build` → csproj の PostBuild Target で `$(ResonitePath)/BepInEx/plugins/` |
| `just decompile`              | ILSpy で Resonite アセンブリを project 形式で `decompiled/` に展開                |
| `just log`                    | `$(ResonitePath)/BepInEx/LogOutput.log` を host で `tail -F` (debug 主経路)       |
| `just mod-pack`               | `dotnet build -t:PackTS` で Thunderstore zip を `mod/build/` に生成               |

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

- C# 側で gRPC サーバ起動 (UDS bind)、別スレッドで動作
- Python 側から `Session.Ping` RPC が通ることを確認
- セッション管理 (接続/切断)
- Step 1 の宿題である `FocusedWorld` / `LocalUser` の取得もこの Step でカバーする

### Step 3: Camera モジュール

- エージェント頭部 Slot に `Camera` コンポーネント生成
- `RenderTextureProvider` から byte\[\] を取り出す
- gRPC server-streaming で Python に push
- Python 側で `cv2.imshow` 目視確認 ← **最初の難関**

### Step 4: Locomotion モジュール

- `LocalUser.Root` の Position/Rotation を設定する RPC
- Python から制御して動くことを確認

### Step 5: Manipulation モジュール

- Hand Slot Pose 制御
- `Grabber` (Pick / Release)

### Step 6: Audio モジュール

- 音声出力 (世界 → Python) の取り出しパス調査・実装
- 音声入力 (Python → 世界 / 自分の声として) 実装

### Step 7 (将来): 独自クライアント / 並列化

- Crystite 方式の独自ホスト検討
- 軽量レンダラへの置き換え (PJB blog 参照)

______________________________________________________________________

## 7. リスク・未解決事項

- **Renderite IPC のドキュメント不足**: Camera readback の実装は decompile 読みが前提
- **Audio 取得経路が未調査**: FrooxEngine 側でユーザーが聞いている音声を取得する API があるか要調査 (なければ Renderite 側のフックが必要かも)
- **ライセンス・ToS**: Resonite は明示的な研究用 bot 規定なし。慣習的には黙認〜歓迎
- **マルチエージェント**: スコープ外だが、将来は 1 Resonite インスタンス = 1 エージェントのコスト問題が出てくる

______________________________________________________________________

## 8. 参考リンク

- BepisLoader / Resonite Modding: <https://modding.resonite.net/>
- Remora.Resonite.Sdk: <https://www.nuget.org/packages/Remora.Resonite.Sdk>
- Crystite (custom headless): <https://github.com/Nihlus/Crystite>
- 独自レンダラ実装記録 (PJB blog): <https://slugcat.systems/post/25-04-25-making-a-custom-resonite-renderer/>
- Camera コンポーネント wiki: <https://wiki.resonite.com/Component:Camera>
- betterproto2: <https://github.com/betterproto/python-betterproto2>
- grpclib: <https://github.com/vmagamedov/grpclib>
