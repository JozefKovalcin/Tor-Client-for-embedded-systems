"""Review and execute local, pinned MSYS2 package transactions."""

from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

from .scan import MSYS, RecipeProbe, ordinary_path, sha256


PACKAGES = (
    ("make", "make", "4.4.1-3", "msys/x86_64", "x86_64",
     "af0bdba17f06fe037f0194069adaa31a8fe45f1a11381501896aea1fae37bd5d", 1698693),
    ("pkg-config", "mingw-w64-ucrt-x86_64-pkgconf", "1~3.0.7-1", "mingw/ucrt64", "any",
     "74b6fe685d84ec6ea19b9ee285d752d122e31b6b93038f933627b05324d8f2a3", 891290),
)
PACMAN_CONFIG = """[options]
Architecture = x86_64
SigLevel = Required TrustedOnly
LocalFileSigLevel = Required TrustedOnly
GPGDir = /etc/pacman.d/gnupg/
"""


def plan_digest(data):
    encoded = json.dumps(data, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def installation_plan(report, cache=None):
    cache = MSYS / "var/cache/pacman/pkg" if cache is None else Path(cache)
    observations = {row["id"]: row["status"] for row in report["dependencies"]}
    selected, blocked = [], []
    for check, package, version, repo_path, arch, digest, installed in PACKAGES:
        state = observations.get(check)
        if state != "MISSING":
            continue
        filename = f"{package}-{version}-{arch}.pkg.tar.zst"
        archive = cache / filename
        try:
            signature = ordinary_path(cache / (filename + ".sig"))
            if sha256(archive) != digest:
                raise ValueError("Package hash differs from reviewed pin")
            size = archive.stat().st_size + signature.stat().st_size
        except (OSError, ValueError):
            blocked.append(package)
            continue
        selected.append({"id": check, "package": package, "version": version, "sha256": digest,
                         "archive": filename, "sourceUrl": "https://mirror.msys2.org/" + repo_path + "/" + filename,
                         "stagingBytes": size, "approximateInstalledBytes": installed})
    unsupported = sorted(name for name, state in observations.items()
                         if state in ("MISSING", "INCOMPATIBLE", "BLOCKED") and name not in {p[0] for p in PACKAGES})
    result = {"kind": "windows-install-plan", "profile": report["profile"], "packages": selected,
              "blockedArchives": blocked, "manualDependencies": unsupported,
              "effects": "Writes the existing MSYS2 installation. No elevation, PATH changes, updates, downloads or reboot requested.",
              "status": "AVAILABLE" if selected and observations.get("pacman") == "AVAILABLE" else "BLOCKED"}
    return {**result, "approvalSha256": plan_digest(result)}


@contextmanager
def operation_lock(workspace, name):
    lock = workspace / name
    if lock.exists():
        ordinary_path(lock)
    with lock.open("a+b") as stream:
        stream.seek(0, os.SEEK_END)
        if not stream.tell():
            stream.write(b"\x00")
            stream.flush()
        stream.seek(0)
        if os.name == "nt":
            import msvcrt
            try:
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            except OSError as exc:
                raise ValueError("Another developer operation is using this workspace") from exc
        else:
            import fcntl
            try:
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            except OSError as exc:
                raise ValueError("Another developer operation is using this workspace") from exc
        yield


def native_environment():
    return {"PATH": os.pathsep.join((str(MSYS / "ucrt64/bin"), str(MSYS / "usr/bin"),
                                    str(Path(os.environ.get("SystemRoot", "C:/Windows")) / "System32"))),
            **{key: os.environ[key] for key in ("SystemRoot", "WINDIR", "SystemDrive") if key in os.environ},
            "MSYSTEM": "UCRT64", "MINGW_PREFIX": "/ucrt64", "MSYS2_PATH_TYPE": "strict", "LANG": "C", "LC_ALL": "C", "TZ": "UTC"}


def msys_path(path):
    result = RecipeProbe().output(MSYS / "usr/bin/cygpath.exe", "-u", "--", str(path))
    text = result.stdout.strip()
    if result.returncode or not text.startswith("/") or "\n" in text or "\r" in text:
        raise ValueError("MSYS2 could not map a local path")
    return text


def stage_package(cache, destination, row):
    filename = row["archive"]
    item = destination / filename
    for suffix in ("", ".sig"):
        original = ordinary_path(cache / (filename + suffix))
        with original.open("rb") as src, (destination / (filename + suffix)).open("xb") as dst:
            shutil.copyfileobj(src, dst)
    if sha256(item) != row["sha256"]:
        raise ValueError("Staged archive changed after approval")
    return item


def require_approval(plan, approved_hash):
    current_hash = plan_digest({key: value for key, value in plan.items() if key != "approvalSha256"})
    if not approved_hash or approved_hash != current_hash or current_hash != plan["approvalSha256"] or plan["status"] != "AVAILABLE":
        raise ValueError("A specific current install plan must be approved")


def install(plan, approved_hash, workspace, *, refresh=None, authorize=None):
    require_approval(plan, approved_hash)
    if os.name != "nt":
        raise ValueError("A native Windows installation is required")
    workspace = ordinary_path(workspace, directory=True)
    cache = MSYS / "var/cache/pacman/pkg"
    pacman = ordinary_path(MSYS / "usr/bin/pacman.exe")
    gpg = ordinary_path(MSYS / "usr/bin/gpg.exe")
    env = native_environment()
    with operation_lock(workspace, ".install-lock"):
        if refresh is None:
            raise ValueError("An installation requires a fresh host scan")
        require_approval(installation_plan(refresh()), approved_hash)
        if (MSYS / "var/lib/pacman/db.lck").exists():
            raise ValueError("MSYS2 package database is in use or needs manual repair")
        stage = Path(tempfile.mkdtemp(prefix="install-", dir=workspace))
        config = stage / "pacman.conf"
        config.write_text(PACMAN_CONFIG, encoding="utf-8", newline="\n")
        staged = []
        for row in plan["packages"]:
            item = stage_package(cache, stage, row)
            archive_path = msys_path(item)
            cmd = [str(gpg), "--homedir", "/etc/pacman.d/gnupg", "--batch", "--no-auto-key-retrieve",
                   "--status-fd", "1", "--verify", archive_path + ".sig", archive_path]
            signature = subprocess.run(cmd, stdin=subprocess.DEVNULL, capture_output=True, text=True,
                                       encoding="utf-8", errors="replace", env=env, cwd=stage, timeout=30, check=False)
            valid = any(line.startswith("[GNUPG:] VALIDSIG ") for line in signature.stdout.splitlines())
            trusted = any(line.startswith(("[GNUPG:] TRUST_FULLY", "[GNUPG:] TRUST_ULTIMATE")) for line in signature.stdout.splitlines())
            if signature.returncode or not valid or not trusted:
                raise ValueError("The package signature was not trusted by the installed MSYS2 keyring")
            identity = subprocess.run([str(pacman), "-Qp", "--", archive_path], stdin=subprocess.DEVNULL,
                                      capture_output=True, text=True, encoding="utf-8", env=env, cwd=stage,
                                      timeout=10, check=False)
            if identity.returncode or identity.stdout.strip() != f"{row['package']} {row['version']}":
                raise ValueError("The package metadata does not match its approved identity")
            installed = subprocess.run([str(pacman), "-Q", "--", row["package"]], stdin=subprocess.DEVNULL,
                                       capture_output=True, text=True, env=env, cwd=stage, timeout=10, check=False)
            if installed.returncode == 0:
                raise ValueError("The selected package is already installed; refresh the plan")
            staged.append(archive_path)
        command = [str(pacman), "--config", msys_path(config), "-U", "--noconfirm", "--", *staged]
        return execute_transaction(command, stage, env, authorize)


def execute_transaction(command, stage, env, authorize):
    if authorize is not None and not authorize():
        raise ValueError("Installation was cancelled before the MSYS2 transaction")
    print("[phase] installing approved packages", flush=True)
    with (stage / "pacman.log").open("xb") as log, subprocess.Popen(command, stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env, cwd=stage) as process:
        for line in iter(lambda: process.stdout.readline(65536), b""):
            log.write(line)
            print(line.decode("utf-8", "replace").rstrip(), flush=True)
        return process.wait()
