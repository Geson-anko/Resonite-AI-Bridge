from __future__ import annotations

import asyncio
from pathlib import Path

from resoio.session import SessionClient
from tests.helpers import mark_e2e


class TestSessionPing:
    @mark_e2e
    def test_smoke(self, resonite_session: Path) -> None:
        async def call() -> None:
            async with SessionClient() as client:
                response = await client.ping("e2e-smoke")
            assert response.message == "e2e-smoke"
            assert response.server_unix_nanos > 0

        asyncio.run(call())
