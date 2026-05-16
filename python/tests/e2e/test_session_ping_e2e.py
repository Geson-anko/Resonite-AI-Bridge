"""End-to-end smoke for Session.Ping against a live Resonite instance.

Prerequisites:
  - Run `just host-agent` on the host (GUI session, foreground).
  - Configure `.env` with a valid Gale profile that has BepisLoader installed.
  - Build the mod once: `just deploy-mod`.
Then from inside the dev container:
  cd python && uv run pytest tests/e2e/ -m e2e -v
"""

from __future__ import annotations

import asyncio
import os
import subprocess
import time
from pathlib import Path

import pytest

from resoio.session import SessionClient

REPO_ROOT = Path(__file__).resolve().parents[3]
SOCKET_DIR_REL = "resonite-io"
SOCKET_GLOB = "resonite-*.sock"
SOCKET_APPEAR_TIMEOUT_S = 120.0
SOCKET_APPEAR_POLL_S = 1.0


def _socket_dir() -> Path:
    xdg = os.environ.get("XDG_RUNTIME_DIR")
    if not xdg:
        pytest.fail("XDG_RUNTIME_DIR is not set in this environment.")
    return Path(xdg) / SOCKET_DIR_REL


def _wait_for_socket(directory: Path, timeout_s: float) -> Path:
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


@pytest.mark.e2e
def test_session_ping_e2e_smoke() -> None:
    """Boot Resonite via host-agent, send one Ping, then shut Resonite down."""
    _run_just("resonite-start")
    try:
        socket_path = _wait_for_socket(_socket_dir(), SOCKET_APPEAR_TIMEOUT_S)
        os.environ["RESONITE_IO_SOCKET"] = str(socket_path)

        async def _ping_once() -> None:
            async with SessionClient() as client:
                response = await client.ping("e2e-smoke")
            assert response.message == "e2e-smoke"
            assert response.server_unix_nanos > 0

        asyncio.run(_ping_once())
    finally:
        # Always stop Resonite. check=False because stop may exit non-zero
        # if Resonite already died for some reason.
        _run_just("resonite-stop", check=False, timeout=30.0)
        os.environ.pop("RESONITE_IO_SOCKET", None)
