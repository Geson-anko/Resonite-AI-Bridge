"""Y4M (YUV4MPEG2) writer used by ``resoio capture``.

This module produces a self-describing Y4M stream from RGBA8 frames so the
output can be piped directly into ``ffmpeg -i -`` without further metadata.
Color conversion is BT.601 full-range.

Two chroma layouts are supported:

* ``"420"`` — 4:2:0 subsampling. Requires both ``H`` and ``W`` to be even;
  callers are expected to crop the input beforehand.
* ``"444"`` — 4:4:4 (no subsampling). Accepts any resolution.
"""

from __future__ import annotations

from fractions import Fraction
from typing import BinaryIO, Literal

import numpy as np
from numpy.typing import NDArray

__all__ = [
    "ChromaSubsampling",
    "fps_to_fraction",
    "rgba_to_yuv420",
    "rgba_to_yuv444",
    "write_frame",
    "write_header",
]

ChromaSubsampling = Literal["420", "444"]


def fps_to_fraction(fps: float) -> tuple[int, int]:
    """Return ``(numerator, denominator)`` for the Y4M ``F`` header field.

    The fraction is bounded to a denominator of at most ``1000``; integer
    rates like ``30.0`` collapse to ``(30, 1)``. Y4M only requires a valid
    rational, so the result may differ from broadcasting conventions: for
    example ``29.97`` yields ``(2997, 100)`` rather than the NTSC ideal
    ``(30000, 1001)`` (denominator 1001 is outside the cap).
    """
    frac = Fraction(fps).limit_denominator(1000)
    return frac.numerator, frac.denominator


def write_header(
    out: BinaryIO,
    width: int,
    height: int,
    fps_num: int,
    fps_den: int,
    chroma: ChromaSubsampling,
) -> None:
    """Write a Y4M stream header to ``out``.

    Aspect ratio is fixed to ``1:1`` (square pixels) and interlacing to
    ``Ip`` (progressive). ``chroma`` is emitted verbatim as ``C420`` or
    ``C444``.
    """
    header = (
        f"YUV4MPEG2 W{width} H{height} F{fps_num}:{fps_den} Ip A1:1 C{chroma}\n"
    ).encode("ascii")
    out.write(header)


def write_frame(
    out: BinaryIO,
    rgba: NDArray[np.uint8],
    chroma: ChromaSubsampling,
) -> None:
    """Write one Y4M frame (``FRAME`` marker plus Y/U/V planes).

    ``rgba`` must be a contiguous ``(H, W, 4)`` ``uint8`` array. For chroma
    ``"420"`` the caller must already have cropped ``rgba`` to even
    dimensions; this function does not crop on its behalf.
    """
    if chroma == "420":
        y, u, v = rgba_to_yuv420(rgba)
    else:
        y, u, v = rgba_to_yuv444(rgba)
    out.write(b"FRAME\n")
    out.write(y.tobytes())
    out.write(u.tobytes())
    out.write(v.tobytes())


def _rgba_to_yuv_planes(
    rgba: NDArray[np.uint8],
) -> tuple[NDArray[np.float64], NDArray[np.float64], NDArray[np.float64]]:
    """Apply BT.601 full-range matrix; return float planes pre-clip."""
    r = rgba[..., 0].astype(np.float64)
    g = rgba[..., 1].astype(np.float64)
    b = rgba[..., 2].astype(np.float64)
    y = 0.299 * r + 0.587 * g + 0.114 * b
    u = -0.168736 * r - 0.331264 * g + 0.5 * b + 128.0
    v = 0.5 * r - 0.418688 * g - 0.081312 * b + 128.0
    return y, u, v


def rgba_to_yuv444(
    rgba: NDArray[np.uint8],
) -> tuple[NDArray[np.uint8], NDArray[np.uint8], NDArray[np.uint8]]:
    """Convert RGBA8 ``(H, W, 4)`` to BT.601 full-range YUV 4:4:4.

    Returns three ``(H, W)`` ``uint8`` planes ``(Y, U, V)``.
    """
    y, u, v = _rgba_to_yuv_planes(rgba)
    y8 = np.clip(y, 0.0, 255.0).astype(np.uint8)
    u8 = np.clip(u, 0.0, 255.0).astype(np.uint8)
    v8 = np.clip(v, 0.0, 255.0).astype(np.uint8)
    return y8, u8, v8


def rgba_to_yuv420(
    rgba: NDArray[np.uint8],
) -> tuple[NDArray[np.uint8], NDArray[np.uint8], NDArray[np.uint8]]:
    """Convert RGBA8 ``(H, W, 4)`` to BT.601 full-range YUV 4:2:0.

    Returns ``(Y(H, W), U(H/2, W/2), V(H/2, W/2))`` as ``uint8``. Raises
    :class:`ValueError` if either spatial dimension is odd, because 4:2:0
    requires an even number of rows and columns to subsample.
    """
    h, w = rgba.shape[0], rgba.shape[1]
    if h % 2 != 0 or w % 2 != 0:
        raise ValueError(f"rgba_to_yuv420 requires even dimensions, got {h}x{w}")
    y, u_full, v_full = _rgba_to_yuv_planes(rgba)
    # 2x2 box average for chroma. Reshape splits each axis into
    # (n_blocks, block_size); mean over the block axes collapses to the
    # subsampled plane.
    u_sub = u_full.reshape(h // 2, 2, w // 2, 2).mean(axis=(1, 3))
    v_sub = v_full.reshape(h // 2, 2, w // 2, 2).mean(axis=(1, 3))
    y8 = np.clip(y, 0.0, 255.0).astype(np.uint8)
    u8 = np.clip(u_sub, 0.0, 255.0).astype(np.uint8)
    v8 = np.clip(v_sub, 0.0, 255.0).astype(np.uint8)
    return y8, u8, v8
