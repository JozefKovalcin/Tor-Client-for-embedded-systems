#!/usr/bin/env python3
"""Collect target files and boot artifact bytes, not host /usr/bin/tor measurements."""
from pathlib import Path
import os
import subprocess
from common import sha256, write_json


def file_total(paths):
    seen, total = set(), 0
    for path in paths:
        if path.is_symlink() or not path.is_file():
            continue
        info = path.stat()
        identity = (info.st_dev, info.st_ino)
        if identity not in seen:
            seen.add(identity)
            total += info.st_size
    return total


def collect(output, profile, run):
    target = output / "target"
    images = output / "images"
    tor = target / "usr/bin/tor"
    artifacts = {name: images / value for name, value in profile["artifacts"].items()}
    for path in [tor, *artifacts.values()]:
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError("missing or empty build artifact: " + str(path))
    artifact_rows = [{"role": name, "path": str(p.relative_to(output)), "bytes": p.stat().st_size,
                      "sha256": sha256(p)} for name, p in artifacts.items()]
    write_json(run / "artifacts.json", artifact_rows)
    libraries = [p for d in ("lib", "usr/lib", "lib64", "usr/lib64")
                 for p in (target / d).rglob("*") if ".so" in p.name]
    metrics = {"tor_executable_bytes": tor.stat().st_size,
               "library_bytes": file_total(libraries),
               "kernel_bytes": artifacts["kernel"].stat().st_size,
               "rootfs_bytes": artifacts["rootfs"].stat().st_size,
               "complete_image_bytes": file_total(artifacts.values()),
               "installed_rootfs_bytes": file_total(target.rglob("*")),
               "persistent_storage_bytes": None, "idle_rss_bytes": None, "idle_pss_bytes": None,
               "peak_process_rss_bytes": None, "peak_system_used_bytes": None,
               "bootstrap_seconds": None, "onion_connection_seconds": None,
               "cpu_percent": None, "throughput_bytes_per_second": None}
    # Record both the configured filesystem capacity and actual sparse host allocation.
    metrics["image_host_allocated_bytes"] = sum(getattr(p.stat(), "st_blocks", 0) * 512 for p in artifacts.values()) if os.name == "posix" else None
    return metrics


def source_provenance(output, source, downloads, run):
    info = subprocess.check_output(["make", "--no-print-directory", "-s", "-C", str(source),
                                    "O=" + str(output), "show-info"], text=True)
    import json
    packages = json.loads(info)
    write_json(run / "packages.json", packages)
    # All downloaded source archive hashes, including transitive host dependencies.
    rows = [{"path": str(p.relative_to(downloads)), "sha256": sha256(p), "bytes": p.stat().st_size}
            for p in sorted(downloads.rglob("*")) if p.is_file() and not p.is_symlink()
            and not any(x in p.parts for x in (".git", "git", "svn"))
            and not p.name.endswith((".lock", ".partial"))]
    provenance = {"packages": packages, "downloads": rows}
    write_json(run / "source-provenance.json", provenance)
    return sha256(run / "source-provenance.json")
