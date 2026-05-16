using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using BepInEx.Logging;

namespace ResoniteIO.Loading;

/// <summary>
/// Plugin folder 同梱の隣接 DLL (Microsoft.AspNetCore.* / Grpc.AspNetCore.* 等) を
/// Default <see cref="AssemblyLoadContext"/> から probe させる fallback リゾルバ。
/// </summary>
/// <remarks>
/// BepInEx は plugin folder を Default ALC の probe path に登録しないため、これが無いと
/// runtime が <see cref="FileNotFoundException"/> を投げる。
/// 寿命は plugin と一致させる: dispose 後の lazy ロードは同じ理由で fail する。
/// <para>
/// <c>ILogSink</c> ではなく BepInEx <see cref="ManualLogSource"/> を直接受ける理由:
/// resolver は Resonite 同梱の旧 Google.Protobuf より先に attach されなければならない。
/// ここで <c>ILogSink</c> を経由すると <c>ResoniteIO.Core.dll</c> が早期ロードされ、
/// 同 dll が依存する Google.Protobuf を Resonite 側 (旧版) から引いてしまう。
/// </para>
/// </remarks>
internal sealed class PluginAssemblyResolver : IDisposable
{
    private readonly string _pluginDirectory;
    private readonly ManualLogSource _log;
    private bool _disposed;

    public PluginAssemblyResolver(string pluginDirectory, ManualLogSource log)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginDirectory);
        ArgumentNullException.ThrowIfNull(log);

        _pluginDirectory = pluginDirectory;
        _log = log;
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private Assembly? Resolve(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (assemblyName.Name is null)
        {
            return null;
        }

        var candidate = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (!File.Exists(candidate))
        {
            return null;
        }

        try
        {
            return context.LoadFromAssemblyPath(candidate);
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                $"Failed to resolve '{assemblyName.Name}' from plugin folder: {ex.Message}"
            );
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        AssemblyLoadContext.Default.Resolving -= Resolve;
    }
}
