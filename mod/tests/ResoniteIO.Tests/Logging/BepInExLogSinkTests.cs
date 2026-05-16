using System;
using System.Collections.Generic;
using BepInEx.Logging;
using ResoniteIO.Logging;
using Xunit;

namespace ResoniteIO.Tests.Logging;

/// <summary>
/// <see cref="BepInExLogSink"/> が各ログレベル呼び出しを <see cref="ManualLogSource"/>
/// 経由で BepInEx の <see cref="Logger.Events"/> パイプラインに正しく流すかを検証する。
/// </summary>
public sealed class BepInExLogSinkTests
{
    [Fact]
    public void Logs_AreForwarded_With_MatchingLevel_And_Message()
    {
        var source = new ManualLogSource("ResoniteIO.Tests.BepInExLogSink");
        var captured = new List<(LogLevel Level, object Data)>();
        EventHandler<LogEventArgs> handler = (_, args) => captured.Add((args.Level, args.Data));
        source.LogEvent += handler;
        try
        {
            BepInExLogSink sink = new(source);

            sink.LogDebug("debug-msg");
            sink.LogInfo("info-msg");
            sink.LogWarning("warning-msg");
            sink.LogError("error-msg");
        }
        finally
        {
            source.LogEvent -= handler;
        }

        Assert.Equal(4, captured.Count);
        Assert.Equal(LogLevel.Debug, captured[0].Level);
        Assert.Equal("debug-msg", captured[0].Data?.ToString());
        Assert.Equal(LogLevel.Info, captured[1].Level);
        Assert.Equal("info-msg", captured[1].Data?.ToString());
        Assert.Equal(LogLevel.Warning, captured[2].Level);
        Assert.Equal("warning-msg", captured[2].Data?.ToString());
        Assert.Equal(LogLevel.Error, captured[3].Level);
        Assert.Equal("error-msg", captured[3].Data?.ToString());
    }
}
