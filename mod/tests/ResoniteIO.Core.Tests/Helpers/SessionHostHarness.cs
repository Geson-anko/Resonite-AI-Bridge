using System.Net.Sockets;
using Grpc.Net.Client;
using ResoniteIO.Core.Bridge;
using ResoniteIO.Core.Session;

namespace ResoniteIO.Core.Tests.Helpers;

/// <summary>
/// テスト用に <see cref="SessionHost"/> を tmp_path UDS 上で起動・停止する harness。
/// </summary>
/// <remarks>
/// <para>
/// テスト本体から socket path 採番 / <c>RESONITE_IO_SOCKET</c> env var の出入り /
/// Kestrel bind 完了待ち / Dispose 時の cleanup といった setup/teardown 関心事を
/// 切り離し、テスト本体は実際に検証したいシナリオに専念できるようにする。
/// </para>
/// <para>
/// 使用例:
/// <code>
/// await using var harness = await SessionHostHarness.StartAsync();
/// using var channel = harness.CreateChannel();
/// var client = new V1.Session.SessionClient(channel);
/// var response = await client.PingAsync(new PingRequest { Message = "hello" });
/// </code>
/// </para>
/// <para>
/// <c>RESONITE_IO_SOCKET</c> env var を読み書きするため、これを使うテストクラスは
/// xunit collection <c>"SessionHostEnv"</c> で直列化する必要がある。
/// </para>
/// </remarks>
internal sealed class SessionHostHarness : IAsyncDisposable
{
    public string SocketPath { get; }
    public SessionHost Host { get; }

    private readonly CancellationTokenSource _cts;
    private readonly string? _previousEnv;
    private bool _disposed;

    private SessionHostHarness(
        string socketPath,
        SessionHost host,
        CancellationTokenSource cts,
        string? previousEnv
    )
    {
        SocketPath = socketPath;
        Host = host;
        _cts = cts;
        _previousEnv = previousEnv;
    }

    /// <summary>
    /// tmp_path UDS に <see cref="SessionHost"/> を起動し、socket file の出現まで待つ。
    /// </summary>
    /// <param name="bridge">
    /// <see cref="ISessionBridge"/> を DI に流し込みたい場合に渡す。<c>null</c> なら
    /// 通常の <see cref="SessionHost.Start(ILogSink, CancellationToken, ISessionBridge?)"/>
    /// が default で DI 登録をスキップする挙動と同等。
    /// </param>
    public static async Task<SessionHostHarness> StartAsync(ISessionBridge? bridge = null)
    {
        var socketPath = Path.Combine(Path.GetTempPath(), $"rio-test-{Guid.NewGuid():N}.sock");
        var previousEnv = Environment.GetEnvironmentVariable("RESONITE_IO_SOCKET");
        Environment.SetEnvironmentVariable("RESONITE_IO_SOCKET", socketPath);

        var cts = new CancellationTokenSource();
        SessionHost host;
        try
        {
            host = SessionHost.Start(new NullLogSink(), cts.Token, bridge);
        }
        catch
        {
            Environment.SetEnvironmentVariable("RESONITE_IO_SOCKET", previousEnv);
            cts.Dispose();
            throw;
        }

        await TestPolling.WaitUntilAsync(
            () => File.Exists(socketPath),
            TimeSpan.FromSeconds(5),
            $"socket file did not appear at {socketPath}"
        );

        return new SessionHostHarness(socketPath, host, cts, previousEnv);
    }

    /// <summary>
    /// 起動済み UDS に接続する <see cref="GrpcChannel"/> を生成する。
    /// 呼び出し側で <c>using</c> して channel の lifecycle を管理する。
    /// </summary>
    public GrpcChannel CreateChannel()
    {
        return GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    ConnectCallback = async (_, ct) =>
                    {
                        var sock = new Socket(
                            AddressFamily.Unix,
                            SocketType.Stream,
                            ProtocolType.Unspecified
                        );
                        await sock.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), ct)
                            .ConfigureAwait(false);
                        return new NetworkStream(sock, ownsSocket: true);
                    },
                },
            }
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _cts.Cancel();
        try
        {
            await Host.DisposeAsync();
        }
        catch
        {
            // best-effort: テスト後の cleanup なので例外は飲み込む
        }
        _cts.Dispose();

        Environment.SetEnvironmentVariable("RESONITE_IO_SOCKET", _previousEnv);
        try
        {
            File.Delete(SocketPath);
        }
        catch
        {
            // best-effort
        }
    }
}
