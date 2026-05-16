using System;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using BepisResoniteWrapper;
using FrooxEngine;
using ResoniteIO.Bridge;
using ResoniteIO.Core.Session;
using ResoniteIO.Loading;
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

    private PluginAssemblyResolver? _assemblyResolver;
    private BepInExLogSink? _logSink;
    private CancellationTokenSource? _hostCts;
    private SessionHost? _sessionHost;
    private FrooxEngineSessionBridge? _sessionBridge;

    /// <summary>
    /// プラグインロード時に BepInEx ランタイムから呼び出される。
    /// </summary>
    /// <remarks>
    /// <para>
    /// この時点では FrooxEngine の初期化が完了していない可能性があるため、
    /// Engine.Current 配下に触れる処理は <see cref="OnEngineReady"/> 側に書く。
    /// </para>
    /// <para>
    /// 重要: <see cref="PluginAssemblyResolver"/> attach **以前** に
    /// <c>ResoniteIO.Core</c> 配下の型 (例: <see cref="BepInExLogSink"/>) を参照しない。
    /// 参照すると <c>ResoniteIO.Core.dll</c> が早期ロードされ、resolver event が
    /// 発火する前に Resonite 同梱の旧 <c>Google.Protobuf</c> が解決され、
    /// その後 <see cref="SessionHost"/> 起動時に
    /// <c>TypeLoadException: Could not load type 'Google.Protobuf.IBufferMessage'</c>
    /// となる。<see cref="BepInExLogSink"/> の生成は <see cref="OnEngineReady"/> に遅延する。
    /// </para>
    /// </remarks>
    public override void Load()
    {
        Log = base.Log;

        // BepInEx は plugin folder を Default ALC の probe path に登録しないため、
        // 同梱した ASP.NET Core / gRPC 隣接 DLL を fallback 解決するリゾルバを attach。
        // ManualLogSource を直接渡し、Core 側型 (BepInExLogSink/ILogSink) を経由しない。
        var pluginDirectory =
            Path.GetDirectoryName(typeof(ResoniteIOPlugin).Assembly.Location) ?? string.Empty;
        if (!string.IsNullOrEmpty(pluginDirectory))
        {
            _assemblyResolver = new PluginAssemblyResolver(pluginDirectory, Log);
        }

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
            // Core 側型を参照する最初のポイント。PluginAssemblyResolver は Load() で
            // attach 済みのため、ResoniteIO.Core.dll および隣接 Google.Protobuf.dll は
            // plugin folder 同梱版が解決される。
            _logSink = new BepInExLogSink(Log);
            // FrooxEngine 状態 (FocusedWorld / LocalUser) を Core 側へ露出する。
            // WorldFocused event をここで購読し、focus 切替時にログ + snapshot を更新。
            _sessionBridge = new FrooxEngineSessionBridge(Engine.Current, _logSink);
            // SessionHost の default (`$HOME/.resonite-io/`) をそのまま使う。
            // Steam pressure-vessel は host の `/home/$USER` を sandbox に
            // pass-through するため、ホスト Python / container Python と
            // 同じ inode に到達できる (container 側は username が異なるため
            // `${HOME}/.resonite-io` → `/home/dev/.resonite-io` の bind を
            // docker-compose.yml で設定済み)。
            _sessionHost = SessionHost.Start(_logSink, _hostCts.Token, _sessionBridge);
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
            _sessionBridge?.Dispose();
        }
        catch
        {
            // best-effort
        }

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

        try
        {
            _assemblyResolver?.Dispose();
        }
        catch
        {
            // best-effort
        }
    }
}
