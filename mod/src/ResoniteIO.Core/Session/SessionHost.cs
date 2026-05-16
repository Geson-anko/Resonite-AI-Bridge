using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
///   <item><c>$XDG_RUNTIME_DIR/resonite-io/</c> 配下に
///     <c>resonite-{pid}.sock</c></item>
/// </list>
/// <para>
/// 上記いずれも解決できない場合は <see cref="InvalidOperationException"/>。
/// bind 直前に stale socket を best-effort で削除し、<see cref="AppDomain.ProcessExit"/>
/// でも best-effort unlink を仕掛ける (SIGKILL 等で取りこぼした場合は次回起動時に上書き)。
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
    /// 本ホストが bind した UDS のフルパス。
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
    /// gRPC server を構築して起動し、<see cref="SessionHost"/> を返す。
    /// 内部的に <see cref="WebApplication.RunAsync(CancellationToken)"/> を
    /// バックグラウンドタスクで回す。
    /// </summary>
    /// <param name="log">Core が利用するログシンク。Service にも DI 経由で渡される。</param>
    /// <param name="cancellationToken">サーバの停止トリガ。</param>
    /// <exception cref="InvalidOperationException">
    /// 環境変数経由でも <c>XDG_RUNTIME_DIR</c> でも socket path を解決できなかった場合。
    /// </exception>
    public static SessionHost Start(ILogSink log, CancellationToken cancellationToken)
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

        var runTask = Task.Run(() => app.RunAsync(cancellationToken), CancellationToken.None);

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

        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdg))
        {
            return Path.Combine(xdg, "resonite-io", socketName);
        }

        throw new InvalidOperationException(
            "Cannot resolve UDS path: set RESONITE_IO_SOCKET, RESONITE_IO_SOCKET_DIR, "
                + "or XDG_RUNTIME_DIR."
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
