using ResoniteIO.Core.Logging;

namespace ResoniteIO.Core.Tests.Helpers;

internal sealed class NullLogSink : ILogSink
{
    public void LogDebug(string message) { }

    public void LogInfo(string message) { }

    public void LogWarning(string message) { }

    public void LogError(string message) { }
}
