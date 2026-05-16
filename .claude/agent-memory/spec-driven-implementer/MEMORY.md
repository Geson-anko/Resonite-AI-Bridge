# Memory Index — spec-driven-implementer

## Feedback

- [check HEAD before implementing](feedback_check_head_before_implementing.md) — On a feature branch matching the task name, verify prior commits haven't already landed the work before re-implementing.
- [grpc-tools message-type duplication in test projects](feedback_grpc_tools_message_duplication.md) — Core で Server stub、Tests で Client stub を別生成すると message 型が CS0436 で重複警告。テスト csproj 限定で NoWarn 抑制する。
- [Engine.OnShutdown subscription deferred to Step 3](feedback_engine_onshutdown_deferred.md) — mod 停止は AppDomain.ProcessExit best-effort。Engine.OnShutdown 経由のより早い hook 調査は Step 3 で再評価。
- [BepInEx mod の transitive DLL 同梱](feedback_bepinex_mod_transitive_dlls.md) — CopyLocalLockFileAssemblies=true + PostBuild Copy 双方が必要。framework reference は別経路で要 E2E 検証。
