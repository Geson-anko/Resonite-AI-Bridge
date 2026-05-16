"""Client for the Resonite IO ``Session`` gRPC service over a UDS."""

from __future__ import annotations

import glob
import logging
import os
from pathlib import Path
from types import TracebackType
from typing import Self

from grpclib.client import Channel

from resoio._generated.resonite_io.v1 import PingRequest, PingResponse, SessionStub

__all__ = [
    "AmbiguousSocketError",
    "SessionClient",
    "SocketNotFoundError",
]

_logger = logging.getLogger("resoio.session")

_SOCKET_GLOB = "resonite-*.sock"
_DEFAULT_SOCKET_DIR_NAME = ".resonite-io"


class SocketNotFoundError(RuntimeError):
    """No ``resonite-*.sock`` matched the configured search directory."""


class AmbiguousSocketError(RuntimeError):
    """Multiple candidate sockets found; set ``RESONITE_IO_SOCKET`` to pick
    one."""


def _resolve_socket_path() -> str:
    # Empty env-var values fall through to the next step so a stray ``FOO=`` in
    # shell config does not produce a bogus empty path. The ``~/.resonite-io/``
    # fallback mirrors the C# Mod default so a zero-arg client just works under
    # the same effective user (including across the pressure-vessel sandbox).
    explicit = os.environ.get("RESONITE_IO_SOCKET")
    if explicit:
        return explicit

    search_dir = os.environ.get("RESONITE_IO_SOCKET_DIR")
    if search_dir:
        return _pick_single_socket(search_dir)

    return _pick_single_socket(str(Path.home() / _DEFAULT_SOCKET_DIR_NAME))


def _pick_single_socket(directory: str) -> str:
    pattern = os.path.join(directory, _SOCKET_GLOB)
    candidates = sorted(glob.glob(pattern))
    if not candidates:
        raise SocketNotFoundError(
            f"No Resonite IO socket matched {pattern!r}. "
            "Is the mod running and bound to a UDS?"
        )
    if len(candidates) > 1:
        joined = ", ".join(candidates)
        raise AmbiguousSocketError(
            f"Multiple Resonite IO sockets matched {pattern!r}: {joined}. "
            "Set RESONITE_IO_SOCKET to disambiguate."
        )
    return candidates[0]


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
        path = self._explicit_path or _resolve_socket_path()
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
