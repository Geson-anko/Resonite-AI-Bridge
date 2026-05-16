using ResoniteIO.Core.Tests.Helpers;
using Xunit;

namespace ResoniteIO.Core.Tests;

[Collection("SessionHostEnv")]
public sealed class SessionHostLifecycleTests
{
    [Fact]
    public async Task UnlinksSocket_AfterDispose()
    {
        var harness = await SessionHostHarness.StartAsync();
        var socketPath = harness.SocketPath;
        Assert.True(
            File.Exists(socketPath),
            $"socket file must exist after StartAsync: {socketPath}"
        );

        await harness.DisposeAsync();

        await TestPolling.WaitUntilAsync(
            () => !File.Exists(socketPath),
            TimeSpan.FromSeconds(5),
            $"socket file was not unlinked after shutdown: {socketPath}"
        );
    }
}
