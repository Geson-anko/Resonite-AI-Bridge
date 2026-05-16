namespace ResoniteIO.Core.Logging;

/// <summary>
/// Core 層が利用する最小ログ抽象。BepInEx <c>ManualLogSource</c> 等の実装を
/// Mod 側がアダプトして注入する。Core は engine フレームワークを知らないため
/// 本 IF 経由でのみログを出す。
/// </summary>
public interface ILogSink
{
    void LogDebug(string message);
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
}
