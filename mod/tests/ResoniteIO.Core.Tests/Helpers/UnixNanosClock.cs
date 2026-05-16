namespace ResoniteIO.Core.Tests.Helpers;

/// <summary>
/// テストから Unix epoch (ナノ秒スケール) の現在時刻を取得するためのヘルパー。
/// <c>SessionService</c> が <c>server_unix_nanos</c> に詰める計算と完全に一致させ、
/// timestamp の前後範囲 assertion を tearing なく書けるようにする。
/// </summary>
internal static class UnixNanosClock
{
    /// <summary>Unix epoch からの経過時間をナノ秒 (100 ns Tick × 100) で返す。</summary>
    public static long Now() => (DateTimeOffset.UtcNow.UtcTicks - DateTime.UnixEpoch.Ticks) * 100L;
}
