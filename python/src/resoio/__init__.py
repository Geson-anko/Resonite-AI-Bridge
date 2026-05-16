"""resoio: Python client for Resonite IO.

The package version is single-sourced from ``pyproject.toml`` via
``importlib.metadata`` so it stays in sync with the installed distribution.
"""

from importlib.metadata import version as _version

from resoio.session import (
    AmbiguousSocketError,
    SessionClient,
    SocketNotFoundError,
)

__version__: str = _version("resoio")

__all__ = [
    "AmbiguousSocketError",
    "SessionClient",
    "SocketNotFoundError",
    "__version__",
]
