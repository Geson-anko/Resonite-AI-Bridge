using Xunit;

namespace ResoniteIO.Core.Tests.Helpers;

/// <summary>
/// 短時間で完了する非同期条件を polling 確認するためのユーティリティ。
/// Kestrel の bind 完了や socket file の出現/消滅を待つテスト本筋の外側で利用する。
/// </summary>
internal static class TestPolling
{
    /// <summary>
    /// <paramref name="predicate"/> が true を返すまで 50 ms 刻みで polling する。
    /// <paramref name="timeout"/> を超えても false なら <see cref="Assert.Fail(string)"/>
    /// で <paramref name="failureMessage"/> 付き fail させる。
    /// </summary>
    public static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        string failureMessage
    )
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail(failureMessage);
    }
}
