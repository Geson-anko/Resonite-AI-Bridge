using System;
using System.Threading;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using ResoniteIO.Core.Bridge;
using ResoniteIO.Core.Logging;

namespace ResoniteIO.Bridge;

/// <summary>
/// <see cref="ICameraBridge"/> の FrooxEngine 実装。LocalUser の HeadSlot 配下に
/// <see cref="Camera"/> コンポーネントを生成し、<see cref="Camera.RenderToBitmap"/>
/// で RGBA8 raw を 1 フレーム取り出して Core へ渡す。
/// </summary>
/// <remarks>
/// 設計判断:
/// <list type="bullet">
///   <item>
///     <description>
///       Camera コンポーネント生成はコンポーネントグラフ変更のため engine update tick
///       上で行う必要があり <see cref="World.RunSynchronously"/> + <see cref="TaskCompletionSource"/>
///       でディスパッチする。<see cref="Camera.RenderToBitmap"/> 自体は内部で
///       <c>RenderManager._scheduledRenderTasks</c> を経由するため任意スレッドから
///       <c>await</c> して安全。
///     </description>
///   </item>
///   <item>
///     <description>
///       初期化は lazy。constructor 時点では LocalUser 未生成の可能性があるため
///       初回 <see cref="CaptureAsync"/> 時にチェックし、未準備なら
///       <see cref="CameraNotReadyException"/> を投げ Service 層で
///       <c>FailedPrecondition</c> に翻訳させる。
///     </description>
///   </item>
///   <item>
///     <description>
///       World 切り替えで旧 slot が孤児化しないよう、毎 capture で
///       <c>_cameraSlot?.World == FocusedWorld</c> を確認し不一致なら破棄して再生成する。
///     </description>
///   </item>
///   <item>
///     <description>
///       座標系: proto API として top-left origin (row 0 = 上端) RGBA に固定する契約
///       (<see cref="ICameraBridge"/> docstring 参照)。<see cref="Bitmap"/> が
///       <c>FlipY=true</c> で返ってきた場合は行単位 reverse して正規化する。
///     </description>
///   </item>
///   <item>
///     <description>
///       RenderTask の textureFormat 既定 (<c>ARGB32</c>) は Unity 上で A,R,G,B の byte 順で
///       <c>GetRawTextureData</c> に返るため、proto の RGBA8 契約と整合しない。Bridge では
///       <c>TextureFormat.RGBA32</c> を明示し R,G,B,A の byte 順を強制する (Camera.RenderToBitmap
///       が GetRenderSettings 経由で隠してしまうので、独自に RenderTask を組み立てて
///       <see cref="RenderManager.RenderToBitmap"/> を直接呼ぶ)。
///     </description>
///   </item>
/// </list>
/// </remarks>
internal sealed class FrooxEngineCameraBridge : ICameraBridge, IDisposable
{
    // RGBA8 固定。proto 規約と一致 (ICameraBridge / CameraFrameFormat.Rgba8)。
    private const int BytesPerPixel = 4;

    // engine thread への Camera 生成ディスパッチが噛み合わないケースの保険。
    // RunSynchronously は通常 1 tick (数 ms) 内に消化されるが、world boot 中など
    // tick が遅延する可能性を見て十分長く取る。
    private static readonly TimeSpan EnsureCameraTimeout = TimeSpan.FromSeconds(10);

    private readonly Engine _engine;
    private readonly ILogSink _log;
    private readonly object _initLock = new();
    private volatile Camera? _camera;
    private volatile Slot? _cameraSlot;
    private long _bridgeFrameId;
    private bool _disposed;

    public FrooxEngineCameraBridge(Engine engine, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(log);
        _engine = engine;
        _log = log;
    }

