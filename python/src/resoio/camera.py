"""Client for the Resonite IO ``Camera`` gRPC streaming service.

The server emits raw BGRA8 frames with row 0 at the image top (fixed
ResoniteIO API). The Python client wraps each frame in a :class:`Frame`
POCO whose ``pixels`` field is an ``(H, W, 4)`` ``numpy.uint8`` array,
sharing memory with the underlying protobuf bytes (zero-copy view).
"""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from dataclasses import dataclass
from types import TracebackType
from typing import Self

import numpy as np
from grpclib.client import Channel
from numpy.typing import NDArray

from resoio._generated.resonite_io.v1 import CameraStreamRequest, CameraStub
from resoio._socket import resolve_socket_path

__all__ = [
    "CameraClient",
    "Frame",
]

_logger = logging.getLogger("resoio.camera")


@dataclass(frozen=True, slots=True)
class Frame:
    """One decoded camera frame.

    ``pixels`` is a view over the protobuf payload bytes (read-only by
    default — call ``.copy()`` if you need a writable array). The byte
    order is BGRA and row 0 is the image top.
    """

    pixels: NDArray[np.uint8]
    width: int
    height: int
    unix_nanos: int
    frame_id: int


class CameraClient:
    """Async client for the Resonite IO ``Camera`` service over a UDS.

    Use as an async context manager so the gRPC channel is closed
    deterministically. ``socket_path`` resolution mirrors
    :class:`resoio.SessionClient`: ``RESONITE_IO_SOCKET`` →
    ``RESONITE_IO_SOCKET_DIR`` → ``~/.resonite-io/``.
    """

    def __init__(self, socket_path: str | None = None) -> None:
        self._explicit_path: str | None = socket_path
        self._channel: Channel | None = None
        self._stub: CameraStub | None = None
        self._resolved_path: str | None = None

    @property
    def socket_path(self) -> str | None:
        """Resolved UDS path, or ``None`` before ``__aenter__``."""
        return self._resolved_path

    async def __aenter__(self) -> Self:
        path = self._explicit_path or resolve_socket_path()
        _logger.debug("Opening Camera channel on UDS path: %s", path)
        channel = Channel(path=path)
        self._channel = channel
        self._stub = CameraStub(channel)
        self._resolved_path = path
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        tb: TracebackType | None,
    ) -> None:
        channel = self._channel
        self._channel = None
        self._stub = None
        self._resolved_path = None
        if channel is not None:
            channel.close()

    async def stream(
        self,
        width: int = 0,
        height: int = 0,
        fps_limit: float = 0.0,
    ) -> AsyncIterator[Frame]:
        """Stream camera frames from the server.

        ``width`` / ``height`` of 0 ask the server for its default
        resolution. ``fps_limit`` of 0 means uncapped (best-effort
        native fps). Large resolutions (e.g. 4096x4096 = 64 MB per
        frame) are not capped client-side — the caller is responsible
        for OOM management.
        """
        stub = self._stub
        if stub is None:
            raise RuntimeError(
                "CameraClient is not connected. Use `async with CameraClient(): ...`."
            )
        request = CameraStreamRequest(
            width=width,
            height=height,
            fps_limit=fps_limit,
        )
        async for raw in stub.stream_frames(request):
            pixels = np.frombuffer(raw.pixels, dtype=np.uint8).reshape(
                raw.height, raw.width, 4
            )
            yield Frame(
                pixels=pixels,
                width=raw.width,
                height=raw.height,
                unix_nanos=raw.unix_nanos,
                frame_id=raw.frame_id,
            )
