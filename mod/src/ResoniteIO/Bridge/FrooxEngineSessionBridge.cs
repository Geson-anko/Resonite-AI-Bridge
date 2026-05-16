using System;
using FrooxEngine;
using ResoniteIO.Core.Bridge;
using ResoniteIO.Core.Logging;

namespace ResoniteIO.Bridge;

/// <summary>
/// FrooxEngine の <see cref="WorldManager.FocusedWorld"/> / <see cref="World.LocalUser"/>
/// を Core 層へ露出する <see cref="ISessionBridge"/> 実装。
/// </summary>
/// <remarks>
/// <para>
/// 実装方針: <see cref="WorldManager.WorldFocused"/> event を購読し、
/// engine update tick 上で <c>volatile</c> snapshot を更新する。Core 側からの
/// プロパティ getter は snapshot を best-effort で読むだけなので、任意スレッド
/// から cost-free に呼べる。値読みで tearing が起きても crash しない
/// (<c>Sync&lt;string&gt;</c> 経由のため; <see cref="World.Name"/> /
/// <see cref="User.UserName"/> ともに参照型の代入で publish される)。
/// </para>
/// <para>
/// 構築時に既に focused world が存在する場合は初期スナップショットも 1 回ログ
/// 出力する (engine ready 直後にホームワールドが既に focus 済みのケースを拾う)。
/// </para>
/// </remarks>
internal sealed class FrooxEngineSessionBridge : ISessionBridge, IDisposable
{
    private readonly WorldManager _worldManager;
    private readonly ILogSink _log;
    private volatile World? _focusedWorld;
    private bool _disposed;

    public FrooxEngineSessionBridge(Engine engine, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(log);

        _worldManager = engine.WorldManager;
        _log = log;

        // 既存 focused world があれば初期 snapshot として採用してログを出す。
        // event を後から subscribe しても、focus 済み状態は通知されないため。
        var initialWorld = _worldManager.FocusedWorld;
        if (initialWorld is not null)
        {
            _focusedWorld = initialWorld;
            LogFocused(initialWorld);
        }

        _worldManager.WorldFocused += OnWorldFocused;
    }

    /// <inheritdoc />
    public string? FocusedWorldName => _focusedWorld?.Name;

    /// <inheritdoc />
    public string? LocalUserName => _focusedWorld?.LocalUser?.UserName;

    private void OnWorldFocused(World world)
    {
        _focusedWorld = world;
        LogFocused(world);
    }

    private void LogFocused(World? world)
    {
        var worldName = world?.Name ?? "<null>";
        var userName = world?.LocalUser?.UserName ?? "<null>";
        _log.LogInfo($"Focused world: {worldName} / LocalUser: {userName}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            _worldManager.WorldFocused -= OnWorldFocused;
        }
        catch
        {
            // best-effort: engine 側が既に破棄されている可能性
        }
    }
}
