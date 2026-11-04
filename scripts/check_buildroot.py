#!/usr/bin/env python3
"""Resolve real Kconfig on Linux without building the cross-toolchain or target."""
import os
from pathlib import Path
import subprocess
import sys
from bootstrap import source_tree
from common import ROOT, read_json
from validate import check_resolved_config


def main():
    if sys.platform != "linux":
        raise SystemExit("Real Kconfig resolution requires Linux")
    source = source_tree()
    for profile_path in sorted((ROOT / "config/profiles").glob("*.json")):
        profile = read_json(profile_path)
        output = ROOT / "build/config-check" / profile["id"]
        output.mkdir(parents=True, exist_ok=True)
        subprocess.run(["make", "-C", str(source), "O=" + str(output),
                        "BR2_EXTERNAL=" + str(ROOT / "br2-external"), profile["defconfig"]], check=True)
        check_resolved_config(ROOT / "br2-external/configs" / profile["defconfig"], output / ".config")
        print("Resolved Kconfig: " + profile["id"])


if __name__ == "__main__":
    main()
