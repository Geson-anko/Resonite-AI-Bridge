---
name: bepinex-mod-transitive-dlls
description: BepInEx mod の bin/ には CopyLocalLockFileAssemblies=true + PostBuild Copy 双方が必要。framework reference (Microsoft.AspNetCore.App) は別経路で要検証。
metadata:
  type: feedback
---

BepInEx 6 mod (`Microsoft.NET.Sdk` ベースのライブラリ csproj) で AspNetCore 系の transitive 依存を持つ Core (`Grpc.AspNetCore.Server` 等) を `ProjectReference` する場合:

- 既定では NuGet 由来の transitive DLL は **bin/ に出ない** (deps.json 経由の nuget cache lookup を前提とした挙動)。BepInEx は `AssemblyLoadContext.Default` で plugin フォルダから DLL を解決するため、隣接 DLL が無いと load 失敗。
- 対策 1: csproj に `<CopyLocalLockFileAssemblies>true</>` を追加 → Grpc 系 NuGet DLL が bin/ に出るようになる。
- 対策 2: gale (Thunderstore mod manager) profile に deploy する PostBuild `<Copy>` Target にも、明示的に Grpc 系 DLL を `<PluginFiles Include>` で追加する必要がある (TargetPath だけだと mod 本体だけが deploy される)。Gale が canonical 版を提供する `BepInExResoniteShim` / `BepisResoniteWrapper` は重複させない。
- `Microsoft.AspNetCore.App` / `Microsoft.NETCore.App` は **framework reference** (shared framework) のため、`CopyLocalLockFileAssemblies=true` でも `PostBuild Copy` でも DLL を取り出せない。Resonite (Unity ベース) は AspNetCore shared framework を持っていない可能性が高く、Kestrel ベースの実装は実機 load 時に missing assembly になる懸念あり。

**Why:** Step 2 Phase 2 で `ResoniteIO.Core` (Kestrel + UDS) を mod から起動するときに発覚。`dotnet build` 単体ではエラーにならず、bin/ を目視して初めて DLL が落ちていることに気付くため見落としやすい。

**How to apply:** 新しい transitive package 依存を Core に足したら、必ず `ls mod/src/ResoniteIO/bin/Release/` を確認し、必要なら csproj の PluginFiles ItemGroup に DLL を追加する。framework reference 起因で deploy 不能な依存は実機 (Phase 4 E2E) で load 可否を検証してから設計を確定する。
