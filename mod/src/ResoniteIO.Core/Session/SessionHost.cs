using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ResoniteIO.Core.Bridge;
using ResoniteIO.Core.Logging;

namespace ResoniteIO.Core.Session;

/// <summary>
/// Kestrel + Unix Domain Socket 上で <see cref="SessionService"/> を hosting する
/// gRPC server lifecycle。Resonite 非依存のピュア Core 層で完結する。
/// </summary>
/// <remarks>
/// <para>
/// socket path 解決順序:
/// </para>
/// <list type="number">
///   <item>環境変数 <c>RESONITE_IO_SOCKET</c> (フルパス指定; 主にテスト用途)</item>
///   <item>環境変数 <c>RESONITE_IO_SOCKET_DIR</c> 配下に
///     <c>resonite-{pid}.sock</c></item>
///   <item>デフォルト: <c>$HOME/.resonite-io/resonite-{pid}.sock</c>。
///     Steam pressure-vessel は <c>/home/$USER</c> を sandbox に
///     pass-through するため、Resonite 上の mod とホスト/コンテナの
///     Python client が同じ inode に到達できる (Docker 利用時は
///     <c>${HOME}/.resonite-io</c> を <c>/home/dev/.resonite-io</c> に bind すれば
///     username が異なっても 3 つの mount namespace で一致する)</item>
/// </list>
/// <para>
/// 上記いずれも解決できない場合 (<c>HOME</c> 未設定) は
/// <see cref="InvalidOperationException"/>。bind 直前に stale socket を
/// best-effort で削除し、<see cref="AppDomain.ProcessExit"/> でも
/// best-effort unlink を仕掛ける (SIGKILL 等で取りこぼした場合は
/// 次回起動時に上書き)。
/// </para>
/// </remarks>
public sealed class SessionHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ILogSink _log;
    private readonly EventHandler _processExitHandler;
    private readonly Task _runTask;
    private bool _disposed;

    /// <summary>
    /// 本ホストが bind 済みの UDS フルパス。<see cref="Start"/> 復帰時点で
    /// filesystem に socket が現れていることが保証される (client が即接続して良い)。
    /// </summary>
    public string SocketPath { get; }

    private SessionHost(
        WebApplication app,
        ILogSink log,
        string socketPath,
        EventHandler processExitHandler,
        Task runTask
    )
    {
        _app = app;
        _log = log;
        SocketPath = socketPath;
        _processExitHandler = processExitHandler;
        _runTask = runTask;
    }

    /// <summary>
    /// gRPC server を構築・起動し、Kestrel の listen 完了を同期的に待ってから
    /// <see cref="SessionHost"/> を返す。復帰時点で <see cref="SocketPath"/> は
    /// 受け入れ可能 (client は race 無しに connect できる)。shutdown 待ちは
    /// バックグラウンドタスクで回り、停止は本オブジェクトの dispose か
    /// <paramref name="cancellationToken"/> 経由で行う。
    /// </summary>
    /// <param name="log">Core が利用するログシンク。Service にも DI 経由で渡される。</param>
    /// <param name="bridge">
    /// Mod 側から注入される engine 状態の露出 IF。<c>null</c> でも host は起動できる
    /// (Core 単体テストや engine 非依存の用途を想定)。non-null なら DI コンテナに
    /// シングルトン登録され、将来 Service / interceptor が consume する余地を持つ。
    /// </param>
    /// <param name="cancellationToken">サーバの停止トリガ。</param>
    /// <exception cref="InvalidOperationException">
    /// 環境変数経由でも <c>$HOME/.resonite-io/</c> でも socket path を
    /// 解決できなかった場合 (<c>HOME</c> 未設定環境)。
    /// </exception>
    public static SessionHost Start(
        ILogSink log,
        CancellationToken cancellationToken,
        ISessionBridge? bridge = null
    )
    {
        ArgumentNullException.ThrowIfNull(log);

        var socketPath = ResolveSocketPath();

        var socketDir = Path.GetDirectoryName(socketPath);
        if (!string.IsNullOrEmpty(socketDir))
        {
            Directory.CreateDirectory(socketDir);
        }

        TryUnlink(socketPath);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(log);
        if (bridge is not null)
        {
            builder.Services.AddSingleton(bridge);
        }
        builder.WebHost.ConfigureKestrel(opts =>
        {
            opts.ListenUnixSocket(
                socketPath,
                listenOpts => listenOpts.Protocols = HttpProtocols.Http2
            );
        });

        var app = builder.Build();
        app.MapGrpcService<SessionService>();

        EventHandler processExitHandler = (_, _) => TryUnlink(socketPath);
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        log.LogInfo($"SessionHost binding UDS at {socketPath}");

        // StartAsync で Kestrel が listen を完了するまで同期的に待つ。
        // この時点で UDS socket が filesystem に現れている保証が得られる。
        try
        {
            app.StartAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            log.LogError($"SessionHost failed to start Kestrel: {ex}");
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
            TryUnlink(socketPath);
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        log.LogInfo($"SessionHost listening on {socketPath}");

        // shutdown 待ちは background task で。停止時例外は警告ログのみ。
        var runTask = Task.Run(
            async () =>
            {
                try
                {
                    await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 期待される。
                }
                catch (Exception ex)
                {
                    log.LogError($"SessionHost runTask faulted: {ex}");
                }
            },
            CancellationToken.None
        );

        return new SessionHost(app, log, socketPath, processExitHandler, runTask);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;

        try
        {
            await _app.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning($"SessionHost.StopAsync threw: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 期待される (cancellationToken による停止)。
        }
        catch (Exception ex)
        {
            _log.LogWarning($"SessionHost run task threw: {ex.GetType().Name}: {ex.Message}");
        }

        await _app.DisposeAsync().ConfigureAwait(false);
        TryUnlink(SocketPath);
    }

    private static string ResolveSocketPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("RESONITE_IO_SOCKET");
        if (!string.IsNullOrEmpty(explicitPath))
        {
            return explicitPath;
        }

        var pid = Process.GetCurrentProcess().Id;
        var socketName = $"resonite-{pid}.sock";

        var socketDir = Environment.GetEnvironmentVariable("RESONITE_IO_SOCKET_DIR");
        if (!string.IsNullOrEmpty(socketDir))
        {
            return Path.Combine(socketDir, socketName);
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            return Path.Combine(home, ".resonite-io", socketName);
        }

        throw new InvalidOperationException(
            "Cannot resolve UDS path: set RESONITE_IO_SOCKET, RESONITE_IO_SOCKET_DIR, "
                + "or ensure HOME is set."
        );
    }

    private static void TryUnlink(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
            // 通常ケース。
        }
        catch (DirectoryNotFoundException)
        {
            // 通常ケース。
        }
        catch
        {
            // best-effort: 削除できなくても致命的ではない。
        }
    }
}
