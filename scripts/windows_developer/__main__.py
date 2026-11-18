"""Typed commands for the Windows Developer workspace."""

import argparse
import json
import sys

from .scan import PROFILES, scan
from .install import install, installation_plan
from .build import build, list_builds, smoke


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("scan", "plan-install", "install", "build", "list", "smoke"))
    parser.add_argument("--profile", choices=PROFILES, default="optimized-static")
    parser.add_argument("--sources", required=True)
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--approve-plan")
    parser.add_argument("--build-id")
    parser.add_argument("--wait-for-job", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.wait_for_job and sys.stdin.readline().strip() != "owned-windows-job":
            raise ValueError("The developer child did not receive its ownership gate")
        if args.action == "build":
            return build(args.profile, args.sources, args.workspace)
        if args.action == "smoke":
            return smoke(args.workspace, args.build_id)
        def refresh():
            return scan(args.profile, args.sources, args.workspace)
        report = list_builds(args.workspace) if args.action == "list" else refresh()
        if args.action in ("plan-install", "install"):
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
