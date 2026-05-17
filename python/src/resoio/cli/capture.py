"""``resoio capture`` subcommand: stream Camera frames as Y4M.

Heavy imports (numpy, the Camera client, the Y4M writer) are deferred to
:func:`_run` so ``resoio capture --help`` and shell completion stay fast.
"""

from __future__ import annotations

import argparse
import asyncio
import sys
from typing import BinaryIO


def _fps_arg(raw: str) -> float:
    """Parse ``--fps``; reject zero/negative so the Y4M header is sane."""
    value = float(raw)
    if value <= 0.0:
        raise argparse.ArgumentTypeError(f"--fps must be positive, got {value}")
    return value


def register(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],  # pyright: ignore[reportPrivateUsage]
    common: argparse.ArgumentParser,
) -> None:
    """Register the ``capture`` subparser on the top-level parser.

    ``common`` carries flags shared by every subcommand (e.g.
    ``-s/--socket``) and is attached via ``parents=[common]``.
    """
    parser = subparsers.add_parser(
        "capture",
        parents=[common],
        help="Stream Camera frames and emit a Y4M video to stdout or a file.",
        description=(
            "Open a Camera stream over the Resonite IO UDS and emit a Y4M "
            "(YUV4MPEG2) video. Pipe into `ffmpeg -i -` for transcoding, or "
            "use `-o FILE` to write a `.y4m` file directly."
        ),
    )
    parser.add_argument(
        "-o",
        "--output",
        default="-",
        help='Output file path; "-" writes to stdout (default: "-").',
    )
    parser.add_argument(
        "--width",
        type=int,
        default=0,
        help="Requested frame width (0 means server default).",
    )
    parser.add_argument(
        "--height",
        type=int,
        default=0,
        help="Requested frame height (0 means server default).",
    )
    parser.add_argument(
        "--fps",
        type=_fps_arg,
        default=30.0,
        help="Y4M frame rate header and server fps_limit request (default: 30.0).",
    )
    parser.add_argument(
        "--chroma",
        choices=["420", "444"],
        default="444",
        help=(
            "Chroma subsampling. 444 preserves any resolution; 420 is more "
            "compact but requires even dimensions (odd inputs are cropped)."
        ),
    )
    parser.add_argument(
        "--duration",
        type=float,
        default=None,
        help="Stop after this many seconds (default: run until Ctrl-C).",
    )
    parser.add_argument(
        "-v",
        "--verbose",
        action="store_true",
        help="Print per-frame stats to stderr.",
    )
    parser.set_defaults(func=_run)


def _open_output(path: str) -> tuple[BinaryIO, bool]:
    """Open the output sink.

    Returns ``(stream, should_close)``: stdout must not be closed by us,
    but a file we opened ourselves must be.
    """
    if path == "-":
        return sys.stdout.buffer, False
    return open(path, "wb"), True


async def _capture_loop(args: argparse.Namespace, out: BinaryIO) -> int:
    # Deferred imports: keep `resoio --help` and tab-completion snappy.
    import numpy as np
    from numpy.typing import NDArray

    from resoio.camera import CameraClient
    from resoio.cli import y4m

    fps_num, fps_den = y4m.fps_to_fraction(args.fps)
    chroma: y4m.ChromaSubsampling = args.chroma
    header_written = False
    frame_count = 0

    async with CameraClient(args.socket) as client:
        async for frame in client.stream(
            width=args.width,
            height=args.height,
            fps_limit=args.fps,
        ):
            pixels: NDArray[np.uint8] = frame.pixels
            h, w = frame.height, frame.width
            if chroma == "420":
                h2, w2 = h & ~1, w & ~1
                if (h2, w2) != (h, w):
                    if args.verbose:
                        print(
                            f"frame {frame.frame_id} cropped: {w}x{h} -> {w2}x{h2}",
                            file=sys.stderr,
                        )
                    pixels = pixels[:h2, :w2, :]
                    h, w = h2, w2
            if not header_written:
                y4m.write_header(out, w, h, fps_num, fps_den, chroma)
                header_written = True
            try:
                y4m.write_frame(out, pixels, chroma)
                out.flush()
            except BrokenPipeError:
                # Downstream closed stdout (e.g. `resoio capture | head`).
                # That is a clean exit, not a failure.
                return 0
            frame_count += 1
            if args.verbose:
                print(
                    f"frame {frame.frame_id} {w}x{h} "
                    f"unix_nanos={frame.unix_nanos} total={frame_count}",
                    file=sys.stderr,
                )
    return 0


async def _run(args: argparse.Namespace) -> int:
    out, should_close = _open_output(args.output)
    try:
        if args.duration is None:
            return await _capture_loop(args, out)
        # wait_for cancels the coroutine on timeout, which unwinds the
        # `async with CameraClient(...)` inside _capture_loop and closes
        # the gRPC channel cleanly. Treat the resulting TimeoutError as
        # a normal end-of-capture, not an error.
        try:
            return await asyncio.wait_for(
                _capture_loop(args, out), timeout=args.duration
            )
        except TimeoutError:
            return 0
    except BrokenPipeError:
        return 0
    finally:
        try:
            out.flush()
        except BrokenPipeError:
            pass
        if should_close:
            out.close()
