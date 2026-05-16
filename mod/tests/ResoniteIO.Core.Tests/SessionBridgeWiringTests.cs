using ResoniteIO.Core.Bridge;
using ResoniteIO.Core.Tests.Helpers;
using Xunit;

namespace ResoniteIO.Core.Tests;

/// <summary>
/// <see cref="ResoniteIO.Core.Session.SessionHost.Start(ResoniteIO.Core.Logging.ILogSink, CancellationToken, ISessionBridge?)"/>
/// に <see cref="ISessionBridge"/> を渡しても host が問題なく起動できる smoke。
/// </summary>
/// <remarks>
/// Ping ラウンドトリップは <see cref="SessionRoundTripTests"/> で別途カバーするので
/// ここでは重複させない。<c>RESONITE_IO_SOCKET</c> env を扱うため
/// <see cref="SessionRoundTripTests"/> と同じ collection でシリアル化する。
/// </remarks>
[Collection("SessionHostEnv")]
public sealed class SessionBridgeWiringTests
{
    [Fact]
    public async Task Start_WithBridge_DoesNotConsumeBridgeValues()
    {
        var bridge = new FakeSessionBridge(focusedWorldName: "home", localUserName: "tester");

        await using var harness = await SessionHostHarness.StartAsync(bridge);

        // Service 側がまだ Bridge を consume していないことの裏返し:
        // 渡した値は変更されずそのまま読み出せる。将来 Service が消費し始めたら
        // この assertion を緩めて consumer 側に移す。
        Assert.Equal("home", bridge.FocusedWorldName);
        Assert.Equal("tester", bridge.LocalUserName);
    }

    private sealed class FakeSessionBridge(string? focusedWorldName, string? localUserName)
        : ISessionBridge
    {
        public string? FocusedWorldName { get; } = focusedWorldName;
        public string? LocalUserName { get; } = localUserName;
    }
}
