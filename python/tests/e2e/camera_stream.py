"""E2E: stream Camera frames from a live Resonite and record to MP4.

Captures ~10 seconds of frames at 640x480 @ 10 fps through ``CameraClient``
and writes them to ``e2e_artifacts/camera_<timestamp>/capture.mp4`` using
OpenCV. The first and last frame are also saved as PNG for quick visual
inspection without needing a video player.

Like every file under ``tests/e2e/`` the actual run requires the host-side
``just host-agent`` daemon plus a live Resonite client; without them the
``require_host_agent`` autouse fixture skips the test.
"""

from __future__ import annotations

import asyncio
import time
from datetime import datetime
from pathlib import Path

import cv2
import numpy as np
from numpy.typing import NDArray

from resoio.camera import CameraClient
from tests.helpers import mark_e2e

ARTIFACT_ROOT = Path(__file__).parent / "e2e_artifacts"

# 10 s × 10 fps = 100 frames nominal. Slack of 20 absorbs slow first-frame
# warm-up (camera component creation, first RenderToBitmap) and per-frame
# pacing jitter.
_CAPTURE_WIDTH = 640
_CAPTURE_HEIGHT = 480
_CAPTURE_FPS = 10.0
_CAPTURE_SECONDS = 10.0
_MIN_FRAMES = 80


class TestCameraStream:
    @mark_e2e
    def test_capture_to_mp4(self, resonite_session: Path) -> None:
        del resonite_session  # fixture only manages Resonite lifecycle

        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        out_dir = ARTIFACT_ROOT / f"camera_{timestamp}"
        out_dir.mkdir(parents=True, exist_ok=True)
        out_path = out_dir / "capture.mp4"
        first_png = out_dir / "frame_0000.png"
        last_png = out_dir / "frame_last.png"

        # ``mp4v`` is the most broadly available codec in the headless
        # opencv build shipped via pip; if the container OpenCV refuses it
        # the writer will silently fail to open and ``out_path`` will stay
        # zero-bytes (caught by the assertion below).
        fourcc = cv2.VideoWriter.fourcc(*"mp4v")
        writer = cv2.VideoWriter(
            str(out_path),
            fourcc,
            _CAPTURE_FPS,
            (_CAPTURE_WIDTH, _CAPTURE_HEIGHT),
        )

        async def capture() -> int:
            count = 0
            last_bgr: NDArray[np.uint8] | None = None
            deadline = time.monotonic() + _CAPTURE_SECONDS
            async with CameraClient() as cam:
                async for frame in cam.stream(
                    width=_CAPTURE_WIDTH,
                    height=_CAPTURE_HEIGHT,
                    fps_limit=_CAPTURE_FPS,
                ):
                    # ``frame.pixels`` is a read-only BGRA view over the
                    # protobuf bytes; cvtColor copies into a fresh BGR
                    # buffer that ``VideoWriter`` / ``imwrite`` accept.
                    bgr = cv2.cvtColor(frame.pixels, cv2.COLOR_BGRA2BGR)
                    writer.write(bgr)
                    if count == 0:
                        cv2.imwrite(str(first_png), bgr)
                    last_bgr = bgr
                    count += 1
                    if time.monotonic() >= deadline:
                        break
            if last_bgr is not None:
                cv2.imwrite(str(last_png), last_bgr)
            return count

        try:
            n = asyncio.run(capture())
        finally:
            writer.release()

        # Printed so CI logs surface the artifact location even on success.
        print(f"E2E artifact dir: {out_dir}")
        print(f"E2E MP4: {out_path}")

        assert out_path.exists(), f"MP4 not created at {out_path}"
        assert out_path.stat().st_size > 0, (
            f"MP4 at {out_path} is empty (codec missing?)"
        )
        assert n >= _MIN_FRAMES, (
            f"expected >= {_MIN_FRAMES} frames in "
            f"{_CAPTURE_SECONDS:.0f}s @ {_CAPTURE_FPS} fps, got {n}"
        )
