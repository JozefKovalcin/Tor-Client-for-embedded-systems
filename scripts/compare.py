#!/usr/bin/env python3
"""Compare validated runs. Exit 0 only for accepted candidates, 1 otherwise, 2 on invalid input."""
import argparse
import json
import math
from pathlib import Path
from common import ROOT, read_json, resolve_result, sha256, validate_result, write_json

def equivalent_environments(a, b):
    if a == b:
        return True
    left, right = dict(a), dict(b)
    old, new = left.pop("host_memory_bytes", None), right.pop("host_memory_bytes", None)
    # WSL's reported physical capacity can change by a few reserved pages.
    # Keep raw observations; tolerate at most 1 MiB on the HOST only. Guest
    # RAM, CPU, architecture, emulator, protocol and every other field stay exact.
    return (left == right and all(isinstance(x, int) and not isinstance(x, bool) and x > 0
                                 for x in (old, new)) and abs(old-new) <= 1024*1024)


def compare(baseline, candidate, policy, policy_sha256=None):
    reasons = validate_result(candidate, policy)
    result = {"status": "INCOMPLETE" if reasons else "REJECTED", "reasons": reasons, "differences": [],
              "functional_regressions": []}
    old = {t["id"]: t["status"] for t in baseline.get("tests", [])}
    new = {t["id"]: t["status"] for t in candidate.get("tests", [])}
    result["functional_regressions"] = [name for name, status in old.items()
                                          if status == "PASS" and new.get(name) != "PASS"]
    if result["functional_regressions"]:
        reasons.append("functional regressions: " + ", ".join(result["functional_regressions"]))
    baseline_errors = validate_result(baseline, policy)
    if baseline_errors:
        reasons.append("baseline is not fully validated: " + "; ".join(baseline_errors))
        result["status"] = "INCOMPLETE"
    equivalent = equivalent_environments(baseline.get("environment", {}), candidate.get("environment", {}))
    for name in ("architecture", "policy_sha256"):
        equivalent = equivalent and baseline.get("metadata", {}).get(name) == candidate.get("metadata", {}).get(name)
    if policy_sha256 is not None:
        equivalent = equivalent and all(r.get("metadata", {}).get("policy_sha256") == policy_sha256
                                        for r in (baseline, candidate))
    if not equivalent:
        reasons.append("test environment, architecture or validation policy differs; comparison refused")
        result["status"] = "NON_COMPARABLE"
    improved = False
    for name in sorted(set(baseline.get("metrics", {})) | set(candidate.get("metrics", {}))):
        a, b = baseline.get("metrics", {}).get(name), candidate.get("metrics", {}).get(name)
        if any(isinstance(x, bool) or not isinstance(x, (int, float)) or not math.isfinite(x) for x in (a, b)):
            continue
        delta = b - a
        reduction = (a - b) * 100 / a if a else (0 if b == 0 else None)
        higher = name in policy["higher_is_better"]
        improvement = -reduction if higher and reduction is not None else reduction
        result["differences"].append({"metric": name, "baseline": a, "candidate": b,
                                      "delta": delta, "reduction_percent": reduction,
                                      "improvement_percent": improvement})
        limit = policy["maximum_regression_percent"].get(name)
        if equivalent and limit is not None:
            regressed = (improvement is None and b > a) or (improvement is not None and improvement < -limit)
            if regressed:
                reasons.append(f"{name} exceeds regression limit {limit}%")
            if improvement is not None and improvement > 0:
                improved = True
    if policy.get("require_improvement") and not improved:
        reasons.append("no measured improvement in a policy-controlled metric")
    if not reasons:
        result["status"] = "ACCEPTED"
        reasons.append("all mandatory tests passed under equivalent conditions; measured thresholds satisfied")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline")
    parser.add_argument("optimized")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        if args.output and any(args.output.resolve().is_relative_to(p.resolve())
                               for p in (ROOT / "reports/baseline", ROOT / "build/baseline")):
            raise ValueError("comparison output may not modify an immutable baseline")
        result = compare(resolve_result(args.baseline), resolve_result(args.optimized),
                         read_json(ROOT / "config/policy.json"), sha256(ROOT / "config/policy.json"))
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f"Comparison blocked: {exc}")
        return 2
    print(json.dumps(result, indent=2, allow_nan=False))
    if args.output:
        write_json(args.output, result)
    return 0 if result["status"] == "ACCEPTED" else 1


if __name__ == "__main__":
    raise SystemExit(main())
