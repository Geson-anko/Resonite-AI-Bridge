using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using BepInEx.Logging;

namespace ResoniteIO.Loading;

/// <summary>
/// BepInEx plugin folder に同梱した隣接 DLL を <see cref="AssemblyLoadContext.Default"/>
/// から probe するためのアセンブリリゾルバ。Plugin 本体の責務 (engine bridging) から
/// 切り離してこのクラスに閉じ込めている。
/// </summary>
/// <remarks>
/// <para>
/// BepInEx は plugin folder を Default <see cref="AssemblyLoadContext"/> の probe path
/// に登録しないため、<see cref="System.Reflection.Assembly"/> 解決時に
/// <c>Microsoft.AspNetCore.*</c> / <c>Grpc.AspNetCore.*</c> 等の隣接 DLL を
/// runtime が見つけられず <see cref="FileNotFoundException"/> になる。
/// 本リゾルバが <see cref="AssemblyLoadContext.Resolving"/> イベントを購読して、
/// plugin folder 内に同名 DLL があれば <see cref="AssemblyLoadContext.LoadFromAssemblyPath"/>
/// で fallback 解決する。
/// </para>
/// <para>
/// <see cref="Dispose"/> でイベント購読を解除する。ただし Resonite プロセス終了直前以外で
/// dispose すると、その後の lazy ロードで <see cref="FileNotFoundException"/> を踏むため、
/// 通常は plugin ライフタイムと同じ寿命で保持する。
/// </para>
/// <para>
/// 本クラスは <c>ResoniteIO.Core</c> の <c>ILogSink</c> を参照しない。理由:
/// resolver が <c>Resonite</c> 同梱の Google.Protobuf 等より先に attach 済みで
/// なければ、plugin folder 同梱の隣接 DLL より旧バージョンが Default 経路から
/// 解決されて衝突する。<c>ILogSink</c> 経由で <c>BepInExLogSink</c> をここで参照
/// すると <c>ResoniteIO.Core.dll</c> が早期にロードされ、その時点で
/// <c>Google.Protobuf</c> を Resonite 側 (旧版) から引きうる。
/// よって BepInEx の <see cref="ManualLogSource"/> を直接使う。
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
