#!/usr/bin/env python3
"""Repeat the paired, one-endpoint capacity32 runs using preserved builds."""

import argparse
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from capacity_native import run_case
from capacity_peer import Peer
from common import git_metadata, read_json, sha256, utcnow, write_json


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--arm-run", required=True, help="ARM pipeline build run ID")
    parser.add_argument("--arm-fixture", type=Path, required=True)
    parser.add_argument("--x86-fixture", type=Path, required=True)
    parser.add_argument("--peer-session", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--rounds", type=int, choices=range(1, 13), default=3)
    args = parser.parse_args()

    arm = read_json(ROOT / "reports/benchmarks" / args.arm_run / "results.json")
    x86_run = "20261105T160654Z-f407769f"
    x86 = read_json(ROOT / "reports/benchmarks" / x86_run / "results.json")
    images = {
        "arm": ("qemu-arm-capacity32", str(Path(arm["build"]["output_directory"]) / "images"), args.arm_run),
        "x86": ("qemu-x86_64-capacity32", "build/iterations/12-capacity32", x86_run),
    }
    fixtures = {"arm": args.arm_fixture.resolve(), "x86": args.x86_fixture.resolve()}
    origins = {"arm": arm, "x86": x86}
    for _, directory, build_id in images.values():
        artifacts = read_json(ROOT / "reports/benchmarks" / build_id / "artifacts.json")
        for artifact in artifacts:
            path = ROOT / directory / Path(artifact["path"]).name
            if sha256(path) != artifact["sha256"]:
                raise ValueError(f"build artifact digest mismatch: {path}")
    state = read_json(args.peer_session)
    peer = Peer({**state["public"], "tor_sha256": state["provenance"]["tor_sha256"]})
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    orders = [("arm", 128), ("x86", 128), ("x86", 256), ("arm", 256)]
    plan = {
        "created_before_measurements": utcnow(), "git": git_metadata(),
        "images": images, "ram_order": [128, 256], "repeats": args.rounds,
        "orders": [orders if n % 2 == 0 else orders[::-1] for n in range(args.rounds)],
        "fixtures": {key: sha256(path) for key, path in fixtures.items()},
        "peer_tor_sha256": peer.data["tor_sha256"], "results": [],
    }
    write_json(output / "plan.json", plan)
    for repeat, order in enumerate(plan["orders"], 1):
        for arch, memory in order:
            profile, directory, _ = images[arch]
            run = output / f"{repeat:02d}-{arch}-{memory}"
            print("START", run.name, flush=True)
            result = run_case(profile, Path(directory), fixtures[arch], memory, run, peer, origins[arch])
            plan["results"].append({"image": arch, "memory_mib": memory, "directory": str(run),
                                    "qualification": result["qualification"]})
            write_json(output / "plan.json", plan)
            print("DONE", run.name, result["qualification"], flush=True)
    return int(any(not row["qualification"][key] for row in plan["results"] for key in
                   ("functional_pass", "queue_reserve_integrity_pass", "memory_margin_pass", "state_capacity_pass")))


if __name__ == "__main__":
    raise SystemExit(main())
