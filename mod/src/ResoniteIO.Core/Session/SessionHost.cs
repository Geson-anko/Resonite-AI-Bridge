using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ResoniteIO.Core.Bridge;
using ResoniteIO.Core.Camera;
using ResoniteIO.Core.Logging;

namespace ResoniteIO.Core.Session;

/// <summary>
/// Kestrel + UDS 上で <see cref="SessionService"/> を hosting する gRPC server lifecycle。
/// </summary>
/// <remarks>
/// socket path 解決順: <c>RESONITE_IO_SOCKET</c> (フルパス) →
/// <c>RESONITE_IO_SOCKET_DIR</c> 配下の <c>resonite-{pid}.sock</c> →
/// <c>$HOME/.resonite-io/resonite-{pid}.sock</c>。デフォルトを <c>$HOME</c> 配下にする
/// のは Steam pressure-vessel が <c>/home/$USER</c> を sandbox に pass-through するため:
/// mod (sandbox 内) とホスト/コンテナ Python client が同じ inode に到達できる
/// (Docker は <c>${HOME}/.resonite-io</c> を <c>/home/dev/.resonite-io</c> に bind し
/// username 差を吸収)。
/// </remarks>
public sealed class SessionHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ILogSink _log;
    private readonly EventHandler _processExitHandler;
    private readonly Task _runTask;
    private bool _disposed;

    /// <summary>
    /// bind 済み UDS のフルパス。<see cref="Start"/> 復帰時点で filesystem 上に
    /// socket が現れている保証があり、client は race 無しに connect できる。
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
    /// Kestrel の listen 完了を同期的に待ってから返す。停止は dispose または
    /// <paramref name="cancellationToken"/> 経由。<paramref name="bridge"/> /
    /// <paramref name="cameraBridge"/> は省略可能 (Core 単体テスト・モダリティが
    /// 提供されない構成用)。<see cref="InvalidOperationException"/> は socket path を
    /// 解決できなかった場合 (<c>HOME</c> 未設定環境)。
    /// </summary>
    public static SessionHost Start(
        ILogSink log,
        CancellationToken cancellationToken,
        ISessionBridge? bridge = null,
        ICameraBridge? cameraBridge = null
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
        // Camera は任意解像度の BGRA8 raw を流す (4K×4K で 64MB クラス)。proto レベルでは
        // 上限を設けず gRPC channel 設定を緩めて運用する (Plan §1 Proto schema)。
        // int.MaxValue を渡すと Grpc.AspNetCore.Server は "上限相当の最大値" として扱う。
        builder.Services.AddGrpc(o =>
        {
            o.MaxReceiveMessageSize = int.MaxValue;
            o.MaxSendMessageSize = int.MaxValue;
        });
        builder.Services.AddSingleton(log);
        if (bridge is not null)
        {
            builder.Services.AddSingleton(bridge);
        }
        if (cameraBridge is not null)
        {
            builder.Services.AddSingleton(cameraBridge);
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
        app.MapGrpcService<CameraService>();

        EventHandler processExitHandler = (_, _) => TryUnlink(socketPath);
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        log.LogInfo($"SessionHost binding UDS at {socketPath}");

        // Sync-wait on StartAsync so SocketPath is guaranteed accept-ready on return.
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

        var runTask = Task.Run(
            async () =>
            {
                try
                {
                    await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    log.LogError($"SessionHost runTask faulted: {ex}");
                }
            },
            CancellationToken.None
        );

        return new SessionHost(app, log, socketPath, processExitHandler, runTask);
    }

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
        catch (OperationCanceledException) { }
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
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch
        {
            // best-effort: 削除失敗でも次回起動時に上書きされる。
        }
    }
}
