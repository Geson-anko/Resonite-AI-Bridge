"""resoio: Python client for Resonite IO.

The package version is single-sourced from ``pyproject.toml`` via
``importlib.metadata`` so it stays in sync with the installed distribution.
"""

from importlib.metadata import version as _version

__version__: str = _version("resoio")

__all__ = ["__version__"]
