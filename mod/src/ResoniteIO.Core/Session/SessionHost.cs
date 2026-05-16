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
///   <item><see cref="Start"/> の <c>defaultSocketDir</c> 引数で渡されたディレクトリ
///     配下に <c>resonite-{pid}.sock</c>。Mod 層が plugin 自身の
///     deploy ディレクトリ (gale 経由で <c>/workspace</c> bind mount に乗る)
///     を渡すことで Steam pressure-vessel sandbox / Docker container 間で
///     filesystem 共有が成立する</item>
///   <item><c>$XDG_RUNTIME_DIR/resonite-io/</c> 配下に
///     <c>resonite-{pid}.sock</c> (上記が無いときの最終フォールバック。
///     pressure-vessel 配下では <c>/run/user/&lt;UID&gt;/</c> が sandbox 内
///     tmpfs に overlay されるため共有不可)</item>
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
    /// <param name="defaultSocketDir">
    /// 環境変数 <c>RESONITE_IO_SOCKET</c> / <c>RESONITE_IO_SOCKET_DIR</c> いずれも
    /// 未設定のときに使う socket 配置ディレクトリ。Mod 層 (BepInEx plugin) は
    /// plugin 自身の deploy ディレクトリを渡すことで pressure-vessel sandbox と
    /// host (container) 間の bind-mount 共有を成立させる。<c>null</c> なら最終
    /// フォールバックとして <c>$XDG_RUNTIME_DIR/resonite-io/</c> が使われる。
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// 環境変数経由でも <paramref name="defaultSocketDir"/> でも
    /// <c>XDG_RUNTIME_DIR</c> でも socket path を解決できなかった場合。
    /// </exception>
    public static SessionHost Start(
        ILogSink log,
        CancellationToken cancellationToken,
        string? defaultSocketDir = null
    )
    {
        ArgumentNullException.ThrowIfNull(log);

        var socketPath = ResolveSocketPath(defaultSocketDir);

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

    private static string ResolveSocketPath(string? defaultSocketDir)
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

        if (!string.IsNullOrEmpty(defaultSocketDir))
        {
            return Path.Combine(defaultSocketDir, socketName);
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdg))
        {
            return Path.Combine(xdg, "resonite-io", socketName);
        }

        throw new InvalidOperationException(
            "Cannot resolve UDS path: set RESONITE_IO_SOCKET, RESONITE_IO_SOCKET_DIR, "
                + "pass defaultSocketDir, or define XDG_RUNTIME_DIR."
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
