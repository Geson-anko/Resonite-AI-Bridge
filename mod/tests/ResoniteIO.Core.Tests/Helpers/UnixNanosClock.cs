namespace ResoniteIO.Core.Tests.Helpers;

/// <summary>
/// テスト assertion 用 Unix epoch ナノ秒クロック。<c>SessionService</c> 内の計算と
/// 完全一致させてあり、Ping の <c>server_unix_nanos</c> を前後範囲で挟める。
/// </summary>
internal static class UnixNanosClock
{
    public static long Now() => (DateTimeOffset.UtcNow.UtcTicks - DateTime.UnixEpoch.Ticks) * 100L;
}
