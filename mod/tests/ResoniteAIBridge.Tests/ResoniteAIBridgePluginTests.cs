using System.Reflection;
using Xunit;

namespace ResoniteAIBridge.Tests;

/// <summary>
/// プラグインアセンブリのメタデータが想定通りであることを検証する smoke test。
/// </summary>
/// <remarks>
/// BepInEx ランタイムが居ない環境で <see cref="ResoniteAIBridgePlugin.Load"/> を
/// 直接呼ぶと <c>BasePlugin</c> 内部の <c>Log</c> が初期化されておらず NRE になる。
/// そのため本テストはアセンブリ名・プラグイン GUID/Name/Version などの
/// 静的に検証可能な情報のみを確認する。E2E (Resonite 起動を伴うロード確認) は
/// <c>mod/tests/manual/</c> 配下の手順書で扱う。
/// </remarks>
public sealed class ResoniteAIBridgePluginTests
{
    [Fact]
    public void AssemblyName_Matches_ProjectAssemblyName()
    {
        var asm = typeof(ResoniteAIBridgePlugin).Assembly;
        Assert.Equal("ResoniteAIBridge", asm.GetName().Name);
    }

    [Fact]
    public void PluginConstants_AreNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(ResoniteAIBridgePlugin.PluginGuid));
        Assert.False(string.IsNullOrWhiteSpace(ResoniteAIBridgePlugin.PluginName));
        Assert.False(string.IsNullOrWhiteSpace(ResoniteAIBridgePlugin.PluginVersion));
    }

    [Fact]
    public void PluginType_HasBepInPluginAttribute_WithMatchingMetadata()
    {
        var attr = typeof(ResoniteAIBridgePlugin).GetCustomAttribute<BepInEx.BepInPlugin>();
        Assert.NotNull(attr);
        Assert.Equal(ResoniteAIBridgePlugin.PluginGuid, attr!.GUID);
        Assert.Equal(ResoniteAIBridgePlugin.PluginName, attr.Name);
        Assert.Equal(ResoniteAIBridgePlugin.PluginVersion, attr.Version.ToString());
    }
}
