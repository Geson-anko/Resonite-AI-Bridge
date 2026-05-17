using System.Diagnostics;
using Grpc.Core;
using ResoniteIO.Core.Tests.Helpers;
using ResoniteIO.V1;
using Xunit;

namespace ResoniteIO.Core.Tests;

// SessionHostHarness が RESONITE_IO_SOCKET env var を書き換えるため、SessionHostEnv collection で
// SessionRoundTripTests / SessionBridgeWiringTests / SessionHostLifecycleTests と直列化する。
[Collection("SessionHostEnv")]
public sealed class CameraRoundTripTests
{
    [Fact]
    public async Task Stream_EmitsRequestedFrames_WithMonotonicIds()
    {
        var bridge = new FakeCameraBridge();
        await using var harness = await SessionHostHarness.StartAsync(cameraBridge: bridge);
        using var channel = harness.CreateChannel();
        var client = new V1.Camera.CameraClient(channel);

        const int width = 32;
        const int height = 24;
        var request = new CameraStreamRequest
        {
            Width = width,
            Height = height,
            FpsLimit = 0f,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var call = client.StreamFrames(request, cancellationToken: cts.Token);

        var received = new List<CameraFrame>();
        await foreach (var frame in call.ResponseStream.ReadAllAsync(cts.Token))
        {
            received.Add(frame);
            if (received.Count >= 3)
            {
                // Client から ストリームを閉じる。Service 側ループは ct.IsCancellationRequested で抜ける。
                cts.Cancel();
                break;
            }
        }

        Assert.Equal(3, received.Count);
        for (var i = 0; i < received.Count; i++)
        {
            var f = received[i];
            Assert.Equal(width, f.Width);
            Assert.Equal(height, f.Height);
            Assert.Equal((long)i, f.FrameId);
            Assert.Equal(CameraFrameFormat.Rgba8, f.Format);
            Assert.Equal(width * height * 4, f.Pixels.Length);
        }
    }

    [Fact]
    public async Task Stream_RespectsFpsLimit_WithinTolerance()
    {
        var bridge = new FakeCameraBridge();
        await using var harness = await SessionHostHarness.StartAsync(cameraBridge: bridge);
        using var channel = harness.CreateChannel();
        var client = new V1.Camera.CameraClient(channel);

        const float fps = 10f;
        const double windowSeconds = 0.5;
        // 期待値: 1/fps = 100ms pacing × 500ms = 5 frame だが、初回イテレーションは
        // pacing 0 で即時送信される (=+1)、client 側 stopwatch は call 確立直後から
        // 動き始める (= boundary 上で 1 frame ぶんの slip ありうる) ため、上限 8 で
        // 不当に高速にループしていないことだけを検証する。下限 1 (= 何か受信)。
        const int expectedMax = 8;

        var request = new CameraStreamRequest
        {
            Width = 16,
            Height = 16,
            FpsLimit = fps,
        };

        using var cts = new CancellationTokenSource();
        using var call = client.StreamFrames(request, cancellationToken: cts.Token);

        var count = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync(cts.Token))
            {
                count++;
                if (sw.Elapsed.TotalSeconds >= windowSeconds)
                {
                    cts.Cancel();
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }

        Assert.InRange(count, 1, expectedMax);
    }

    [Fact]
    public async Task Stream_TranslatesCameraNotReady_ToFailedPrecondition()
    {
        var bridge = new FakeCameraBridge { ThrowNotReady = true };
        await using var harness = await SessionHostHarness.StartAsync(cameraBridge: bridge);
        using var channel = harness.CreateChannel();
        var client = new V1.Camera.CameraClient(channel);

        var request = new CameraStreamRequest
        {
            Width = 16,
            Height = 16,
            FpsLimit = 0f,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var call = client.StreamFrames(request, cancellationToken: cts.Token);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync(cts.Token))
            {
                // 1 フレームも出ないはず。
            }
        });

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }
}
