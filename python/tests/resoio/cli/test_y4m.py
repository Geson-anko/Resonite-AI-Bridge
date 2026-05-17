from io import BytesIO

import numpy as np
import pytest

from resoio.cli import y4m


def test_write_header_420():
    buf = BytesIO()
    y4m.write_header(buf, 64, 48, 30, 1, "420")
    assert buf.getvalue() == b"YUV4MPEG2 W64 H48 F30:1 Ip A1:1 C420\n"


def test_write_header_444():
    buf = BytesIO()
    y4m.write_header(buf, 64, 48, 30, 1, "444")
    assert buf.getvalue() == b"YUV4MPEG2 W64 H48 F30:1 Ip A1:1 C444\n"


def test_write_frame_lengths_420():
    buf = BytesIO()
    rgba = np.zeros((48, 64, 4), dtype=np.uint8)
    y4m.write_frame(buf, rgba, "420")
    # b"FRAME\n" (6) + Y(64*48=3072) + U(32*24=768) + V(768) = 4614
    assert len(buf.getvalue()) == 6 + 3072 + 768 + 768
    assert buf.getvalue().startswith(b"FRAME\n")


def test_write_frame_lengths_444():
    buf = BytesIO()
    rgba = np.zeros((48, 64, 4), dtype=np.uint8)
    y4m.write_frame(buf, rgba, "444")
    # b"FRAME\n" (6) + Y(3072) + U(3072) + V(3072) = 9222
    assert len(buf.getvalue()) == 6 + 3 * 3072
    assert buf.getvalue().startswith(b"FRAME\n")


def test_rgba_to_yuv420_solid_white():
    rgba = np.full((4, 4, 4), 255, dtype=np.uint8)
    y, u, v = y4m.rgba_to_yuv420(rgba)
    assert y.shape == (4, 4)
    assert u.shape == (2, 2)
    assert v.shape == (2, 2)
    # BT.601 fullrange: pure white -> Y=255, U=V=128.
    assert int(y.min()) == 255
    assert int(y.max()) == 255
    assert abs(int(u.mean()) - 128) <= 1
    assert abs(int(v.mean()) - 128) <= 1
    # fps_to_fraction sanity (folded into y4m test to keep file count tight).
    assert y4m.fps_to_fraction(30.0) == (30, 1)
    num, den = y4m.fps_to_fraction(29.97)
    # 29.97 is exactly 2997/100 as a float; limit_denominator(1000) keeps it
    # at that exact ratio rather than collapsing to the NTSC 30000/1001 ideal.
    assert num / den == pytest.approx(29.97, abs=1e-3)
    assert den >= 1


def test_rgba_to_yuv444_shape_and_solid_red():
    rgba = np.zeros((48, 64, 4), dtype=np.uint8)
    rgba[..., 0] = 255  # pure red, alpha intentionally ignored.
    rgba[..., 3] = 255
    y, u, v = y4m.rgba_to_yuv444(rgba)
    assert y.shape == (48, 64)
    assert u.shape == (48, 64)
    assert v.shape == (48, 64)
    # BT.601 fullrange known values for pure red:
    # Y = 0.299 * 255 ~ 76, U = -0.168736*255 + 128 ~ 85,
    # V = 0.5*255 + 128 = 255 (clipped).
    assert abs(int(y[0, 0]) - 76) <= 2
    assert abs(int(u[0, 0]) - 85) <= 2
    assert abs(int(v[0, 0]) - 255) <= 2
