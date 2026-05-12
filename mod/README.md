# mod/ — ResoniteIO (C# / BepisLoader)

Resonite クライアントに読み込まれる C# Mod。BepisLoader 公式テンプレート
(`dotnet new bep6resonite`) と同じ構成で、`Microsoft.NET.Sdk` + 明示
PackageReference でビルドする。FrooxEngine は `$(ResonitePath)` 配下の
DLL を build-time に直接参照する。

## 構成

- `src/ResoniteIO/` — 本体プロジェクト (`ResoniteIO.dll`)
  - モダリティ別フォルダ (`Session/`, `Camera/`, `Audio/`, `Locomotion/`,
    `Manipulation/`) は Python 側 `src/resoio/` とミラーリングする
  - `Properties/launchSettings.json` — VS / Rider / VSCode から F5 で
    `$(GamePath)Renderite.Host.exe` を起動するプロファイル
- `tests/ResoniteIO.Tests/` — xunit ユニットテスト (Resonite 起動なしで
  実行可能な範囲: アセンブリ名検査・属性メタデータ検査)
- `Directory.Build.targets` — Thunderstore 配布用 `PackTS` Target
- `thunderstore.toml` / `icon.png` / `CHANGELOG.md` — tcli 用 packaging 資材
- proto は build 時に `Grpc.Tools` が `obj/` 配下に C# を生成 (commit 不要)

## ローカル開発コマンド

リポジトリ全体は root の `just` レシピを使う:

```bash
just mod-build      # dotnet build -c Release; PostBuild が $(ResonitePath)/
                    #   BepInEx/plugins/ResoniteIO/ に DLL+PDB を自動配置
just mod-test       # xunit smoke test
just mod-format     # csharpier format
just mod-pack       # Thunderstore zip を mod/build/ に生成
just mod-clean      # bin/ obj/ build/ と Resonite 上の plugin を撤去
just deploy-mod     # mod-build のラッパ (名前で意図を表す)
```

直接叩く場合はこの `mod/` ディレクトリで:

```bash
dotnet restore
dotnet build -c Release
dotnet test
dotnet csharpier format .
dotnet build -c Release -t:PackTS -v d   # Thunderstore zip
```

## Resonite へのデプロイ

`mod/src/ResoniteIO/ResoniteIO.csproj` の `PostBuild` Target が
`$(ResonitePath)/BepInEx/plugins/ResoniteIO/` に `ResoniteIO.dll` と
`ResoniteIO.pdb` を Copy する。配置先は次の優先順位で決まる:

1. `.env` (もしくは shell env) の `ResonitePath`
2. Steam Windows: `%PROGRAMFILES(x86)%\Steam\steamapps\common\Resonite\`
3. Steam Linux: `$HOME/.steam/steam/steamapps/common/Resonite/`

いずれも見付からなければ NuGet の `Resonite.GameLibs` (build-time 参照のみ)
にフォールバックし、Copy は skip される (CI 安全)。

`.env.example` をコピーして `.env` を作成し、`ResonitePath` を絶対パスで
書くこと (dotenv は `~` / `$HOME` を展開しない)。

## F5 デバッグ

`Properties/launchSettings.json` の `Launch` profile を選択して F5 を押すと、
`$(GamePath)Renderite.Host.exe` が起動する。BepisLoader debug attach の
ワークフローに直結する。

## NuGet feed

- `nuget.org` — Grpc.Tools / Google.Protobuf / xunit など一般 OSS
- `https://nuget.bepinex.dev/v3/index.json` — `BepInEx.Core` 系プレリリース
- `https://nuget-modding.resonite.net/v3/index.json` —
  `BepInEx.ResonitePluginInfoProps` / `ResoniteModding.BepInExResoniteShim` /
  `ResoniteModding.BepisResoniteWrapper` / `Resonite.GameLibs`

設定は `mod/NuGet.config` に固定 (CI でも同じ feed が解決される)。
