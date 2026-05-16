"""Cross-cutting helpers shared by unit and e2e tests.

`mark_*` constants expose pytest marker decorators as plain identifiers so
that test files can apply them without re-importing ``pytest`` everywhere.
"""

import pytest

mark_e2e = pytest.mark.e2e

__all__ = ["mark_e2e"]
