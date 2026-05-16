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
/// Step 2 では <see cref="DateTimeOffset.UtcNow"/> の ms 精度をそのままナノ秒スケールへ
/// 拡大して用いる。<see cref="System.Diagnostics.Stopwatch"/> を起点とした高精度補正は
/// YAGNI として見送る (clock skew や RTT の概算用途では ms 粒度で十分)。
/// 必要になった段階で個別 RPC レベルで精度向上を検討する。
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
            ServerUnixNanos = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L,
        };
        return Task.FromResult(response);
    }
}
