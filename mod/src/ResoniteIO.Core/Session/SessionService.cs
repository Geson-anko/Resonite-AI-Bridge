using Grpc.Core;
using ResoniteIO.Core.Logging;
using ResoniteIO.V1;

namespace ResoniteIO.Core.Session;

/// <summary>
/// <c>resonite_io.v1.Session</c> サービスの Core 実装。受信した
/// <see cref="PingRequest"/> を echo し、サーバ側の Unix epoch (ナノ秒スケール) を
/// 付与して返す。
/// </summary>
/// <remarks>
/// <see cref="DateTimeOffset.UtcNow"/> の Tick (100 ns 単位) を Unix epoch との差で
/// 取り、100 倍してナノ秒スケールへ拡大する。<see cref="System.Diagnostics.Stopwatch"/>
/// 起点の高精度補正は YAGNI として見送る (clock skew や RTT の概算用途では Tick 精度で
/// 十分; OS が提供するクロック分解能を超える精度が必要になった段階で再検討する)。
/// </remarks>
public sealed class SessionService : V1.Session.SessionBase
{
    private readonly ILogSink _log;

    /// <summary>
    /// <see cref="SessionService"/> を生成する。
    /// </summary>
    /// <param name="log">Core が利用するログシンク。DI 経由で注入される。</param>
    public SessionService(ILogSink log)
    {
        _log = log;
    }

    /// <inheritdoc />
    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        _log.LogDebug($"Session.Ping received: \"{request.Message}\"");
        var response = new PingResponse
        {
            Message = request.Message,
            ServerUnixNanos = (DateTimeOffset.UtcNow.UtcTicks - DateTime.UnixEpoch.Ticks) * 100L,
        };
        return Task.FromResult(response);
    }
}
