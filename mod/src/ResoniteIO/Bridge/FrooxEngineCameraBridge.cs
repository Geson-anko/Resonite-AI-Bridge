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
/// LocalUser の HeadSlot 配下に <see cref="Camera"/> を生成し RGBA8 raw を返す
/// <see cref="ICameraBridge"/> 実装。
/// </summary>
/// <remarks>
/// 初期化は lazy で、LocalUser や FocusedWorld が未準備なら
/// <see cref="CameraNotReadyException"/> を投げる (Service が <c>FailedPrecondition</c>
/// に翻訳し client が retry できる契約)。詳細な engine 制約 (engine-thread dispatch、
/// FlipY 正規化、TextureFormat 強制) は各メソッドのコメント参照。
/// </remarks>
internal sealed class FrooxEngineCameraBridge : ICameraBridge, IDisposable
{
    private const int BytesPerPixel = 4;

    // world boot 中などで engine tick が遅延しても RunSynchronously を待ち抜けられる長さ。
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

        // Camera.GetRenderSettings の default は ARGB32 (Unity 側 byte 順 A,R,G,B) で
        // proto の RGBA8 契約と不整合。textureFormat を上書きできるよう RenderTask を
        // 自前で組み立て、RenderManager.RenderToBitmap を直接叩く。
        var renderTask = camera.GetRenderSettings(new int2(width, height));
        renderTask.parameters.textureFormat = TextureFormat.RGBA32;

        Bitmap2D bitmap;
        try
        {
            bitmap = await camera.World.Render.RenderToBitmap(renderTask).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 一過性失敗 (world 遷移など) は NotReady に丸めて Service で
            // FailedPrecondition に翻訳。予期せぬ例外はそのまま Internal に伝搬。
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
                // best-effort: 失敗時は engine GC 任せ。
            }
        }
    }

    private async Task<Camera> EnsureCameraAsync(CancellationToken ct)
    {
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

        // RunSynchronously をまたいで camera を新規生成する slow path。
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

            // Destroy は engine thread 上でしか呼べないので参照だけ先にクリアし、
            // 新 slot 生成と同じ RunSynchronously 内で順序を保って破棄する。
            var oldSlot = lockedSlot;
            _camera = null;
            _cameraSlot = null;

            afterLock.RunSynchronously(() =>
            {
                try
                {
                    if (oldSlot is not null && !oldSlot.IsDestroyed)
                    {
                        try
                        {
                            oldSlot.Destroy();
                        }
                        catch
                        {
                            // best-effort: 再生成側には影響しない。
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
                    // ユーザーがダッシュ等で実際に見ている UI を含めてレンダリングする
                    // (Plan §1 で renderer 側 culling コードで確認済み)。
                    camera.RenderPrivateUI = true;
                    camera.Postprocessing.Value = true;
                    camera.Clear.Value = CameraClearMode.Skybox;

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
    /// Bitmap2D を proto 規約の top-left origin RGBA byte[] に正規化する。
    /// <c>FlipY=true</c> なら行単位で逆順コピー、<c>FlipY=false</c> なら平コピー
    /// (いずれも Buffer 寿命から detach させる)。byte 順は upstream で
    /// <c>TextureFormat.RGBA32</c> 指定済みなので変換不要。
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

        // ProcessExit 経路を含むため例外は飲む。engine shutdown 後の RunSynchronously は
        // no-op (IsDisposed) で安全。
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
                    // best-effort
                }
            });
        }
        catch
        {
            // best-effort
        }
    }
}
