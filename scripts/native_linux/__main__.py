"""Emit a native Linux build plan; this CLI contains no build operation."""
import argparse
import json
import sys

sys.dont_write_bytecode = True

from .preflight import preflight


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", required=True, help="absolute native Linux checkout path")
    parser.add_argument("--output-dir", required=True, help="new absolute native Linux output path; never created")
    parser.add_argument("--archive-cache", required=True, help="existing local cache; archives never downloaded")
    args = parser.parse_args(argv)
    try:
        report = preflight(args.source_dir, args.output_dir, args.archive_cache)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(json.dumps({"schemaVersion": 1, "kind": "native-linux-build-plan", "executionStatus": "NOT RUN",
                          "readyForBuildPlan": False, "buildRequest": None,
                          "error": type(exc).__name__ + ": " + str(exc)}, allow_nan=False))
        return 2
    print(json.dumps(report, indent=2, sort_keys=True, allow_nan=False))
    return 0 if report["readyForBuildPlan"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
