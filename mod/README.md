# mod/ — ResoniteIO (C# / BepisLoader)

Resonite クライアントに読み込まれる C# Mod。BepInEx 6 系列の `BepisLoader` を
ターゲットにし、`Remora.Resonite.Sdk` 2.1.0 が FrooxEngine 参照アセンブリと
BepisLoader 向けの依存を一括で引き込む。

## 構成

- `src/ResoniteIO/` — 本体プロジェクト (`ResoniteIO.dll`)
  - モダリティ別フォルダ (`Session/`, `Camera/`, `Audio/`, `Locomotion/`,
    `Manipulation/`) は Python 側 `src/resoio/` とミラーリングする
- `tests/ResoniteIO.Tests/` — xunit ユニットテスト
- `proto/` のスキーマは build 時に `Grpc.Tools` が `obj/` に C# を生成する
  (commit 不要)

## ローカル開発コマンド

このディレクトリで実行する:

```bash
dotnet restore                  # NuGet 復元 (NuGet.config の 3 feed を使う)
dotnet build -c Release         # mod ビルド (proto 生成も走る)
dotnet test                     # xunit smoke test
dotnet csharpier check .        # フォーマット検査 (CI モード)
dotnet csharpier format .       # フォーマットを書き戻す
```

リポジトリ全体のタスクは root の `just` レシピを使うこと
(`just mod-build`, `just mod-test`, `just mod-format`)。

## Resonite へのデプロイ

`scripts/deploy_mod.sh` (root の `just deploy-mod`) を使う。
`.env` の `RESONITE_PLUGIN_DIR` で配置先を指定する。

## 参照アセンブリと NuGet feed

- `nuget.org` — Grpc.Tools / Google.Protobuf / xunit など一般 OSS
- `https://nuget.bepinex.dev/v3/index.json` — `BepInEx.Core` 系のプレリリース
- `https://nuget-modding.resonite.net/v3/index.json` — `BepInExResoniteShim` /
  `BepisResoniteWrapper` / `BepInEx.ResonitePluginInfoProps`

これらの設定は `mod/NuGet.config` に固定されている (CI でも同じ feed が解決される)。
