#!/usr/bin/env python3
"""Short native Windows QEMU smoke test of preserved capacity32 images.

No build or performance qualification is claimed. Disks use disposable snapshots.
Only serial output is exported; Tor state and control cookies stay in the guest.
"""
import argparse
import contextlib
import os
from pathlib import Path
import platform
import re
import socket
import subprocess
import time

from common import ROOT, git_metadata, read_json, sha256, utcnow, write_json


def probe(arch, images, qemu_dir, output, bootstrap_timeout):
    profile = read_json(ROOT / f"config/profiles/qemu-{arch}-capacity32.json")
    evidence = ROOT / ("reports/arm-2026-11-05/build-artifacts.json" if arch == "arm"
                       else "reports/capacity-2026-11-05/capacity32-build/artifacts.json")
    directory = output / arch
    directory.mkdir()
    observer_key = "arm" if arch == "arm" else "x86"
    observer = ROOT / f"build/windows-qemu-inputs/fixtures/arm-study-{observer_key}-observer.ext4"
    observer_expected = read_json(ROOT / "reports/arm-2026-11-05/observer-provenance.json")[observer_key]["sha256"]
    if sha256(observer) != observer_expected:
        raise ValueError("Observer disk checksum mismatch")
    hashes = {}
    for item in read_json(evidence):
        name = Path(item["path"]).name
        hashes[name] = sha256(images / name)
        if hashes[name] != item["sha256"]:
            raise ValueError(f"Preserved {arch} {name} checksum mismatch")
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        serial_port = reservation.getsockname()[1]
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        agent_port = reservation.getsockname()[1]
    executable = qemu_dir / (profile["qemu"] + ".exe")
    environment = profile["environment"]
    command = [str(executable), "-machine", environment["machine"], "-cpu", environment["cpu"],
               "-accel", "tcg,thread=single", "-m", "256", "-smp", "1", "-display", "none",
               "-monitor", "none", "-no-reboot", "-serial",
               f"tcp:127.0.0.1:{serial_port},server=on,wait=on", "-kernel",
               str(images / profile["artifacts"]["kernel"]), "-drive",
               f"file={images / 'disk.img'},if=virtio,format=raw,snapshot=on", "-append",
               "root=/dev/vda1 ro console=" + profile.get("qemu_console", "ttyS0") +
               ",115200 init=/bin/sh tor_validation_external=1", "-netdev",
               f"user,id=n0,hostfwd=tcp:127.0.0.1:{agent_port}-:19090",
               "-device", "virtio-net-pci,netdev=n0", "-device", "virtio-rng-pci",
               "-drive", f"file={observer},if=virtio,format=raw,readonly=on"]
    write_json(directory / "qemu-command.json", command)
    result = {"architecture": arch, "started": utcnow(), "image_sha256": hashes,
              "qemu_version": subprocess.check_output([str(executable), "--version"], text=True).splitlines()[0],
              "memory_mib": 256, "observer_sha256": observer_expected,
              "tests": [], "metrics": {}, "status": "BLOCKED"}
    serial = None
    data = bytearray()
    process = None
    started = time.monotonic()
    log = (directory / "serial.log").open("wb")

    def wait(pattern, timeout):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            match = re.search(pattern, data, re.S)
            if match:
                return match
            if process.poll() is not None:
                raise RuntimeError("QEMU exited; see preserved logs")
            try:
                block = serial.recv(65536)
                if not block:
                    raise RuntimeError("Serial connection closed")
                data.extend(block)
                log.write(block)
                log.flush()
            except socket.timeout:
                pass
        raise TimeoutError("Serial phase deadline exceeded; see preserved logs")

    def send(command_text):
        serial.sendall(command_text.encode("ascii") + b"\n")

    def record(name, passed, detail, failure_status="FAIL"):
        status = "PASS" if passed else failure_status
        result["tests"].append({"id": name, "status": status, "detail": detail})
        print(arch, name, status, flush=True)

    try:
        with (directory / "qemu-stderr.log").open("wb") as stderr:
            process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=stderr,
                                       stderr=subprocess.STDOUT, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
            deadline = time.monotonic() + 15
            while True:
                try:
                    serial = socket.create_connection(("127.0.0.1", serial_port), timeout=1)
                    serial.settimeout(.25)
                    break
                except OSError:
                    if process.poll() is not None or time.monotonic() > deadline:
                        raise RuntimeError("QEMU serial startup failed; see qemu-stderr.log")
                    time.sleep(.2)
            wait(rb"(?:^|\n)[/~] # ", 60)
            record("linux_boot", True, "Original kernel and disk booted in native Windows QEMU")
            send("mount -t proc proc /proc && mkdir -p /dev/pts /dev/shm && mount -a && stty -echo && "
                 "mkdir -p /run/lock/subsys && hostname -F /etc/hostname && /etc/init.d/rcS && "
                 "printf '\\nSMOKE_INIT_OK\\n'")
            wait(rb"(?:^|\n)SMOKE_INIT_OK\r?\n", 60)
            send("echo SMOKE_INFO_BEGIN; uname -m; date -u +%s; ip -4 addr show eth0; "
                 "cat /proc/mounts; sha256sum /usr/bin/tor; /usr/bin/tor --version; "
                 "echo SMOKE_INFO_END")
            info = wait(rb"SMOKE_INFO_BEGIN\r?\n(.*?)SMOKE_INFO_END\r?\n", 15)[1].decode(errors="replace")
            result["guest_info"] = info
            record("ethernet_dhcp", "10.0.2.15/24" in info, "VirtIO Ethernet acquired QEMU NAT address")
            guest_epoch = re.search(r"\n(\d{10})\r?\n", info)
            offset = int(guest_epoch[1]) - time.time() if guest_epoch else None
            result["metrics"]["clock_offset_seconds"] = offset
            record("valid_time", offset is not None and abs(offset) < 60, "Guest UTC compared with Windows clock")
            mounts = [line.split() for line in info.splitlines()]
            root_ok = any(len(x) > 3 and x[1] == "/" and x[2] == "squashfs" and "ro" in x[3].split(",") for x in mounts)
            state_ok = any(len(x) > 3 and x[1] == "/var/lib/tor" and x[2] == "ext4" and "rw" in x[3].split(",") for x in mounts)
            record("root_and_state", root_ok and state_ok, "Read-only SquashFS and separate writable ext4 state")
            send("TOR_FORCE_START=1 /etc/init.d/S50tor start && printf '\\nSMOKE_TOR_STARTED\\n'")
            wait(rb"(?:^|\n)SMOKE_TOR_STARTED\r?\n", 30)
            send("echo SMOKE_SECURITY_BEGIN; cat /etc/tor/torrc /etc/tor/defaults-torrc; "
                 "cat /proc/$(cat /run/tor/tor.pid)/status; echo SMOKE_SECURITY_END")
            security = wait(rb"SMOKE_SECURITY_BEGIN\r?\n(.*?)SMOKE_SECURITY_END\r?\n", 10)[1].decode(errors="replace")
            result["security_info"] = security
            uid = re.search(r"^Uid:\s+(.+)$", security, re.M)
            gid = re.search(r"^Gid:\s+(.+)$", security, re.M)
            safe = all(line in security.splitlines() for line in ["CookieAuthentication 1", "SafeLogging 1",
                       "SocksPort 127.0.0.1:9050", "ControlPort 127.0.0.1:9051"])
            record("production_tor", safe and all(m and all(v == "905" for v in m[1].split()) for m in (uid, gid)),
                   "Production init script; UID/GID 905; loopback listeners, cookie authentication and SafeLogging")
            send("mkdir -p /run/probe && mount -t ext4 -o ro,noload /dev/vdb /run/probe && "
                 "/run/probe/native-agent >/run/observer.log 2>&1 &")
            send("sleep 1; cat /run/observer.log; echo SMOKE_OBSERVER_END")
            wait(rb"CAPACITY_AGENT_READY\r?\n.*?SMOKE_OBSERVER_END\r?\n", 10)
            with socket.create_connection(("127.0.0.1", agent_port), timeout=5) as proxy:
                proxy.sendall(b"SOCKS\n\x05\x01\x00")
                reply = proxy.makefile("rb").read(2)
            record("socks5", reply == b"\x05\x00", "Guest loopback SOCKS5 negotiation returned 05 00 through read-only diagnostic agent")
            from capacity_native import Agent
            with contextlib.closing(Agent(agent_port).control()) as control:
                answer = control.command("GETINFO version")
            record("authenticated_control", "0.4.9.14" in answer, "SAFECOOKIE authentication completed inside guest; no credentials exported")
            deadline = time.monotonic() + bootstrap_timeout
            bootstrapped = False
            result["bootstrap_observations"] = []
            while time.monotonic() < deadline:
                with contextlib.closing(Agent(agent_port).control()) as control:
                    phase = control.command("GETINFO status/bootstrap-phase")
                progress = int(re.search(r"PROGRESS=(\d+)", phase)[1])
                if not result["bootstrap_observations"] or result["bootstrap_observations"][-1]["progress"] != progress:
                    result["bootstrap_observations"].append({"progress": progress, "seconds": time.monotonic() - started})
                    print(arch, "public Tor bootstrap", str(progress) + "%", flush=True)
                if progress == 100:
                    bootstrapped = True
                    result["metrics"]["boot_to_bootstrap_seconds"] = time.monotonic() - started
                    break
                time.sleep(1)
            record("public_bootstrap", bootstrapped, "Public Tor bootstrap reached 100%" if bootstrapped else "Short-test bootstrap deadline exceeded", "BLOCKED")
            result["status"] = "PASS" if all(row["status"] == "PASS" for row in result["tests"]) else "FAIL"
            if any(row["status"] == "BLOCKED" for row in result["tests"]):
                result["status"] = "BLOCKED"
            send("sync; poweroff -f")
    except (OSError, RuntimeError, TimeoutError) as exc:
        result["error"] = str(exc)
        print(arch, "BLOCKED", str(exc), flush=True)
    finally:
        if serial:
            serial.close()
        if process and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        log.close()
        record("source_images_unchanged", all(sha256(images / name) == digest for name, digest in hashes.items()),
               "Original sealed image hashes unchanged after snapshot run")
        if sha256(observer) != observer_expected:
            result["status"] = "FAIL"
        if any(row["status"] == "FAIL" for row in result["tests"]):
            result["status"] = "FAIL"
        result["seconds"] = time.monotonic() - started
        result["finished"] = utcnow()
        write_json(directory / "result.json", result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--qemu-dir", type=Path, default=Path(os.environ.get("LOCALAPPDATA", ".")) / "Programs/qemu")
    parser.add_argument("--arm-images", type=Path, default=ROOT / "build/arm-capacity32/20261105T190223Z-7002ec13")
    parser.add_argument("--x86-images", type=Path, default=ROOT / "build/windows-qemu-inputs/x86")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--bootstrap-timeout", type=int, default=180)
    args = parser.parse_args()
    if platform.system() != "Windows":
        parser.error("This runner tests native Windows QEMU")
    args.output.mkdir(parents=True, exist_ok=False)
    runner_source = Path(__file__).read_bytes()
    (args.output / "runner.py").write_bytes(runner_source)
    result = {"started": utcnow(), "host": platform.platform(), "git": git_metadata(),
              "runner_sha256": sha256(args.output / "runner.py"),
              "scope": "Short smoke test only; full nine-test integration suite and hardware qualification not executed",
              "cases": []}
    for arch, images in (("arm", args.arm_images), ("x86_64", args.x86_images)):
        result["cases"].append(probe(arch, images.resolve(), args.qemu_dir.resolve(), args.output.resolve(), args.bootstrap_timeout))
        write_json(args.output / "results.json", result)
    return int(any(case["status"] != "PASS" for case in result["cases"]))


if __name__ == "__main__":
    raise SystemExit(main())
