using ResoniteIO.Core.Bridge;

namespace ResoniteIO.Core.Tests.Helpers;

/// <summary>
/// テスト用の <see cref="ICameraBridge"/> 実装。要求解像度の checkerboard RGBA8 byte[] を
/// 生成して返す。Bridge 側 FrameId は内部 long カウンタで monotonic に振る (proto に流す
/// frame_id とは独立、CameraService 側で別途振り直される点を検証する用途)。
/// </summary>
internal sealed class FakeCameraBridge : ICameraBridge
{
    private long _bridgeFrameId;

    /// <summary>true なら <see cref="CaptureAsync"/> で必ず <see cref="CameraNotReadyException"/> を投げる。</summary>
    public bool ThrowNotReady { get; set; }

    /// <summary>各 capture 前に挟む遅延 (fps_limit pacing テスト等で利用)。</summary>
    public int? DelayMs { get; set; }

    public async Task<CameraFrame> CaptureAsync(int width, int height, CancellationToken ct)
    {
        if (ThrowNotReady)
        {
            throw new CameraNotReadyException("FakeCameraBridge: simulated not-ready state.");
        }

        if (DelayMs is { } d && d > 0)
        {
            await Task.Delay(d, ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        var pixels = CreateCheckerboard(width, height);
        var unixNanos = (DateTimeOffset.UtcNow.UtcTicks - DateTime.UnixEpoch.Ticks) * 100L;
        var id = Interlocked.Increment(ref _bridgeFrameId);

        return new CameraFrame(
            Pixels: pixels,
            Width: width,
            Height: height,
            UnixNanos: unixNanos,
            FrameId: id,
            Format: CameraFrameFormat.Rgba8
        );
    }

    private static byte[] CreateCheckerboard(int width, int height)
    {
        // 8 ピクセル角の白黒チェッカーボード。RGBA byte order、row 0 = top。
        const int tile = 8;
        var buffer = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var isWhite = ((x / tile) + (y / tile)) % 2 == 0;
                var i = ((y * width) + x) * 4;
                var v = (byte)(isWhite ? 0xFF : 0x00);
                buffer[i + 0] = v; // R
                buffer[i + 1] = v; // G
                buffer[i + 2] = v; // B
                buffer[i + 3] = 0xFF; // A
            }
        }
        return buffer;
    }
}