    public async Task<CameraFrame> CaptureAsync(int width, int height, CancellationToken ct)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FrooxEngineCameraBridge));
        }

        var camera = await EnsureCameraAsync(ct).ConfigureAwait(false);

        // RenderTask を独自に組み立てて textureFormat = RGBA32 を強制する。
        // Camera.GetRenderSettings の default は ARGB32 (Unity 側で byte 順は A,R,G,B) で
        // proto の RGBA8 契約と不整合になるため、Camera.RenderToBitmap を使わず
        // RenderManager.RenderToBitmap(RenderTask) を直接叩く。
        var renderTask = camera.GetRenderSettings(new int2(width, height));
        renderTask.parameters.textureFormat = TextureFormat.RGBA32;

        Bitmap2D bitmap;
        try
        {
            bitmap = await camera.World.Render.RenderToBitmap(renderTask).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // engine 側 RenderManager がフレームを返せない一過性失敗 (world 遷移など)
            // は CameraNotReadyException に丸めて Service で FailedPrecondition に翻訳する。
            // 真の予期せぬ例外はそのまま伝搬し Service で Internal にする。
            throw new CameraNotReadyException(
                $"RenderToBitmap failed: {ex.GetType().Name}: {ex.Message}",
                ex
            );
        }

        if (bitmap is null)
        {
            throw new CameraNotReadyException("RenderToBitmap returned null bitmap.");
        }

        try
        {
            bitmap.EnsureManagedMemoryBuffer();
            var pixels = ToTopLeftOriginRgba(bitmap, width, height);
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
        finally
        {
            try
            {
                bitmap.Buffer?.Dispose();
            }
            catch
            {
                // buffer 解放失敗は engine 側 GC 任せにする。
            }
        }
    }

    private async Task<Camera> EnsureCameraAsync(CancellationToken ct)
    {
        // Fast path: 既に有効な camera があり world が変わっていなければそのまま返す。
        var focused = _engine.WorldManager.FocusedWorld;
        if (focused is null)
        {
            throw new CameraNotReadyException("No focused world is currently available.");
        }

        var existing = _camera;
        var existingSlot = _cameraSlot;
        if (
            existing is not null
            && existingSlot is not null
            && !existingSlot.IsDestroyed
            && existingSlot.World == focused
        )
        {
            return existing;
        }

        // Slow path: lock を取ったうえで世代を再確認し、必要なら旧 slot を捨てて再生成。
        // RunSynchronously をまたぐので非同期で待つ。
        Camera? created = null;
        Exception? error = null;
        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        lock (_initLock)
        {
            // double-checked: 別スレッドが先に作っていれば fast path で復帰。
            var afterLock = _engine.WorldManager.FocusedWorld;
            if (afterLock is null)
            {
                throw new CameraNotReadyException("No focused world is currently available.");
            }
            var lockedSlot = _cameraSlot;
            var lockedCamera = _camera;
            if (
                lockedCamera is not null
                && lockedSlot is not null
                && !lockedSlot.IsDestroyed
                && lockedSlot.World == afterLock
            )
            {
                return lockedCamera;
            }

            // 旧 slot を破棄 (world 切替や強制再生成時)。Destroy は engine thread 上で行うため
            // ここでは参照だけクリアし、新 slot 生成の同 RunSynchronously 内で旧 slot.Destroy
            // を実行する (queue 順序が保たれる)。
            var oldSlot = lockedSlot;
            _camera = null;
            _cameraSlot = null;

            afterLock.RunSynchronously(() =>
            {
                try
                {
                    // 旧 slot は world 切替時に既に destroy されているケースもある。
                    if (oldSlot is not null && !oldSlot.IsDestroyed)
                    {
                        try
                        {
                            oldSlot.Destroy();
                        }
                        catch
                        {
                            // 再生成に支障は無いので失敗はログに残すだけ。
                        }
                    }

                    var headSlot = afterLock.LocalUser?.Root?.HeadSlot;
                    if (headSlot is null)
                    {
                        throw new CameraNotReadyException(
                            "LocalUser HeadSlot is not yet available."
                        );
                    }

                    var slot = headSlot.AddSlot("ResoniteIO Camera");
                    var camera = slot.AttachComponent<Camera>();
                    // RenderPrivateUI=true でユーザーが画面で見ているダッシュ UI 等を含めて
                    // レンダリングする (Plan §1 で renderer 側 culling コードで確認済み)。
                    camera.RenderPrivateUI = true;
                    camera.Postprocessing.Value = true;
                    camera.Clear.Value = CameraClearMode.Skybox;
                    // FOV は OnAwake の default 60° のまま (調整は follow-up)。

                    _cameraSlot = slot;
                    _camera = camera;
                    created = camera;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });
        }

        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        using (var timeoutCts = new CancellationTokenSource(EnsureCameraTimeout))
        using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
        {
            await tcs.Task.ConfigureAwait(false);
        }

        if (error is not null)
        {
            if (error is CameraNotReadyException)
            {
                throw error;
            }
            throw new CameraNotReadyException(
                $"Failed to create Camera component: {error.GetType().Name}: {error.Message}",
                error
            );
        }

        if (created is null)
        {
            throw new CameraNotReadyException("Camera component was not created.");
        }

        _log.LogInfo("FrooxEngineCameraBridge: created Camera on HeadSlot");
        return created;
    }

    /// <summary>
    /// Bitmap2D が <c>FlipY=true</c> (bottom-up) で返ってきた場合に行単位で逆順コピーし、
    /// proto 規約の top-left origin RGBA に正規化する。<c>FlipY=false</c> なら単純な
    /// <c>ToArray</c> 相当のコピー (Buffer 寿命と独立した byte[] に detach するため)。
    /// byte 順そのものは upstream で <c>TextureFormat.RGBA32</c> を指定済みなのでここで
    /// 変換不要。
    /// </summary>
    private static byte[] ToTopLeftOriginRgba(Bitmap2D bitmap, int width, int height)
    {
        var stride = width * BytesPerPixel;
        var expected = stride * height;
        var src = bitmap.Buffer.Memory.Span;
        if (src.Length < expected)
        {
            throw new CameraNotReadyException(
                $"Bitmap buffer too small: expected {expected} bytes for {width}x{height}, got {src.Length}."
            );
        }

        var dst = new byte[expected];
        if (bitmap.FlipY)
        {
            // bottom-up な src を row 単位で逆順に dst に詰める。1024² RGBA で ~4MB の
            // memcpy = サブミリ秒。perf 化したら SIMD 検討。
            for (var y = 0; y < height; y++)
            {
                var srcRow = (height - 1 - y) * stride;
                var dstRow = y * stride;
                src.Slice(srcRow, stride).CopyTo(dst.AsSpan(dstRow, stride));
            }
        }
        else
        {
            src.Slice(0, expected).CopyTo(dst);
        }
        return dst;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        var slot = _cameraSlot;
        _cameraSlot = null;
        _camera = null;
        if (slot is null)
        {
            return;
        }

        // engine 側が既に shutdown 済みなら RunSynchronously は no-op (IsDisposed) で安全。
        // 例外は飲む (ProcessExit 経路ではログ経路も信頼できない)。
        try
        {
            var world = slot.World;
            world?.RunSynchronously(() =>
            {
                try
                {
                    if (!slot.IsDestroyed)
                    {
                        slot.Destroy();
                    }
                }
                catch
                {
                    // engine がもう破棄中の可能性。best-effort。
                }
            });
        }
        catch
        {
            // best-effort
        }
    }
}
