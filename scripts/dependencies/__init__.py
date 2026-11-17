"""Read-only dependency diagnostics; no installers or source downloads."""

import sys

# The CLI and public API must not create bytecode files during discovery.
sys.dont_write_bytecode = True

from .checker import ReadOnlyProbe, collect_report, dependency_check

__all__ = ["ReadOnlyProbe", "collect_report", "dependency_check"]
