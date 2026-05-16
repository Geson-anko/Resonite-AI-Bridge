"""Common fixtures for E2E tests requiring a live Resonite + host-agent."""

from __future__ import annotations

import os
from pathlib import Path

import pytest


def _debug_socket_path() -> Path | None:
    xdg = os.environ.get("XDG_RUNTIME_DIR")
    if not xdg:
        return None
    return Path(xdg) / "resonite-io-debug" / "host-agent.sock"


@pytest.fixture(autouse=True)
def require_host_agent() -> None:
    socket = _debug_socket_path()
    if socket is None or not socket.exists():
        pytest.skip(
            "host-agent is not running on host (expected socket at "
            f"{socket}). Start it with `just host-agent` on the host."
        )
