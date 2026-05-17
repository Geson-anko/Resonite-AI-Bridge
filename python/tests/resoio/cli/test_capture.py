import asyncio
import time
from collections.abc import AsyncIterator
from pathlib import Path

import pytest
from grpclib.server import Server

from resoio._generated.resonite_io.v1 import (
    CameraBase,
    CameraFrame,
    CameraFrameFormat,
    CameraStreamRequest,
)
from resoio.cli import _amain, _build_parser


def _make_fixed_camera(width: int, height: int, frame_count: int) -> type[CameraBase]:
    """Build a Camera fake that yields exactly ``frame_count`` frames.

    The fake intentionally ignores ``request.width``/``request.height`` so
    tests can prove the CLI threads the *server-reported* dimensions into
    the Y4M header (e.g. crop scenarios).
    """

    class _FixedCamera(CameraBase):
        async def stream_frames(
            self, message: CameraStreamRequest
        ) -> AsyncIterator[CameraFrame]:
            for i in range(frame_count):
                pixels = bytes([i & 0xFF]) * (width * height * 4)
                yield CameraFrame(
                    width=width,
                    height=height,
                    format=CameraFrameFormat.RGBA8,
                    unix_nanos=time.time_ns(),
                    frame_id=i,
                    pixels=pixels,
                )

    return _FixedCamera


def _make_infinite_camera(width: int, height: int) -> type[CameraBase]:
    """Build a Camera fake that yields forever, simulating a live stream."""

    class _InfiniteCamera(CameraBase):
        async def stream_frames(
            self, message: CameraStreamRequest
        ) -> AsyncIterator[CameraFrame]:
            i = 0
            while True:
                pixels = bytes([i & 0xFF]) * (width * height * 4)
                yield CameraFrame(
                    width=width,
                    height=height,
                    format=CameraFrameFormat.RGBA8,
                    unix_nanos=time.time_ns(),
                    frame_id=i,
                    pixels=pixels,
                )
                i += 1
                await asyncio.sleep(0.01)

    return _InfiniteCamera


async def test_capture_to_file_yuv444(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    socket_path = tmp_path / "rio.sock"
    out_path = tmp_path / "out.y4m"
    camera = _make_fixed_camera(width=16, height=8, frame_count=3)
    server = Server([camera()])
    await server.start(path=str(socket_path))
    try:
        monkeypatch.setenv("RESONITE_IO_SOCKET", str(socket_path))
        args = _build_parser().parse_args(
            [
                "capture",
                "-o",
                str(out_path),
                "--chroma",
                "444",
                "--duration",
                "1",
            ]
        )
        rc = await _amain(args)
        assert rc == 0
        data = out_path.read_bytes()
        assert data.startswith(b"YUV4MPEG2 W16 H8 F30:1 Ip A1:1 C444\n")
        # First chunk is the header; subsequent chunks are FRAME blocks.
        chunks = data.split(b"FRAME\n")
        assert len(chunks) == 1 + 3
        # 4:4:4 payload per frame: 16 * 8 * 3 = 384.
        for payload in chunks[1:]:
            assert len(payload) == 16 * 8 * 3
    finally:
        server.close()
        await server.wait_closed()


async def test_capture_to_file_yuv420(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    socket_path = tmp_path / "rio.sock"
    out_path = tmp_path / "out.y4m"
    camera = _make_fixed_camera(width=16, height=8, frame_count=3)
    server = Server([camera()])
    await server.start(path=str(socket_path))
    try:
        monkeypatch.setenv("RESONITE_IO_SOCKET", str(socket_path))
        args = _build_parser().parse_args(
            [
                "capture",
                "-o",
                str(out_path),
                "--chroma",
                "420",
                "--duration",
                "1",
            ]
        )
        rc = await _amain(args)
        assert rc == 0
        data = out_path.read_bytes()
        assert data.startswith(b"YUV4MPEG2 W16 H8 F30:1 Ip A1:1 C420\n")
        chunks = data.split(b"FRAME\n")
        assert len(chunks) == 1 + 3
        # 4:2:0: Y = 16*8 = 128, U = V = 8*4 = 32. Total per frame = 192.
        for payload in chunks[1:]:
            assert len(payload) == 128 + 32 + 32
    finally:
        server.close()
        await server.wait_closed()


async def test_capture_crops_odd_dimensions_for_420(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    socket_path = tmp_path / "rio.sock"
    out_path = tmp_path / "out.y4m"
    camera = _make_fixed_camera(width=17, height=9, frame_count=2)
    server = Server([camera()])
    await server.start(path=str(socket_path))
    try:
        monkeypatch.setenv("RESONITE_IO_SOCKET", str(socket_path))
        args = _build_parser().parse_args(
            [
                "capture",
                "-o",
                str(out_path),
                "--chroma",
                "420",
                "--duration",
                "1",
            ]
        )
        rc = await _amain(args)
        assert rc == 0
        data = out_path.read_bytes()
        # Cropped to even dimensions before the header is written.
        assert data.startswith(b"YUV4MPEG2 W16 H8 F30:1 Ip A1:1 C420\n")
    finally:
        server.close()
        await server.wait_closed()


async def test_capture_no_crop_for_444_with_odd_dimensions(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    socket_path = tmp_path / "rio.sock"
    out_path = tmp_path / "out.y4m"
    camera = _make_fixed_camera(width=17, height=9, frame_count=1)
    server = Server([camera()])
    await server.start(path=str(socket_path))
    try:
        monkeypatch.setenv("RESONITE_IO_SOCKET", str(socket_path))
        args = _build_parser().parse_args(
            [
                "capture",
                "-o",
                str(out_path),
                "--chroma",
                "444",
                "--duration",
                "1",
            ]
        )
        rc = await _amain(args)
        assert rc == 0
        data = out_path.read_bytes()
        assert data.startswith(b"YUV4MPEG2 W17 H9 F30:1 Ip A1:1 C444\n")
        # 4:4:4 payload at 17x9 = 459 bytes per frame.
        chunks = data.split(b"FRAME\n")
        assert len(chunks) == 1 + 1
        assert len(chunks[1]) == 17 * 9 * 3
    finally:
        server.close()
        await server.wait_closed()


async def test_capture_duration_stops_streaming(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    socket_path = tmp_path / "rio.sock"
    out_path = tmp_path / "out.y4m"
    camera = _make_infinite_camera(width=8, height=8)
    server = Server([camera()])
    await server.start(path=str(socket_path))
    try:
        monkeypatch.setenv("RESONITE_IO_SOCKET", str(socket_path))
        args = _build_parser().parse_args(
            [
                "capture",
                "-o",
                str(out_path),
                "--chroma",
                "444",
                "--duration",
                "0.15",
            ]
        )
        start = time.monotonic()
        rc = await _amain(args)
        elapsed = time.monotonic() - start
        assert rc == 0
        # Wall-clock guard: duration should bound runtime; allow generous
        # CI slack but ensure we don't hang waiting for an infinite stream.
        assert elapsed < 0.5
        data = out_path.read_bytes()
        assert data.startswith(b"YUV4MPEG2 W8 H8 F30:1 Ip A1:1 C444\n")
        chunks = data.split(b"FRAME\n")
        # At ~10ms per frame and 0.15s duration we expect a handful of
        # frames, but stay tolerant for slower runners.
        assert 1 <= len(chunks) - 1 <= 30
    finally:
        server.close()
        await server.wait_closed()
