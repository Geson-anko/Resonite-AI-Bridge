"""Tests for ``resoio.session``.

Covers the Step 2 behaviors: in-process UDS round-trip against a real
``grpclib`` server, and the two error paths in socket-path resolution.
"""

import time
from pathlib import Path

import pytest
from grpclib.server import Server

from resoio._generated.resonite_io.v1 import (
    PingRequest,
    PingResponse,
    SessionBase,
)
from resoio.session import (
    AmbiguousSocketError,
    SessionClient,
    SocketNotFoundError,
)


class _EchoSession(SessionBase):
    """In-process echo implementation used to validate round-tripping."""

    async def ping(self, message: PingRequest) -> PingResponse:
        return PingResponse(
            message=message.message,
            server_unix_nanos=time.time_ns(),
        )


class TestSessionClient:
    """End-to-end behavior of ``SessionClient`` against a local UDS server."""

    async def test_round_trip_over_uds(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
    ):
        socket_path = tmp_path / "rio.sock"
        server = Server([_EchoSession()])
        await server.start(path=str(socket_path))
        try:
            monkeypatch.setenv("RESONITE_IO_SOCKET", str(socket_path))
            async with SessionClient() as client:
                assert client.socket_path == str(socket_path)
                resp = await client.ping("hi")
            assert resp.message == "hi"
            assert resp.server_unix_nanos > 0
        finally:
            server.close()
            await server.wait_closed()

    async def test_raises_when_socket_not_found(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
    ):
        monkeypatch.delenv("RESONITE_IO_SOCKET", raising=False)
        monkeypatch.setenv("RESONITE_IO_SOCKET_DIR", str(tmp_path))
        with pytest.raises(SocketNotFoundError):
            async with SessionClient():
                pass

    async def test_raises_when_socket_ambiguous(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
    ):
        (tmp_path / "resonite-1.sock").touch()
        (tmp_path / "resonite-2.sock").touch()
        monkeypatch.delenv("RESONITE_IO_SOCKET", raising=False)
        monkeypatch.setenv("RESONITE_IO_SOCKET_DIR", str(tmp_path))
        with pytest.raises(AmbiguousSocketError):
            async with SessionClient():
                pass
