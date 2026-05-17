namespace ResoniteIO.Core.Bridge;

/// <summary>
/// Core 層が映像フレームを取得するための抽象。Mod 側 (FrooxEngine) が実装し DI で注入する。
/// </summary>
/// <remarks>
/// <see cref="CaptureAsync"/> は任意スレッドから呼ばれる。実装側で engine update tick への
/// dispatch が必要なら隠蔽すること。Core は <see cref="CameraFrame"/> POCO のみを扱い、
/// FrooxEngine の <c>Bitmap2D</c> や proto 生成型をここに漏らさない。
/// </remarks>
public interface ICameraBridge
{
    /// <summary>
    /// 要求された解像度で 1 フレームをキャプチャする。pixels は
    /// <see cref="CameraFrameFormat.Rgba8"/> 時 row 0 = 画像上端 (top-left origin) で返す
    /// ことを契約とする (proto API 規約を Bridge IF レベルで強制)。
    /// </summary>
    /// <exception cref="CameraNotReadyException">
    /// engine がまだフレームを返せる状態に無い (LocalUser 未生成、world 切り替え中等)。
    /// </exception>
    Task<CameraFrame> CaptureAsync(int width, int height, CancellationToken ct);
}

/// <summary>
/// 1 フレーム分の raw pixel と metadata。proto 生成型 <c>V1.CameraFrame</c> とは独立した
/// Core POCO として保ち、Bridge / Service 間の契約境界を明確化する。
/// </summary>
public readonly record struct CameraFrame(
    byte[] Pixels,
    int Width,
    int Height,
    long UnixNanos,
    long FrameId,
    CameraFrameFormat Format
);

/// <summary>
/// pixels の byte 配置を識別する。proto 側 <c>V1.CameraFrameFormat</c> と 1:1 対応するが、
/// Core 層では proto 型を露出しないため別 enum として定義する。
/// </summary>
public enum CameraFrameFormat
{
    Unspecified = 0,

    /// <summary>1 ピクセル 4 byte (R, G, B, A の順)、row 0 = 画像上端 (top-left origin)。</summary>
    Rgba8 = 1,
}

/// <summary>
/// Bridge が現時点でフレームを返せないことを示す回復可能例外。Service 層は
/// <c>Status.FailedPrecondition</c> に翻訳し、Client は再 stream で retry する。
/// </summary>
public sealed class CameraNotReadyException : Exception
{
    public CameraNotReadyException(string message)
        : base(message) { }

    public CameraNotReadyException(string message, Exception innerException)
        : base(message, innerException) { }
}
