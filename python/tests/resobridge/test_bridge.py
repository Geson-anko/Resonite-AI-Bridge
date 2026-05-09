"""Skeleton-level smoke tests for resobridge.

These exist mainly to confirm that the package builds, installs, and resolves
its own version through ``importlib.metadata``. Behavioral tests for the
Bridge client itself land in Step 2 once the gRPC plumbing is wired up.
"""

import resobridge
from resobridge import bridge


def test_can_import_bridge():
    assert bridge is not None


def test_version_is_a_string():
    assert isinstance(resobridge.__version__, str)
    assert resobridge.__version__
