"""Print a dependency/environment report without modifying the system."""

import argparse
import json
import sys

sys.dont_write_bytecode = True

from .checker import collect_report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", choices=("developer",), default="developer")
    args = parser.parse_args(argv)
    try:
        report = collect_report(profile=args.profile)
    except (OSError, ValueError, ImportError) as exc:
        print(json.dumps({"schemaVersion": 1, "status": "BLOCKED",
                          "reason": str(exc)}), file=sys.stderr)
        return 2
    print(json.dumps(report, indent=2, sort_keys=True, allow_nan=False))
    return 0 if report["summary"]["readyForScope"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
