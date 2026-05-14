# Gale 経由起動での mod load 検証

`ResoniteIO.dll` が Gale プロファイル経由で起動した Resonite に正しくロードされるかを確認する手動 smoke test。

実装計画上の対応:

- [.claude/resonite_io_plan.md](../../../.claude/resonite_io_plan.md) §Step 1 の終了条件「BepisLoader mod として最小構成で起動確認」のうち、実機での確認に相当する
- 自動化できない (FrooxEngine 初期化を伴うため Resonite を実プロセスで起動する必要がある)

## 前提

- host に Gale v1.5.4+ がインストール済み ([github.com/Kesomannen/gale](https://github.com/Kesomannen/gale))
- `<repo>/gale/` が Gale のカスタムプロファイルとして登録されている
- `just check-gale` が exit 0 を返す状態 (BepisLoader / BepInEx.Core / BepInExResoniteShim が in)
- `just deploy-mod` が成功し、`gale/BepInEx/plugins/ResoniteIO/ResoniteIO.{dll,pdb}` が配置済み
- Resonite が Steam からインストール済み

## 手順

1. **Gale 部品の在中確認**

   ```sh
   just check-gale
   ```

   全項目 ✓ を確認。`✗` があれば Gale で profile を更新してから次へ。

2. **mod を deploy**

   ```sh
   just container-shell    # container 内に attach
   # container 内で:
   just deploy-mod         # gale/BepInEx/plugins/ResoniteIO/ に DLL+PDB 配置
   exit                    # container を抜ける
   ```

3. **host 側でログ追従用ターミナルを開く**

   ```sh
   just log                # tail -F で BepInEx LogOutput を追う
   ```

   ファイルがまだ無ければ「未存在」と告知されるが、Resonite 起動後に自動追従する。

4. **Gale から Resonite を起動**

   - Gale を開き、`<repo>/gale` プロファイルを選択
   - `Launch Profile` (または `Play`) で Resonite を起動
   - Gale 経由起動でないと `LinuxBootstrap.sh` 差し替えが効かず BepInEx がロードされない点に注意

5. **ログを観測**

   `just log` を走らせているターミナルで以下のような行が出ることを確認:

   ```text
   [Info   :   BepInEx] Loading [ResoniteIO 0.1.0]
   [Info   :ResoniteIO] ResoniteIO 0.1.0 loaded
   ...
   [Info   :ResoniteIO] Engine ready — modality wiring will be added in Step 2+
   ```

   `ResoniteIO 0.1.0 loaded` が `BasePlugin.Load()` の出力、`Engine ready ...` が `OnEngineReady` フックの出力。両方確認できれば mod が正しく BepisLoader + Shim 経由で読み込まれ、FrooxEngine の初期化フックも生きていることが分かる。

## 期待結果

- 上記 2 行 (`loaded` と `Engine ready`) が `gale/BepInEx/LogOutput.log` に出る
- Resonite 起動後にエラーダイアログ等が出ない
- Gale で profile に追加した他の mod (Shim 等) と共存できる

## トラブルシュート

### Gale バージョン要件

Gale v1.5.4 未満ではカスタム profile path を指定できない。Gale を更新してから再度プロファイルを作る。

### Linux modloading の挙動差

[Kesomannen/gale#381](https://github.com/Kesomannen/gale/issues/381) を参照。Linux で Gale 経由起動時に BepInEx が一部ロードされないケースが報告されている。確認ポイント:

- Gale プロファイル直下の `hookfxr.ini` に `enable=true` が書かれているか
- `LinuxBootstrap.sh` が profile 版に置換されているか (Gale 起動時に Resonite 側 `run.sh` などが上書きされる)

### `BepInExResoniteShim` 不在

ログに `Could not load type 'BepInExResoniteShim.ResoniteHooks'` 系のエラーが出る場合、Gale で `ResoniteModding-BepInExResoniteShim` (>=0.9.3) を install していない。Gale で追加して `just check-gale` を再走。

### `BepisLoader.dll` が profile root に無い

profile を Gale で再作成、または `ResoniteModding-BepisLoader` を install。`gale/BepisLoader.dll` (profile root 直下) に存在することを `just check-gale` で確認。

### Vanilla 起動と混同していないか

Steam から直接 (Gale を介さず) Resonite を起動すると、BepInEx が読み込まれず mod はロードされない。これは設計通り (ホスト Resonite は Vanilla 維持)。確認には Gale から `Launch Profile` を使う。
