namespace ResoniteIO.Core.Bridge;

/// <summary>
/// Mod 層が FrooxEngine の Session 状態を Core 層へ露出するためのコールバック IF。
/// Core はこの IF 経由でのみ engine state を読む (FrooxEngine 型を直接知らない)。
/// </summary>
/// <remarks>
/// 実装側 (mod の <c>FrooxEngineSessionBridge</c>) は engine update tick 上で
/// 内部 snapshot を更新し、本プロパティの read は任意スレッドから cost-free に
/// 行える前提とする。両プロパティとも null は「まだ focused world / local user が
/// 確定していない」状態 (Engine.OnReady 直後など) を意味する。
/// </remarks>
public interface ISessionBridge
{
    /// <summary>現在 focus されている World の名前。未確定なら null。</summary>
    string? FocusedWorldName { get; }

    /// <summary>focused world 上の LocalUser の UserName。未確定なら null。</summary>
    string? LocalUserName { get; }
}
