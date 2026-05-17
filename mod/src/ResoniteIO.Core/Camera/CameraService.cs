using System.Diagnostics;
using Google.Protobuf;
using Grpc.Core;
using ResoniteIO.Core.Bridge;
using ResoniteIO.Core.Logging;

#pragma warning disable CA1031 // catch (Exception) は Bridge 側の任意例外を gRPC Status に翻訳するために必要

namespace ResoniteIO.Core.Camera;

/// <summary><c>resonite_io.v1.Camera</c> サービスの Core 実装。</summary>
/// <remarks>
/// <see cref="ICameraBridge"/> は optional DI。null の場合 (例: Core 単体テストで bridge
/// 未注入、または engine が camera を提供しない構成) は <c>Status.Unavailable</c> を返す。
/// </remarks>
public sealed class CameraService : V1.Camera.CameraBase
{
    private const int DefaultWidth = 640;
    private const int DefaultHeight = 480;

    private readonly ICameraBridge? _bridge;
    private readonly ILogSink _log;

    public CameraService(ILogSink log, ICameraBridge? bridge = null)
    {
        _log = log;
        _bridge = bridge;
    }

    /// <summary>
    /// Bridge から 1 フレームずつ取り出して proto に詰めて流す server-streaming RPC。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 解像度は <c>request.Width</c>/<c>Height</c> が 0 以下のとき
    /// <see cref="DefaultWidth"/> × <see cref="DefaultHeight"/> (640×480) を採用する。
    /// <c>fps_limit</c> が 0 以下なら pacing 無し (Bridge が返せる最速)、それ以上なら
    /// 1 フレーム周期 = <c>1/fps_limit</c> 秒で sleep する best-effort pacing。
    /// </para>
    /// <para>
    /// 例外翻訳: <see cref="ICameraBridge"/> 未注入は <c>Unavailable</c>、
    /// <see cref="CameraNotReadyException"/> は <c>FailedPrecondition</c>、その他の Bridge
    /// 例外は <c>Internal</c>。<c>FailedPrecondition</c> はクライアントが時間を置いて
    /// 再 stream する想定 (LocalUser や world が ready 待ちのとき発生する)。
    /// </para>
    /// <para>
    /// <c>frame_id</c> は本 service の monotonic counter で 0 から振り直す
    /// (Bridge 側 ID とは独立)。client は同一 stream 内のフレームを抜けや並び替え無しに
    /// 識別できる。
    /// </para>
    /// </remarks>
    public override async Task StreamFrames(
        V1.CameraStreamRequest request,
        IServerStreamWriter<V1.CameraFrame> responses,
        ServerCallContext context
    )
    {
        if (_bridge is null)
        {
            _log.LogWarning(
                "Camera.StreamFrames called but no ICameraBridge is registered; "
                    + "returning Unavailable."
            );
            throw new RpcException(
                new Status(StatusCode.Unavailable, "Camera bridge is not configured.")
            );
        }

        var width = request.Width > 0 ? request.Width : DefaultWidth;
        var height = request.Height > 0 ? request.Height : DefaultHeight;
        var frameDelay =
            request.FpsLimit > 0f ? TimeSpan.FromSeconds(1.0 / request.FpsLimit) : TimeSpan.Zero;

        _log.LogDebug(
            $"Camera.StreamFrames start: width={width} height={height} "
                + $"fps_limit={request.FpsLimit}"
        );

        var ct = context.CancellationToken;
        long protoFrameId = 0;
        var stopwatch = new Stopwatch();

        while (!ct.IsCancellationRequested)
        {
            stopwatch.Restart();

            CameraFrame coreFrame;
            try
            {
                coreFrame = await _bridge.CaptureAsync(width, height, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (CameraNotReadyException ex)
            {
                _log.LogInfo($"Camera.StreamFrames: bridge not ready: {ex.Message}");
                throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
            }
            catch (Exception ex)
            {
                _log.LogError($"Camera.StreamFrames: bridge faulted: {ex}");
                throw new RpcException(
                    new Status(StatusCode.Internal, $"Camera bridge faulted: {ex.Message}")
                );
            }

            var protoFrame = MapToProto(coreFrame, protoFrameId);
            protoFrameId++;

            try
            {
                await responses.WriteAsync(protoFrame, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException ex)
            {
                // Client が disconnect すると WriteAsync が "response stream is completed"
                // 系の InvalidOperationException で抜ける実装がある。break 扱いで終了する。
                _log.LogDebug($"Camera.StreamFrames: write aborted: {ex.Message}");
                break;
            }

            if (frameDelay > TimeSpan.Zero)
            {
                var remaining = frameDelay - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(remaining, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        _log.LogDebug($"Camera.StreamFrames end: emitted {protoFrameId} frame(s)");
    }

    private static V1.CameraFrame MapToProto(CameraFrame frame, long protoFrameId)
    {
        return new V1.CameraFrame
        {
            Width = frame.Width,
            Height = frame.Height,
            Format = ToProtoFormat(frame.Format),
            UnixNanos = frame.UnixNanos,
            // service 側で monotonic に振る (Bridge 側 FrameId とは独立)。
            FrameId = protoFrameId,
            // Bridge から渡された byte[] は per-capture で新規確保される契約だが、
            // 防衛的に CopyFrom で defensive copy する (proto 側保持期間と Bridge buffer
            // 寿命を独立させるため)。64MB クラスのフレームで perf 問題化したら
            // UnsafeByteOperations.UnsafeWrap への切替を検討する。
            Pixels = ByteString.CopyFrom(frame.Pixels),
        };
    }

    private static V1.CameraFrameFormat ToProtoFormat(CameraFrameFormat format) =>
        format switch
        {
            CameraFrameFormat.Rgba8 => V1.CameraFrameFormat.Rgba8,
            CameraFrameFormat.Unspecified => V1.CameraFrameFormat.Unspecified,
            _ => V1.CameraFrameFormat.Unspecified,
        };
}
