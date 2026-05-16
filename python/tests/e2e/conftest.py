"""Common fixtures for E2E tests requiring a live Resonite + host-agent.

The fixtures defined here own the lifecycle of the Resonite process and
the UDS socket exposed by the mod, so individual e2e tests can focus on
the scenario under verification rather than setup/teardown plumbing.
"""

from __future__ import annotations

import os
import subprocess
import time
from collections.abc import Iterator
from pathlib import Path

import pytest

REPO_ROOT: Path = Path(__file__).resolve().parents[2]
SOCKET_DIR: Path = Path.home() / ".resonite-io"
SOCKET_GLOB = "resonite-*.sock"
SOCKET_APPEAR_TIMEOUT_S = 120.0
SOCKET_APPEAR_POLL_S = 1.0
DEBUG_SOCKET: Path = Path.home() / ".resonite-io-debug" / "host-agent.sock"


def _wait_for_socket(directory: Path, timeout_s: float) -> Path:
    """Poll *directory* for exactly one Resonite IO socket and return it.

    Mod が SessionHost を bind するまで最大 ``timeout_s`` 秒待つ。複数 socket が
    残っているケースはテストの安全側として ambiguous エラーにしたいので、
    1 個に揃ったときだけ返す。
    """
    deadline = time.monotonic() + timeout_s
    last_candidates: list[Path] = []
    while time.monotonic() < deadline:
        if directory.is_dir():
            candidates = sorted(directory.glob(SOCKET_GLOB))
            if len(candidates) == 1:
                return candidates[0]
            last_candidates = candidates
        time.sleep(SOCKET_APPEAR_POLL_S)
    raise AssertionError(
        f"Timed out waiting for Resonite IO socket under {directory} "
        f"after {timeout_s:.0f}s. Last seen: {last_candidates}"
    )


def _run_just(
    *args: str, check: bool = True, timeout: float = 60.0
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["just", *args],
        cwd=REPO_ROOT,
        check=check,
        text=True,
        capture_output=True,
        timeout=timeout,
    )


def _purge_stale_sockets(directory: Path) -> None:
    """Remove leftover socket files from prior runs.

    Mod の ``AppDomain.ProcessExit`` cleanup は ``just resonite-stop`` の SIGKILL
    で skip されることがあり、stale socket が残ると次回 run で
    ``ConnectionRefusedError`` を踏む。テスト開始直前に必ず一掃する。
    """
    if not directory.is_dir():
        return
    for sock in directory.glob(SOCKET_GLOB):
        sock.unlink(missing_ok=True)


@pytest.fixture(autouse=True)
def require_host_agent() -> None:
    """Skip the whole e2e test when host-agent is not running."""
    if not DEBUG_SOCKET.exists():
        pytest.skip(
            "host-agent is not running on host (expected socket at "
            f"{DEBUG_SOCKET}). Start it with `just host-agent` on the host."
        )


@pytest.fixture
def resonite_session() -> Iterator[Path]:
    """Start Resonite via host-agent, expose the bound socket, stop on
    teardown.

    Yields the active ``$HOME/.resonite-io/resonite-<pid>.sock`` path with
    ``RESONITE_IO_SOCKET`` already pointing at it, so ``SessionClient`` picks
    the right socket without further wiring. The Resonite process is always
    stopped and stale sockets purged in the ``finally`` branch, even if the
    test body raises.
    """
    _purge_stale_sockets(SOCKET_DIR)
    _run_just("resonite-start")
    try:
        socket_path = _wait_for_socket(SOCKET_DIR, SOCKET_APPEAR_TIMEOUT_S)
        os.environ["RESONITE_IO_SOCKET"] = str(socket_path)
        yield socket_path
    finally:
        # Resonite stop is best-effort: it may have already died. The stale
        # socket purge below covers SIGKILL paths where the mod's
        # ProcessExit cleanup didn't run.
        _run_just("resonite-stop", check=False, timeout=30.0)
        os.environ.pop("RESONITE_IO_SOCKET", None)
        _purge_stale_sockets(SOCKET_DIR)
