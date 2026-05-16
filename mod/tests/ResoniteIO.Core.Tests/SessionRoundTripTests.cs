using ResoniteIO.Core.Tests.Helpers;
using ResoniteIO.V1;
using Xunit;

namespace ResoniteIO.Core.Tests;

/// <summary>
/// <see cref="ResoniteIO.Core.Session.SessionService"/> の <c>Ping</c> RPC 振る舞いを
/// in-process Kestrel ラウンドトリップで検証する。
/// </summary>
/// <remarks>
/// host 起動 / channel 構築 / 後片付けは <see cref="SessionHostHarness"/> に閉じ込めて
/// いるため、本テストは「Ping を送り、echo + timestamp を確認する」シナリオ自体に集中する。
/// <c>RESONITE_IO_SOCKET</c> env var を内部で扱う他テストとの競合を避けるため
/// xunit collection <c>"SessionHostEnv"</c> でシリアル化する。
/// </remarks>
[Collection("SessionHostEnv")]
public sealed class SessionRoundTripTests
{
    [Fact]
    public async Task Ping_EchoesMessage_AndStampsServerTimestamp()
    {
        await using var harness = await SessionHostHarness.StartAsync();
        using var channel = harness.CreateChannel();
        var client = new V1.Session.SessionClient(channel);

        var beforeNanos = UnixNanosClock.Now();
        var response = await client.PingAsync(new PingRequest { Message = "hello" });
        var afterNanos = UnixNanosClock.Now();

        Assert.Equal("hello", response.Message);
        // タイムスタンプはクライアント計測の前後範囲に収まる (Tick 精度 = 100 ns)。
        Assert.InRange(response.ServerUnixNanos, beforeNanos, afterNanos);
    }
}
