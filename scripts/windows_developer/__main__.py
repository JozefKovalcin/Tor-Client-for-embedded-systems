"""Typed commands for the Windows Developer workspace."""

import argparse
import json
import sys

from .scan import PROFILES, scan


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("scan",))
    parser.add_argument("--profile", choices=PROFILES, default="optimized-static")
    parser.add_argument("--sources", required=True)
    parser.add_argument("--workspace", required=True)
    args = parser.parse_args(argv)
    try:
        report = scan(args.profile, args.sources, args.workspace)
    except (OSError, ValueError):
        print(json.dumps({"status": "BLOCKED", "reason": "Invalid settings or unreadable local inputs"}), file=sys.stderr)
        return 2
    print(json.dumps(report, allow_nan=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
