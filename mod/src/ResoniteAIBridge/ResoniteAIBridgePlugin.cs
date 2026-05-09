using BepInEx;
using BepInEx.NET.Common;

namespace ResoniteAIBridge;

/// <summary>
/// BepisLoader 経由で Resonite クライアントに読み込まれる mod のエントリポイント。
/// </summary>
/// <remarks>
/// Step 1 (skeleton) では FrooxEngine API を一切呼ばず、ロードログを出すだけの
/// Hello World 実装。Step 2 以降で gRPC server の起動 / モダリティ別ストリームの
/// 配線を <see cref="Load"/> 内に追加していく。本クラスを <see cref="BasePlugin"/>
/// 派生にしているのは BepInEx 6 の慣習に従うため。
/// </remarks>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ResoniteAIBridgePlugin : BasePlugin
{
    /// <summary>
    /// BepInEx に通知する一意なプラグイン識別子。逆 DNS 形式で衝突を避ける。
    /// </summary>
    public const string PluginGuid = "net.gop.resonite-ai-bridge";

    /// <summary>BepInEx ログ等で表示される人間可読のプラグイン名。</summary>
    public const string PluginName = "ResoniteAIBridge";

    /// <summary>SemVer 形式のプラグインバージョン。</summary>
    public const string PluginVersion = "0.1.0";

    /// <summary>
    /// プラグインロード時に BepInEx ランタイムから呼び出される。
    /// </summary>
    public override void Load()
    {
        Log.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}
