using System.Net.Sockets;
using Grpc.Net.Client;
using ResoniteIO.Core.Logging;
using ResoniteIO.Core.Session;
using ResoniteIO.V1;
using Xunit;

namespace ResoniteIO.Core.Tests;

/// <summary>
/// <see cref="SessionHost"/> を tmp_path UDS に in-process で起動し、
/// <see cref="Grpc.Net.Client.GrpcChannel"/> から実 RPC を投げて
/// Ping echo と <c>server_unix_nanos</c> を検証する統合テスト。
/// </summary>
public sealed class SessionRoundTripTests
{
    [Fact]
    public async Task SessionHost_Ping_EchoesMessageAndStampsTimestamp()
    {
        var tmpSocketPath = Path.Combine(Path.GetTempPath(), $"rio-test-{Guid.NewGuid():N}.sock");
        var originalEnv = Environment.GetEnvironmentVariable("RESONITE_IO_SOCKET");
        Environment.SetEnvironmentVariable("RESONITE_IO_SOCKET", tmpSocketPath);
        try
        {
            using var cts = new CancellationTokenSource();
            await using var host = SessionHost.Start(new NullLogSink(), cts.Token);

            Assert.Equal(tmpSocketPath, host.SocketPath);

            // Kestrel の async bind 完了を待つ (socket file 出現を poll)。
            await WaitUntilAsync(
                () => File.Exists(tmpSocketPath),
                TimeSpan.FromSeconds(5),
                "socket file did not appear"
            );

            using var channel = GrpcChannel.ForAddress(
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
                            await sock.ConnectAsync(new UnixDomainSocketEndPoint(tmpSocketPath), ct)
                                .ConfigureAwait(false);
                            return new NetworkStream(sock, ownsSocket: true);
                        },
                    },
                }
            );

            var client = new V1.Session.SessionClient(channel);
            var beforeNanos = (DateTimeOffset.UtcNow.UtcTicks - DateTime.UnixEpoch.Ticks) * 100L;
            var resp = await client.PingAsync(new PingRequest { Message = "hello" });
            var afterNanos = (DateTimeOffset.UtcNow.UtcTicks - DateTime.UnixEpoch.Ticks) * 100L;

            Assert.Equal("hello", resp.Message);
            // タイムスタンプはクライアント計測の前後範囲に収まる。Tick 精度 (100 ns)
            // で生成しているため ms 単位への切り詰めはこの assert を通らない (clock 分解能の
            // 範囲で安全マージンを取りつつ、precision regression を検知する)。
            Assert.InRange(resp.ServerUnixNanos, beforeNanos, afterNanos);

            cts.Cancel();

            // 停止後、socket file が消えていることを poll で確認。
            await WaitUntilAsync(
                () => !File.Exists(tmpSocketPath),
                TimeSpan.FromSeconds(5),
                "socket file was not unlinked after shutdown"
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("RESONITE_IO_SOCKET", originalEnv);
            // best-effort cleanup if poll above failed.
            try
            {
                File.Delete(tmpSocketPath);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        string failureMessage
    )
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail(failureMessage);
    }

    private sealed class NullLogSink : ILogSink
    {
        public void LogDebug(string message) { }

        public void LogInfo(string message) { }

        public void LogWarning(string message) { }

        public void LogError(string message) { }
    }
}
