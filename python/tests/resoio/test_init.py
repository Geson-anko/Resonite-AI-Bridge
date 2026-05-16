"""Tests for ``resoio/__init__.py``.

Currently only validates ``__version__`` exposure via ``importlib.metadata``.
"""

import resoio


def test_version_is_a_string():
    assert isinstance(resoio.__version__, str)
    assert resoio.__version__
