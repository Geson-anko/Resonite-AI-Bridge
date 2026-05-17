"""Shared UDS socket discovery for Resonite IO gRPC clients.

Resolution order is unified across all modality clients so a
zero-argument client just works under the same effective user as the
running mod (including across the pressure-vessel sandbox):

1. ``RESONITE_IO_SOCKET`` (explicit absolute path)
2. ``RESONITE_IO_SOCKET_DIR`` (directory containing ``resonite-*.sock``)
3. ``~/.resonite-io/`` (matches the C# Mod default)
"""

import glob
import os
from pathlib import Path

__all__ = [
    "AmbiguousSocketError",
    "SocketNotFoundError",
    "resolve_socket_path",
]

_SOCKET_GLOB = "resonite-*.sock"
_DEFAULT_SOCKET_DIR_NAME = ".resonite-io"


class SocketNotFoundError(RuntimeError):
    """No ``resonite-*.sock`` matched the configured search directory."""


class AmbiguousSocketError(RuntimeError):
    """Multiple candidate sockets found; set ``RESONITE_IO_SOCKET`` to pick
    one."""


def resolve_socket_path() -> str:
    """Resolve the UDS path for a Resonite IO gRPC client.

    Empty env-var values fall through to the next step so a stray
    ``FOO=`` in shell config does not produce a bogus empty path.
    """
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
