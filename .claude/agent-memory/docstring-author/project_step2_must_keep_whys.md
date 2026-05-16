---
name: step2-must-keep-whys
description: Non-obvious WHY comments in Step 2 that must survive future docstring trim passes
metadata:
  type: project
---

When trimming docstrings/comments under `mod/src/ResoniteIO/` and
`mod/tests/ResoniteIO.Core.Tests/`, these WHY notes are load-bearing and
must NOT be cut:

1. **Google.Protobuf early-resolution hazard**
   - `ResoniteIOPlugin.Load`: must not touch any `ResoniteIO.Core` type
     before `PluginAssemblyResolver` is attached, or Resonite's bundled
     old Google.Protobuf wins resolution and SessionHost fails with
     `TypeLoadException: Could not load type 'Google.Protobuf.IBufferMessage'`.
   - `PluginAssemblyResolver`: takes `ManualLogSource` directly instead
     of `ILogSink` for the same reason (Core dll must not preload).
2. **Sync<string> tearing tolerance** in `FrooxEngineSessionBridge`:
   getters can be read from any thread because the underlying values are
   reference-typed publishes via `Sync<string>` — tearing yields a stale
   ref, never a crash.
3. **`[Collection("SessionHostEnv")]`** on RoundTrip / Lifecycle /
   BridgeWiring tests: `SessionHostHarness` mutates the
   `RESONITE_IO_SOCKET` env var, so these tests must serialize.
4. **`csproj` `CopyLocalLockFileAssemblies` + explicit `Microsoft.AspNetCore.*`
   copy** in `ResoniteIO.csproj`: required to ship the adjacent DLLs that
   `PluginAssemblyResolver` then probes. Comments there are out of scope
   for the docstring agent — leave them entirely.
5. **`betterproto2_compiler` separate distribution** in Python deps:
   not a `[compiler]` extra — keep any comment explaining that.

**Why:** these WHYs explain non-local behaviour: changing one site
(removing the resolver, dropping the collection, etc.) silently breaks
another. The codebase is also young (Step 2) so the bug stories aren't
in git blame yet.

**How to apply:** if a future docstring/comment pass touches these
files, preserve these notes — compress wording, never drop the
substance.
