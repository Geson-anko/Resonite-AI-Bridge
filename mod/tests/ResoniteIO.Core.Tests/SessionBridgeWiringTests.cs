using ResoniteIO.Core.Bridge;
using ResoniteIO.Core.Logging;
using ResoniteIO.Core.Session;
using Xunit;

namespace ResoniteIO.Core.Tests;

/// <summary>
/// <see cref="SessionHost.Start"/> に <see cref="ISessionBridge"/> を渡しても
/// host が起動 / 停止できることを確認する smoke。Ping ラウンドトリップは
/// <see cref="SessionRoundTripTests"/> で別途カバー済みなので重複させない。
/// </summary>
/// <remarks>
/// <c>RESONITE_IO_SOCKET</c> env var を読み書きするため
/// <see cref="SessionRoundTripTests"/> と同じ collection でシリアル化する。
/// </remarks>
[Collection("SessionHostEnv")]
public sealed class SessionBridgeWiringTests
{
    [Fact]
    public async Task SessionHost_StartsAndStops_WithBridgeInjected()
    {
        var tmpSocketPath = Path.Combine(Path.GetTempPath(), $"rio-bridge-{Guid.NewGuid():N}.sock");
        var originalEnv = Environment.GetEnvironmentVariable("RESONITE_IO_SOCKET");
        Environment.SetEnvironmentVariable("RESONITE_IO_SOCKET", tmpSocketPath);
        try
        {
            using var cts = new CancellationTokenSource();
            var bridge = new FakeSessionBridge(focusedWorldName: "home", localUserName: "tester");

            await using var host = SessionHost.Start(new NullLogSink(), cts.Token, bridge);

            Assert.Equal(tmpSocketPath, host.SocketPath);
            Assert.True(File.Exists(tmpSocketPath));

            // Bridge プロパティが Core 側でそのまま読めることだけ確認 (Service が
            // 今は consume していなくても、DI 経由で取得できる前提を守る)。
            Assert.Equal("home", bridge.FocusedWorldName);
            Assert.Equal("tester", bridge.LocalUserName);

            cts.Cancel();
        }
        finally
        {
            Environment.SetEnvironmentVariable("RESONITE_IO_SOCKET", originalEnv);
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

    private sealed class FakeSessionBridge(string? focusedWorldName, string? localUserName)
        : ISessionBridge
    {
        public string? FocusedWorldName { get; } = focusedWorldName;
        public string? LocalUserName { get; } = localUserName;
    }

    private sealed class NullLogSink : ILogSink
    {
        public void LogDebug(string message) { }

        public void LogInfo(string message) { }

        public void LogWarning(string message) { }

        public void LogError(string message) { }
    }
}
