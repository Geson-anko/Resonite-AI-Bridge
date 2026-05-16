namespace ResoniteIO.Core.Logging;

/// <summary>
/// Core 層のログ抽象。Mod 側で BepInEx 等のロガーをアダプトして注入する。
/// </summary>
public interface ILogSink
{
    void LogDebug(string message);
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
}
