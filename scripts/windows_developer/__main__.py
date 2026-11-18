"""Typed commands for the Windows Developer workspace."""

import argparse
import json
import sys

from .scan import PROFILES, scan
from .install import install, installation_plan


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("scan", "plan-install", "install"))
    parser.add_argument("--profile", choices=PROFILES, default="optimized-static")
    parser.add_argument("--sources", required=True)
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--approve-plan")
    args = parser.parse_args(argv)
    try:
        def refresh():
            return scan(args.profile, args.sources, args.workspace)
        report = refresh()
        if args.action != "scan":
            plan = installation_plan(report)
            if args.action == "install":
                return install(plan, args.approve_plan, args.workspace, refresh=refresh)
            report = plan
    except (OSError, ValueError) as error:
        print(json.dumps({"status": "BLOCKED", "reason": str(error) if isinstance(error, ValueError)
                          else "Unreadable local inputs"}), file=sys.stderr)
        return 2
    print(json.dumps(report, allow_nan=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
