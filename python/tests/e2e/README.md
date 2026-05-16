# E2E tests (live Resonite)

These tests exercise the full stack against an actual running Resonite
client. They are **excluded from default pytest collection** (see
`pyproject.toml`: `addopts = ["--ignore=tests/e2e"]`) and only execute
when explicitly targeted.

## Prerequisites

1. **Host-agent running on host (GUI session, foreground):**

   ```bash
   just host-agent
   ```

   This brings up the debug bridge daemon (`~/.resonite-io-debug/host-agent.sock`).
   See `scripts/host_agent.py` for details.

2. **`.env` configured** with a `GaleProfile` that has BepisLoader + the
   `ResoniteIO` mod installed (`just deploy-mod` deploys the local build
   into the Gale profile).

3. **Resonite installed** (Linux native FrooxEngine + Proton-managed
   Renderite). `just init` walks through the host-side preconditions.

## Run

From the dev container:

```bash
cd python && uv run pytest tests/e2e/ -m e2e -v
```

The test orchestrates:

- `just resonite-start` (boots Resonite via Gale)
- Polls `~/.resonite-io/resonite-*.sock` until the mod binds the UDS
  (up to 120 s).
- Calls `Session.Ping("e2e-smoke")` once via `SessionClient`.
- `just resonite-stop` in `finally:` so Resonite is stopped even on
  failure.

If host-agent is not running on host, the test will skip with a clear
message.

## Scope (Step 2)

Only one smoke case is implemented. Continuous pings, error paths
(stopping Resonite mid-call, missing mod), and multi-modality tests
land in later Steps.
