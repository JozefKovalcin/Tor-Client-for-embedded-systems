#!/usr/bin/env python3
"""Fetch and verify pinned upstream Buildroot; never execute unverified sources."""
import argparse
from pathlib import Path
import os
import shutil
import tarfile
import urllib.request
import uuid
from common import ROOT, read_json, sha256


def extract_upstream(tar, parent, root_name):
    """Defer inert filesystem-skeleton symlinks until all regular writes finish.

    Buildroot legitimately contains absolute target-root symlinks. Python's
    data filter rejects them. Never follow them while extracting source files.
    """
    parent = Path(parent).resolve()
    members = tar.getmembers()
    links = {Path(m.name) for m in members if m.issym()}
    for member in members:
        name = Path(member.name)
        if name.is_absolute() or ".." in name.parts or not name.parts or name.parts[0] != root_name:
            raise ValueError("unsafe upstream archive member: " + member.name)
        if any(ancestor in links for ancestor in name.parents):
            raise ValueError("archive writes through a symlink: " + member.name)
        if not (member.isdir() or member.isfile() or member.issym() or member.islnk()):
            raise ValueError("special archive entry refused: " + member.name)
        if member.islnk() and (Path(member.linkname).is_absolute() or ".." in Path(member.linkname).parts
                              or Path(member.linkname).parts[0] != root_name):
            raise ValueError("unsafe archive hardlink")
    for member in members:
        path = parent / member.name
        if member.issym():
            continue
        if member.isdir():
            path.mkdir(parents=True, exist_ok=True)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            with tar.extractfile(member) as src, path.open("xb") as dst:
                shutil.copyfileobj(src, dst)
            path.chmod(member.mode & 0o777)
    for member in members:
        if member.issym():
            path = parent / member.name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.symlink_to(member.linkname)
    for member in reversed(members):
        if member.isdir():
            (parent / member.name).chmod(member.mode & 0o777)


def source_tree():
    lock = read_json(ROOT / "config/sources.lock.json")["buildroot"]
    cache = ROOT / "downloads"
    cache.mkdir(exist_ok=True)
    archive = cache / ("buildroot-" + lock["version"] + ".tar.xz")
    if not archive.exists():
        part = archive.with_name(archive.name + ".partial-" + uuid.uuid4().hex)
        with urllib.request.urlopen(lock["url"], timeout=60) as response, part.open("xb") as output:
            while block := response.read(1024 * 1024):
                output.write(block)
        part.rename(archive)
    if sha256(archive) != lock["sha256"]:
        raise ValueError("Buildroot archive SHA-256 mismatch; cached file retained for investigation")
    parent = ROOT / "build/sources" / lock["sha256"][:16]
    source = parent / ("buildroot-" + lock["version"])
    if not source.exists():
        parent.mkdir(parents=True, exist_ok=True)
        stage = parent / (".extracting-" + uuid.uuid4().hex)
        stage.mkdir()
        with tarfile.open(archive) as tar:
            extract_upstream(tar, stage, source.name)
        (stage / source.name).rename(source)
        stage.rmdir()
    # Verify actual build inputs against the pinned archive on every invocation.
    # Do not silently discard edits in a user's cached source tree.
    import hashlib
    with tarfile.open(archive) as tar:
        for member in tar:
            path = parent / member.name
            if member.isfile() or member.islnk():
                expected = hashlib.sha256(tar.extractfile(member).read()).hexdigest()
                if not path.is_file() or path.is_symlink() or sha256(path) != expected:
                    raise ValueError("modified or incomplete upstream source: " + str(path))
            elif member.issym():
                # WSL's Windows-backed filesystem normalizes trailing slashes.
                if not path.is_symlink() or os.readlink(path).rstrip("/") != member.linkname.rstrip("/"):
                    raise ValueError("modified upstream symlink: " + str(path))
    return source


if __name__ == "__main__":
    argparse.ArgumentParser(description=__doc__).parse_args()
    print(source_tree())
