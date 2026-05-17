"""Client for the Resonite IO ``Session`` gRPC service over a UDS."""

from __future__ import annotations

import logging
from types import TracebackType
from typing import Self

from grpclib.client import Channel

from resoio._generated.resonite_io.v1 import PingRequest, PingResponse, SessionStub
from resoio._socket import (
    AmbiguousSocketError,
    SocketNotFoundError,
    resolve_socket_path,
)

# Re-exported for backwards compatibility: the exception types historically
# lived in this module and are documented in ``SessionClient`` docstring.
__all__ = [
    "AmbiguousSocketError",
    "SessionClient",
    "SocketNotFoundError",
]

_logger = logging.getLogger("resoio.session")


class SessionClient:
    """Async client for the Resonite IO ``Session`` service over a UDS.

    Use as an async context manager so the gRPC channel is closed
    deterministically. With ``socket_path=None`` the path is resolved on
    ``__aenter__`` via ``RESONITE_IO_SOCKET`` →
    ``RESONITE_IO_SOCKET_DIR`` → ``~/.resonite-io/``; resolution may
    raise :class:`SocketNotFoundError` or :class:`AmbiguousSocketError`.
    """

    def __init__(self, socket_path: str | None = None) -> None:
        # Defer resolution to __aenter__ so env vars patched between
        # construction and connection are honoured, and so resolution
        # errors surface at the connect site.
        self._explicit_path: str | None = socket_path
        self._channel: Channel | None = None
        self._stub: SessionStub | None = None
        self._resolved_path: str | None = None

    @property
    def socket_path(self) -> str | None:
        """Resolved UDS path, or ``None`` before ``__aenter__``."""
        return self._resolved_path

    async def __aenter__(self) -> Self:
        path = self._explicit_path or resolve_socket_path()
        _logger.debug("Opening Session channel on UDS path: %s", path)
        channel = Channel(path=path)
        self._channel = channel
        self._stub = SessionStub(channel)
        self._resolved_path = path
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        tb: TracebackType | None,
    ) -> None:
        channel = self._channel
        # Reset state before close() so a raising close still leaves the
        # client in a clean "not connected" state for retry / re-enter.
        self._channel = None
        self._stub = None
        self._resolved_path = None
        if channel is not None:
            channel.close()

    async def ping(self, message: str) -> PingResponse:
        stub = self._stub
        if stub is None:
            raise RuntimeError(
                "SessionClient is not connected. Use `async with SessionClient(): ...`."
            )
        return await stub.ping(PingRequest(message=message))
