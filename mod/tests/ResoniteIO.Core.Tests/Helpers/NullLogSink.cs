using ResoniteIO.Core.Logging;

namespace ResoniteIO.Core.Tests.Helpers;

/// <summary>
/// <see cref="ILogSink"/> の no-op 実装。Core 層のログを捨てるテスト用 double。
/// </summary>
internal sealed class NullLogSink : ILogSink
{
    public void LogDebug(string message) { }

    public void LogInfo(string message) { }

    public void LogWarning(string message) { }

    public void LogError(string message) { }
}
