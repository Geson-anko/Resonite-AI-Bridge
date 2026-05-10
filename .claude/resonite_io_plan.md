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

```
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

### A. Resonite 実行環境 (Linux)

- [ ] Steam で Resonite をインストール (Linux ネイティブ FrooxEngine + Proton 経由 Renderite)
- [ ] BepisLoader を導入
- [ ] Sunshine + Moonlight でリモートデスクトップ動作確認
- ~~開発用プライベートワールド~~ (不要: ワールド非依存に設計)

### B. 開発ツールチェーン

- [ ] .NET 10 SDK
- [ ] `Remora.Resonite.Sdk` を NuGet 参照
- [ ] VSCode + C# Dev Kit + Python 拡張 (Remote SSH)
- [ ] `protoc` + プラグイン (両言語コード生成)
- [ ] **uv** (Python パッケージマネージャ)
- [ ] **`scripts/setup.sh`**: 上記すべてを **任意の Linux ディストリ** で一発インストールするシェルスクリプト
  - 公式バイナリインストーラを優先 (`dotnet-install.sh`, `uv` の curl installer, `protoc` の GitHub releases バイナリ)
  - ディストロ依存のパッケージマネージャ (apt/dnf/pacman) は最小限に留める
  - `csharpier` 等は `dotnet tool install -g`、`betterproto2_compiler` 等は `uv` 経由

### C. モノレポ構造

- **リポジトリ名**: `resonite-io`
- **C# Mod アセンブリ名**: `ResoniteIO`
- **Python パッケージ名**: `resoio`

```
resonite-io/
├── proto/                         # 単一の真実: .proto 定義
│   └── resonite_io/v1/
│       ├── session.proto          # セッション管理・ヘルスチェック
│       ├── camera.proto
│       ├── audio.proto
│       ├── locomotion.proto
│       └── manipulation.proto
│
├── mod/                           # C# 側 (BepisLoader mod)
│   ├── ResoniteIO.sln
│   ├── src/ResoniteIO/
│   │   ├── ResoniteIO.csproj
│   │   ├── Session/                # gRPC server 起点・セッション管理
│   │   ├── Camera/                 # RGB フレーム取得・配信
│   │   ├── Audio/                  # 音声入出力
│   │   ├── Locomotion/             # LocalUser 駆動
│   │   └── Manipulation/           # Hand / Grabber 制御
│   └── tests/
│
├── python/                        # Python 側
│   ├── pyproject.toml
│   ├── src/resoio/
│   │   ├── __init__.py
│   │   ├── session.py             # gRPC channel 共有・接続管理
│   │   ├── camera.py              # ← C# Camera をミラー
│   │   ├── audio.py               # ← C# Audio をミラー
│   │   ├── locomotion.py          # ← C# Locomotion をミラー
│   │   ├── manipulation.py        # ← C# Manipulation をミラー
│   │   └── _generated/            # protoc 出力 (commit する)
│   ├── examples/
│   └── tests/
│
├── scripts/
│   ├── setup_ubuntu.sh            # 全依存を一発インストール
│   ├── gen_proto.sh               # .proto → C#/Python コード生成
│   └── deploy_mod.sh              # ビルド → BepInEx/plugins/ にコピー
│
├── docs/
├── .github/workflows/
└── README.md
```

### D. ビルド・デプロイサイクル

| スクリプト              | 役割                                                                    |
| ----------------------- | ----------------------------------------------------------------------- |
| `scripts/setup.sh`      | 任意の Linux で .NET SDK / protoc / uv / Python deps を一発インストール |
| `scripts/gen_proto.sh`  | `.proto` から C# / Python の両側コードを生成                            |
| `scripts/deploy_mod.sh` | `dotnet build` → `.dll` を Resonite の plugins ディレクトリへ           |

Python 側は `uv sync` で editable install 含めて完結。

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
  - Python 3.10+ 必須
  - `pip install "betterproto2[grpclib,compiler]"`
  - 生成コードは Python dataclass + type hints ネイティブで pyright strict をそのまま通る想定
- ✅ C# / Python のモジュール構造はモダリティ単位でミラーリング
- ✅ 各モダリティは独立非同期ストリーム (RL `step()` なし)
- ✅ ワールド非依存・単一ユーザー操作スコープ
- ✅ 通信データ型は pyright strict 準拠
- ✅ `setup.sh` は Linux ディストロ非依存 (公式バイナリインストーラ優先)
- ✅ 補助ツール: ライセンス MIT、formatter (csharpier / ruff)、type-check (pyright strict)、test (xunit / pytest)
- ✅ **C# Linter/Analyzer**: csharpier + Roslyn analyzers + `Nullable=enable` + `TreatWarningsAsErrors=true` (StyleCop は不採用)
- ✅ **proto スキーマは Step ごとに incremental に詰める** (Step 1 で `session.proto`、Step 3 で `camera.proto`、…)
- ✅ **BepInEx PluginGuid**: `net.mlshukai.resonite-io`

______________________________________________________________________

## 6. 今後のステップ

### Step 1: スケルトン構築

- BepisLoader mod として最小構成で起動確認
- `Engine.Current.WorldManager.FocusedWorld` から `LocalUser` を引いて Console にログ出力

### Step 2: gRPC Session

- C# 側で gRPC サーバ起動 (UDS bind)、別スレッドで動作
- Python 側から `Session.Ping` RPC が通ることを確認
- セッション管理 (接続/切断)

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
