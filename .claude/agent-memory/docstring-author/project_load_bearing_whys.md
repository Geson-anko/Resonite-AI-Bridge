---
name: load-bearing-whys
description: Non-obvious WHY comments under mod/ and Core tests that must survive future docstring trim passes (Step 2 + Step 3 surface)
metadata:
  type: project
---

When trimming docstrings/comments under `mod/src/ResoniteIO{,.Core}/` and
`mod/tests/ResoniteIO.Core.Tests/`, these WHY notes are load-bearing and
must NOT be cut. Items 1–5 originate from Step 2 (Session / loader);
items 6–9 originate from Step 3 (Camera).

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
   BridgeWiring **and CameraRoundTrip** tests: `SessionHostHarness`
   mutates the `RESONITE_IO_SOCKET` env var, so any test using the
   harness must serialize via this collection (extended in Step 3 to
   cover Camera tests too).
4. **`csproj` `CopyLocalLockFileAssemblies` + explicit `Microsoft.AspNetCore.*`
   copy + `CopyAspNetCoreSharedFrameworkRuntime` Target** in
   `ResoniteIO.csproj`: required to ship the adjacent DLLs that
   `PluginAssemblyResolver` then probes. The shared-framework copy
   Target is the canonical workaround for AspNetCore framework
   references and must stay paired with the PluginFiles glob — see
   \[\[bepinex-mod-transitive-dlls\]\]. Comments there are out of scope
   for the docstring agent — leave them entirely.
5. **`betterproto2_compiler` separate distribution** in Python deps:
   not a `[compiler]` extra — keep any comment explaining that.
6. **`ICameraBridge` optional DI** in `CameraService`: a `null` bridge
   returns `Status.Unavailable` so Core can be tested without a Bridge
   and camera-less engine configs still load. Keep the remark.
7. **Engine-thread dispatch in `FrooxEngineCameraBridge`**: component
   graph mutations (`AttachComponent`, `Slot.AddSlot` etc.) MUST go
   through `World.RunSynchronously` + `TaskCompletionSource`; pure reads
   (volatile snapshots) do not. Don't strip the comment explaining this
   asymmetry — see \[\[bridge-engine-thread-dispatch\]\].
8. **ProcessExit swallowing in Camera bridge** (`FrooxEngineCameraBridge.cs`
   `OnProcessExit`): `RunSynchronously` becomes a no-op after engine
   shutdown, so exceptions are intentionally drunk. Keep that note —
   it documents an intentional best-effort path tied to
   \[\[engine-onshutdown-deferred\]\].
9. **BGRA8 → RGBA8 conversion rationale** in the Camera bridge: the
   swap was made because raw BGRA8 readback caused a blue tint
   (commit `5129bb6`). Any comment near the conversion site that
   explains this must stay.

**Why:** these WHYs explain non-local behaviour: changing one site
(removing the resolver, dropping the collection, swapping the channel
order back, etc.) silently breaks another. The codebase is still young
(Steps 2-3) so the bug stories aren't in git blame depth yet.

**How to apply:** if a future docstring/comment pass touches these
files, preserve these notes — compress wording, never drop the
substance. When Step 4+ adds new bridges, append a new section here
rather than rewriting earlier entries.
