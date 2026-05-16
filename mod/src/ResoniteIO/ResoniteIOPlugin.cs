using System;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using BepisResoniteWrapper;
using ResoniteIO.Core.Session;
using ResoniteIO.Logging;

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

    private CancellationTokenSource? _hostCts;
    private SessionHost? _sessionHost;

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
    /// FrooxEngine が完全初期化された後に呼ばれるフック。Session gRPC server を
    /// Core 側の <see cref="SessionHost"/> でバックグラウンド起動する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// BepInEx 6 <c>BasePlugin</c> には Unload 相当の hook が無いため、停止は
    /// <see cref="AppDomain.ProcessExit"/> で best-effort に行う。
    /// <c>Engine.OnShutdown</c> 系の hook 経路 (より早く graceful に停止できる
    /// 経路) は Step 3 で再評価する。
    /// </para>
    /// <para>
    /// <see cref="SessionHost.Start"/> はバックグラウンドタスクで Kestrel を回す
    /// ため engine update tick をブロックしない。
    /// </para>
    /// </remarks>
    private void OnEngineReady()
    {
        Log.LogInfo("Engine ready — starting Session gRPC host");
        try
        {
            _hostCts = new CancellationTokenSource();
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _sessionHost = SessionHost.Start(new BepInExLogSink(Log), _hostCts.Token);
            Log.LogInfo($"Session gRPC host bound at: {_sessionHost.SocketPath}");
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to start Session gRPC host: {ex}");
        }
    }

    /// <summary>
    /// プロセス終了時の best-effort cleanup。<see cref="AppDomain.ProcessExit"/>
    /// から呼ばれる。例外は飲み込む (これ以降ログ出力経路が信頼できないため)。
    /// </summary>
    private void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            _hostCts?.Cancel();
        }
        catch
        {
            // best-effort
        }

        try
        {
            _sessionHost?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // best-effort
        }
    }
}
