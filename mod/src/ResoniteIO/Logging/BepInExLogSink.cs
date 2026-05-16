using BepInEx.Logging;
using ResoniteIO.Core.Logging;

namespace ResoniteIO.Logging;

/// <summary>
/// Core 層の <see cref="ILogSink"/> を BepInEx <see cref="ManualLogSource"/>
/// にブリッジする internal アダプタ。
/// </summary>
/// <remarks>
/// Core は BepInEx を直接参照しないため、mod 側で本実装を注入する。
/// 単純なメッセージ転送のみで、フォーマットや filtering は行わない。
/// </remarks>
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
