#!/usr/bin/env python3
"""Fast portable repository checks; no network or Tor binary required."""
import ast
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
from common import ROOT, read_json


def check():
    for pattern in ("scripts/*.py", "tests/*.py"):
        for path in ROOT.glob(pattern):
            ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    for path in (ROOT / "config").rglob("*.json"):
        json.loads(path.read_text(encoding="utf-8"))
    profile = read_json(ROOT / "config/profiles/qemu-x86_64.json")
    policy = read_json(ROOT / "config/policy.json")
    mandatory = {"security_config", "tor_config", "tor_startup", "socks5", "public_bootstrap",
                 "onion_v3", "two_node", "bidirectional_integrity", "reconnection"}
    if not mandatory.issubset(set(policy["required_tests"])):
        raise ValueError("validation policy removed required Tor functionality")
    path = ROOT / "br2-external/configs" / profile["defconfig"]
    text = path.read_text()
    for setting in ("BR2_PACKAGE_TOR=y", "BR2_REPRODUCIBLE=y", "BR2_DOWNLOAD_FORCE_CHECK_HASHES=y",
                    "BR2_SSP_STRONG=y", "BR2_PIC_PIE=y", "BR2_RELRO_FULL=y"):
        if setting not in text.splitlines():
            raise ValueError("required defconfig setting missing: " + setting)
    keys = re.findall(r"^(BR2_[A-Za-z0-9_]+)=", text, re.M)
    if len(keys) != len(set(keys)):
        raise ValueError("duplicate defconfig settings")
    for name in ("linux.config", "busybox.config", "torrc.in", "post-build.sh"):
        if not (ROOT / "br2-external/board/qemu" / name).is_file():
            raise ValueError("missing configuration: " + name)
    kernel = read_json(ROOT / "config/sources.lock.json")["kernel_version"]
    for package in ("linux", "linux-headers"):
        hashes = ROOT / "br2-external/patches" / package / (package + ".hash")
        if not hashes.is_file() or ("linux-" + kernel + ".tar.xz") not in hashes.read_text():
            raise ValueError("missing custom kernel source hash: " + package)
    # Syntax-check shell with a real POSIX shell when available. Windows WSL
    # launchers are not a usable sh; Linux CI enforces these checks.
    if sys.platform == "linux":
        for path in [*ROOT.glob("scripts/*.sh"), *ROOT.glob("br2-external/board/qemu/*.sh"),
                     ROOT / "br2-external/board/qemu/overlay/etc/init.d/S99tor-validation"]:
            subprocess.run(["sh", "-n", str(path)], check=True)
        if shutil.which("shellcheck"):
            subprocess.run(["shellcheck", "scripts/validate.sh", "br2-external/board/qemu/post-build.sh",
                            "br2-external/board/qemu/overlay/etc/init.d/S99tor-validation"], cwd=ROOT, check=True)
    print("Configuration and script checks passed")


if __name__ == "__main__":
    try:
        check()
        subprocess.run([sys.executable, "-m", "unittest", "discover", "-s", "tests", "-p", "test_*.py", "-v"],
                       cwd=ROOT, check=True)
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1)
