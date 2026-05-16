using BepInEx.Logging;
using ResoniteIO.Core.Logging;

namespace ResoniteIO.Logging;

internal sealed class BepInExLogSink : ILogSink
{
    private readonly ManualLogSource _source;

    public BepInExLogSink(ManualLogSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public void LogDebug(string message) => _source.LogDebug(message);

    public void LogInfo(string message) => _source.LogInfo(message);

    public void LogWarning(string message) => _source.LogWarning(message);

    public void LogError(string message) => _source.LogError(message);
}
