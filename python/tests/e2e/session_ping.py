"""End-to-end smoke for Session.Ping against a live Resonite instance.

Run from inside the dev container:

  just e2e-test session_ping     # run only this file
  just e2e-test                  # run every e2e file

The ``resonite_session`` fixture (see ``conftest.py``) owns the Resonite
lifecycle and socket plumbing, so the test body below stays focused on
the actual call + assertions.
"""

from __future__ import annotations

import asyncio
from pathlib import Path

from resoio.session import SessionClient
from tests.helpers import mark_e2e


class TestSessionPing:
    """Live-Resonite end-to-end smoke for ``Session.Ping``."""

    @mark_e2e
    def test_smoke(self, resonite_session: Path) -> None:
        async def call() -> None:
            async with SessionClient() as client:
                response = await client.ping("e2e-smoke")
            assert response.message == "e2e-smoke"
            assert response.server_unix_nanos > 0

        asyncio.run(call())
