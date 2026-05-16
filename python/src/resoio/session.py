"""High-level client for the Resonite IO ``Session`` gRPC service.

The :class:`SessionClient` opens a :mod:`grpclib` channel over a Unix Domain
Socket and exposes typed wrappers around the RPCs defined in
``proto/resonite_io/v1/session.proto`` (currently only ``Ping``). Socket path
resolution mirrors the discovery contract documented in the project plan
(``RESONITE_IO_SOCKET`` / ``RESONITE_IO_SOCKET_DIR`` / ``$XDG_RUNTIME_DIR``).
"""

from __future__ import annotations

import glob
import logging
import os
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
_DEFAULT_SUBDIR = "resonite-io"


class SocketNotFoundError(RuntimeError):
    """No Resonite IO socket could be discovered.

    Raised when neither an explicit path nor any candidate
    ``resonite-*.sock`` matched the configured search directory. Usually
    means the mod is not running or the discovery env vars do not point
    at the directory it bound to.
    """


class AmbiguousSocketError(RuntimeError):
    """Multiple candidate sockets were found and no override resolved them.

    Raised when more than one ``resonite-*.sock`` exists in the search
    directory (e.g. a stale socket from a previous Resonite process plus
    the live one). Set ``RESONITE_IO_SOCKET`` to the desired path to
    disambiguate.
    """


def _resolve_socket_path() -> str:
    """Resolve the UDS path to connect to, honoring env-var overrides.

    Empty strings are treated as "unset" so a stray ``=`` in shell config
    falls through to the next discovery step rather than yielding a bogus
    empty path.
    """
    explicit = os.environ.get("RESONITE_IO_SOCKET")
    if explicit:
        return explicit

    search_dir = os.environ.get("RESONITE_IO_SOCKET_DIR")
    if search_dir:
        return _pick_single_socket(search_dir)

    runtime_dir = os.environ.get("XDG_RUNTIME_DIR")
    if not runtime_dir:
        raise SocketNotFoundError(
            "Could not resolve a Resonite IO socket: neither "
            "RESONITE_IO_SOCKET, RESONITE_IO_SOCKET_DIR, nor "
            "XDG_RUNTIME_DIR is set."
        )
    return _pick_single_socket(os.path.join(runtime_dir, _DEFAULT_SUBDIR))


def _pick_single_socket(directory: str) -> str:
    """Pick exactly one ``resonite-*.sock`` inside *directory*."""
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

    Use as an async context manager so the underlying gRPC channel is
    closed deterministically::

        async with SessionClient() as client:
            response = await client.ping("hello")

    When ``socket_path`` is omitted, the UDS path is resolved on
    ``__aenter__`` in this order: explicit ``RESONITE_IO_SOCKET`` env var,
    a single ``resonite-*.sock`` under ``RESONITE_IO_SOCKET_DIR``, then a
    single one under ``$XDG_RUNTIME_DIR/resonite-io/``. Resolution can
    raise :class:`SocketNotFoundError` or :class:`AmbiguousSocketError`.
    """

    def __init__(self, socket_path: str | None = None) -> None:
        # Defer socket resolution until __aenter__ so env vars can be patched
        # between construction and connection (and so errors surface there).
        self._explicit_path: str | None = socket_path
        self._channel: Channel | None = None
        self._stub: SessionStub | None = None
        self._resolved_path: str | None = None

    @property
    def socket_path(self) -> str | None:
        """The UDS path the client is connected to, or ``None`` before
        enter."""
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
        # Reset state first so a raising `close()` still leaves the client
        # in a clean "not connected" state for any retry / re-enter logic.
        self._channel = None
        self._stub = None
        self._resolved_path = None
        if channel is not None:
            channel.close()

    async def ping(self, message: str) -> PingResponse:
        """Send a ``Ping`` RPC and return the server's
        :class:`PingResponse`."""
        stub = self._stub
        if stub is None:
            raise RuntimeError(
                "SessionClient is not connected. Use `async with SessionClient(): ...`."
            )
        return await stub.ping(PingRequest(message=message))
