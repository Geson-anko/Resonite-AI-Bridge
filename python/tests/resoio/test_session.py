"""Skeleton-level smoke tests for resoio.

These exist mainly to confirm that the package builds, installs, and resolves
its own version through ``importlib.metadata``. Behavioral tests for the
Session client itself land in Step 2 once the gRPC plumbing is wired up.
"""

import resoio
from resoio import session


def test_can_import_session():
    assert session is not None


def test_version_is_a_string():
    assert isinstance(resoio.__version__, str)
    assert resoio.__version__
