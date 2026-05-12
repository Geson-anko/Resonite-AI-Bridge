using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using BepisResoniteWrapper;

namespace ResoniteIO;

/// <summary>
/// BepisLoader 経由で Resonite クライアントに読み込まれる mod のエントリポイント。
/// </summary>
/// <remarks>
/// PluginMetadata 定数は csproj の Version / Authors / PackageId / Product /
/// RepositoryUrl から BepInEx.ResonitePluginInfoProps が build-time に生成する。
/// 二重管理を避けるため、本クラスにはメタデータ定数を持たない。
/// </remarks>
[ResonitePlugin(
    PluginMetadata.GUID,
    PluginMetadata.NAME,
    PluginMetadata.VERSION,
    PluginMetadata.AUTHORS,
    PluginMetadata.REPOSITORY_URL
)]
[BepInDependency(
    BepInExResoniteShim.PluginMetadata.GUID,
    BepInDependency.DependencyFlags.HardDependency
)]
public sealed class ResoniteIOPlugin : BasePlugin
{
    /// <summary>
    /// プラグインから static にアクセスできる BepInEx ログハンドラ。<see cref="Load"/>
    /// 内で <c>base.Log</c> を代入する Template の慣習に従う。
    /// </summary>
    internal static new ManualLogSource Log = null!;

    /// <summary>
    /// プラグインロード時に BepInEx ランタイムから呼び出される。
    /// </summary>
    /// <remarks>
    /// この時点では FrooxEngine の初期化が完了していない可能性があるため、
    /// Engine.Current 配下に触れる処理は <see cref="OnEngineReady"/> 側に書く。
    /// </remarks>
    public override void Load()
    {
        Log = base.Log;
        ResoniteHooks.OnEngineReady += OnEngineReady;
        Log.LogInfo($"{PluginMetadata.NAME} {PluginMetadata.VERSION} loaded");
    }

    /// <summary>
    /// FrooxEngine が完全初期化された後に呼ばれるフック。Step 2 以降で
    /// gRPC server 起動などのモダリティ配線をここに追加する。
    /// </summary>
    private void OnEngineReady()
    {
        Log.LogInfo("Engine ready — modality wiring will be added in Step 2+");
    }
}
