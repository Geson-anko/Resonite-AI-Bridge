using System.Reflection;
using Xunit;

namespace ResoniteIO.Tests;

/// <summary>
/// プラグインアセンブリのメタデータが想定通りであることを検証する smoke test。
/// </summary>
/// <remarks>
/// BepInEx ランタイムが居ない環境で <see cref="ResoniteIOPlugin.Load"/> を
/// 直接呼ぶと <c>BasePlugin</c> 内部の <c>Log</c> が初期化されておらず NRE になる。
/// そのため本テストはアセンブリ名・プラグイン GUID/Name/Version などの
/// 静的に検証可能な情報のみを確認する。E2E (Resonite 起動を伴うロード確認) は
/// <c>mod/tests/manual/</c> 配下の手順書で扱う。
/// </remarks>
public sealed class ResoniteIOPluginTests
{
    [Fact]
    public void AssemblyName_Matches_ProjectAssemblyName()
    {
        var asm = typeof(ResoniteIOPlugin).Assembly;
        Assert.Equal("ResoniteIO", asm.GetName().Name);
    }

    [Fact]
    public void PluginType_HasBepInPluginAttribute_WithMatchingMetadata()
    {
        var attr = typeof(ResoniteIOPlugin).GetCustomAttribute<BepInEx.BepInPlugin>();
        Assert.NotNull(attr);
        Assert.Equal(ResoniteIOPlugin.PluginGuid, attr!.GUID);
        Assert.Equal(ResoniteIOPlugin.PluginName, attr.Name);
        Assert.Equal(ResoniteIOPlugin.PluginVersion, attr.Version.ToString());
    }
}
