using Grpc.Core;
using ResoniteIO.Core.Logging;
using ResoniteIO.V1;

namespace ResoniteIO.Core.Session;

/// <summary><c>resonite_io.v1.Session</c> サービスの Core 実装。</summary>
public sealed class SessionService : V1.Session.SessionBase
{
    private readonly ILogSink _log;

    public SessionService(ILogSink log)
    {
        _log = log;
    }

    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        _log.LogDebug($"Session.Ping received: \"{request.Message}\"");
        var response = new PingResponse
        {
            Message = request.Message,
            // Tick は 100 ns 単位。ns に拡張するため ×100。Stopwatch 補正は不要 (Tick 精度で
            // clock skew / RTT 計測には十分)。
            ServerUnixNanos = (DateTimeOffset.UtcNow.UtcTicks - DateTime.UnixEpoch.Ticks) * 100L,
        };
        return Task.FromResult(response);
    }
}
