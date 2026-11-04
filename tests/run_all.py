#!/usr/bin/env python3
"""Real Tor integration tests, runnable on Windows/Linux or inside the target VM."""
import argparse
import contextlib
import hashlib
import hmac
import json
import os
import platform
from pathlib import Path
import random
import re
import shutil
import socket
import struct
import subprocess
import threading
import time

from echo_server import SERVER_PAYLOAD, read_frame, receive, send_frame, start_server

OFFLINE = ("security_config", "tor_config", "tor_startup")
ONLINE = ("socks5", "public_bootstrap", "onion_v3", "two_node", "bidirectional_integrity", "reconnection")
USED_PORTS = set()


class Blocked(Exception):
    pass


def port():
    while True:
        with socket.socket() as s:
            s.bind(("127.0.0.1", 0))
            chosen = s.getsockname()[1]
        if chosen not in USED_PORTS:
            USED_PORTS.add(chosen)
            return chosen


def check_security(text):
    directives = {}
    for line in text.splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            key, _, value = line.partition(" ")
            directives.setdefault(key.lower(), []).append(value.strip())
    for key, value in {"cookieauthentication": "1", "cookieauthfilegroupreadable": "0",
                       "clientonly": "1", "safelogging": "1", "runasdaemon": "0"}.items():
        if directives.get(key) != [value]:
            raise AssertionError("unsafe or duplicate directive: " + key)
    for key in ("socksport", "controlport"):
        values = directives.get(key, [])
        if len(values) != 1 or not re.fullmatch(r"127\.0\.0\.1:[0-9]+", values[0]):
            raise AssertionError("listener must be unique and loopback-only: " + key)
    for key in ("orport", "dirport", "dnsport", "transport", "httptunnelport", "sockslistenaddress",
                "controllistenaddress", "__owningcontrollerprocess", "hashedcontrolpassword"):
        if key in directives:
            raise AssertionError("unexpected directive: " + key)


class Control:
    def __init__(self, address):
        self.sock = socket.create_connection(("127.0.0.1", address), timeout=5)
        self.file = self.sock.makefile("rb")

    def close(self):
        self.file.close()
        self.sock.close()

    def command(self, text, expected=250):
        self.sock.sendall(text.encode("ascii") + b"\r\n")
        lines = []
        while True:
            line = self.file.readline(65536).decode("ascii").rstrip("\r\n")
            if len(line) < 4:
                raise ConnectionError("truncated control response")
            lines.append(line)
            if line[3] == " ":
                if int(line[:3]) != expected:
                    # Never expose authentication arguments or response challenges.
                    raise AssertionError("control command rejected with code " + line[:3])
                return "\n".join(lines)
            if line[3] == "+":
                raise AssertionError("unexpected multiline control data response")

    def authenticate(self, cookie_path):
        cookie = cookie_path.read_bytes()
        if len(cookie) != 32:
            raise AssertionError("invalid control cookie length")
        nonce = os.urandom(32)
        answer = self.command("AUTHCHALLENGE SAFECOOKIE " + nonce.hex())
        server_hash = bytes.fromhex(re.search(r"SERVERHASH=([A-Fa-f0-9]+)", answer).group(1))
        server_nonce = bytes.fromhex(re.search(r"SERVERNONCE=([A-Fa-f0-9]+)", answer).group(1))
        msg = cookie + nonce + server_nonce
        expected = hmac.new(b"Tor safe cookie authentication server-to-controller hash", msg, hashlib.sha256).digest()
        if not hmac.compare_digest(server_hash, expected):
            raise AssertionError("SAFECOOKIE server authentication failed")
        proof = hmac.new(b"Tor safe cookie authentication controller-to-server hash", msg, hashlib.sha256).hexdigest()
        self.command("AUTHENTICATE " + proof)


class Node:
    def __init__(self, name, root, tor, template, offline, service_port=None):
        self.name, self.tor = name, tor
        self.root = root / name
        self.root.mkdir(mode=0o700)
        self.data = self.root / "data"
        self.data.mkdir(mode=0o700)
        self.hs = self.root / "onion"
        self.log = self.root / "notice.log"
        self.socks, self.control = port(), port()
        while self.control == self.socks:
            self.control = port()
        values = {"DATA": self.data.as_posix(), "SOCKS": str(self.socks), "CONTROL": str(self.control),
                  "LOG": self.log.as_posix(), "OFFLINE": "1" if offline else "0"}
        text = template
        for key, value in values.items():
            text = text.replace("@" + key + "@", value)
        if service_port:
            text += f'\nHiddenServiceDir "{self.hs.as_posix()}"\nHiddenServiceVersion 3\nHiddenServicePort 80 127.0.0.1:{service_port}\n'
        self.config = self.root / "torrc"
        self.config.write_text(text, encoding="utf-8")
        self.defaults = self.root / "defaults-torrc"
        self.defaults.write_text("# Intentionally empty; ignore host defaults.\n", encoding="utf-8")
        self.process = None
        self.output = None
        self.started = None

    def start(self):
        self.output = (self.root / "process.log").open("ab")
        self.started = time.monotonic()
        self.last_bootstrap = None
        self.process = subprocess.Popen([self.tor, "--defaults-torrc", str(self.defaults), "-f", str(self.config)],
                                        stdout=self.output, stderr=subprocess.STDOUT)

    def ready(self):
        if self.process.poll() is not None:
            raise AssertionError(self.name + " exited during startup; see preserved Tor log")
        if not (self.data / "control_auth_cookie").exists():
            return False
        try:
            with contextlib.closing(Control(self.control)) as ctl:
                ctl.authenticate(self.data / "control_auth_cookie")
                ctl.command("GETINFO version")
            return True
        except OSError:
            return False

    def bootstrap(self):
        if self.process.poll() is not None:
            raise AssertionError(self.name + " exited during bootstrap")
        with contextlib.closing(Control(self.control)) as ctl:
            ctl.authenticate(self.data / "control_auth_cookie")
            answer = ctl.command("GETINFO status/bootstrap-phase")
            progress = re.search(r"PROGRESS=(\d+)", answer)
            if progress and progress.group(1) != self.last_bootstrap:
                self.last_bootstrap = progress.group(1)
                print(f"BOOTSTRAP {self.name}: {self.last_bootstrap}%", flush=True)
            return "PROGRESS=100" in answer

    def stop(self, force=False):
        if self.process and self.process.poll() is None:
            self.process.kill() if force else self.process.terminate()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=5)
        if self.output:
            self.output.close()


def wait_for(predicate, seconds, detail):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.25)
    raise Blocked(detail)


def socks_handshake(sock):
    sock.sendall(b"\x05\x01\x00")
    if receive(sock, 2) != b"\x05\x00":
        raise AssertionError("SOCKS5 negotiation failed")


def socks_connect(address, hostname, target_port, timeout):
    if not re.fullmatch(r"[a-z2-7]{56}\.onion", hostname):
        raise ValueError("only v3 onion hostnames are allowed")
    sock = socket.create_connection(("127.0.0.1", address), timeout=timeout)
    try:
        sock.settimeout(timeout)
        socks_handshake(sock)
        host = hostname.encode("ascii")
        # SOCKS domain-name address type; never resolve .onion using ordinary DNS.
        sock.sendall(b"\x05\x01\x00\x03" + bytes([len(host)]) + host + struct.pack("!H", target_port))
        header = receive(sock, 4)
        if header[:3] != b"\x05\x00\x00":
            raise ConnectionError("SOCKS connection failed: reply " + str(header[1]))
        length = {1: 4, 4: 16}.get(header[3])
        if header[3] == 3:
            length = receive(sock, 1)[0]
        if length is None:
            raise AssertionError("invalid SOCKS address type")
        receive(sock, length + 2)
        return sock
    except BaseException:
        sock.close()
        raise


def proc_memory(pid):
    try:
        status = Path(f"/proc/{pid}/status").read_text()
        fields = {k: int(v) * 1024 for k, v in re.findall(r"^(VmRSS|VmHWM):\s+(\d+) kB", status, re.M)}
        try:
            pss = re.search(r"^Pss:\s+(\d+)", Path(f"/proc/{pid}/smaps_rollup").read_text(), re.M)
            fields["Pss"] = int(pss.group(1)) * 1024 if pss else None
        except (OSError, AttributeError):
            fields["Pss"] = None
        # comm can contain spaces; split after the final closing parenthesis.
        stat = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()
        fields["cpu_ticks"] = int(stat[11]) + int(stat[12])
        return fields
    except OSError:
        return {}


class Sampler:
    def __init__(self, nodes):
        self.nodes = nodes
        self.samples = []
        self.stop_event = threading.Event()
        self.thread = threading.Thread(target=self.sample, daemon=True)

    def sample(self):
        while not self.stop_event.is_set():
            processes = {n.name: {"pid": n.process.pid, **proc_memory(n.process.pid)}
                         for n in self.nodes if n.process and n.process.poll() is None}
            used = None
            try:
                info = dict((k, int(v) * 1024) for k, v in
                            re.findall(r"^(MemTotal|MemAvailable):\s+(\d+) kB", Path("/proc/meminfo").read_text(), re.M))
                used = info["MemTotal"] - info["MemAvailable"]
            except (OSError, KeyError):
                pass
            self.samples.append({"time": time.monotonic(), "processes": processes, "system_used_bytes": used})
            self.stop_event.wait(0.2)

    def start(self):
        self.thread.start()

    def stop(self):
        self.stop_event.set()
        self.thread.join(timeout=3)

    def idle(self, start):
        rows = [s for s in self.samples if s["time"] >= start]
        result = {}
        for source, metric in (("VmRSS", "idle_rss_bytes"), ("Pss", "idle_pss_bytes")):
            values = [sum(p[source] for p in s["processes"].values()) for s in rows
                      if len(s["processes"]) == 2 and all(p.get(source) is not None for p in s["processes"].values())]
            result[metric] = sum(values) / len(values) if values else None
        return result

    def metrics(self):
        rss = [sum(p["VmRSS"] for p in s["processes"].values()) for s in self.samples
               if s["processes"] and all("VmRSS" in p for p in s["processes"].values())]
        system = [s["system_used_bytes"] for s in self.samples if s["system_used_bytes"] is not None]
        ticks = {}
        for s in self.samples:
            for p in s["processes"].values():
                if "cpu_ticks" in p:
                    ticks[p["pid"]] = max(ticks.get(p["pid"], 0), p["cpu_ticks"])
        elapsed = self.samples[-1]["time"] - self.samples[0]["time"] if len(self.samples) > 1 else 0
        return {"peak_process_rss_bytes": max(rss) if rss else None,
                "peak_system_used_bytes": max(system) if system else None,
                "cpu_percent": sum(ticks.values()) / os.sysconf("SC_CLK_TCK") / elapsed * 100 if ticks and elapsed else None}


def run(args):
    rows, metrics, nodes, sampler = [], {}, [], None
    os.umask(0o077)
    server = None
    root = args.work_dir.resolve()
    root.mkdir(parents=True, exist_ok=False)
    tor = shutil.which(args.tor)

    def test(name, mode, action):
        start = time.monotonic()
        try:
            action()
            status, detail = "PASS", "explicit assertions passed"
        except Blocked as exc:
            status, detail = "BLOCKED", str(exc)
        except Exception as exc:
            status, detail = "FAIL", type(exc).__name__ + ": " + str(exc)
        rows.append({"id": name, "mode": mode, "status": status, "detail": detail,
                     "seconds": time.monotonic() - start})
        print(f"TEST {name}: {status} ({time.monotonic() - start:.2f}s) {detail}", flush=True)
        return status == "PASS"

    try:
        if not tor:
            raise Blocked("Tor executable is unavailable")
        template = args.template.read_text(encoding="utf-8")
        server = start_server()
        nodes = [Node("A", root, tor, template, args.mode == "offline"),
                 Node("B", root, tor, template, args.mode == "offline", server.server_address[1])]
        def security():
            for n in nodes:
                check_security(n.config.read_text())
                if os.name == "posix" and n.data.stat().st_mode & 0o077:
                    raise AssertionError("data directory permissions exceed 0700")
            if nodes[0].data == nodes[1].data or nodes[0].control == nodes[1].control or nodes[0].socks == nodes[1].socks:
                raise AssertionError("node isolation failed")
        if not test("security_config", "offline", security):
            raise Blocked("security preflight failed; Tor was not started")
        def verify():
            for n in nodes:
                p = subprocess.run([tor, "--defaults-torrc", str(n.defaults), "-f", str(n.config), "--verify-config"],
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
                (n.root / "verify.log").write_bytes(p.stdout)
                if p.returncode:
                    raise AssertionError("tor --verify-config rejected " + n.name)
        if not test("tor_config", "offline", verify):
            raise Blocked("Tor configuration rejected")
        sampler = Sampler(nodes)
        sampler.start()
        def startup():
            for n in nodes:
                n.start()
            for n in nodes:
                wait_for(n.ready, 45, n.name + " control startup timeout")
                with contextlib.closing(Control(n.control)) as ctl:
                    ctl.command("GETINFO version", expected=514)
            metrics["startup_seconds"] = max(time.monotonic() - n.started for n in nodes)
        if not test("tor_startup", "offline", startup):
            raise Blocked("startup prerequisite failed")
        def socks():
            for n in nodes:
                with socket.create_connection(("127.0.0.1", n.socks), timeout=5) as s:
                    socks_handshake(s)
        if args.mode == "online":
            test("socks5", "online", socks)
            def bootstrap():
                pending = list(nodes)
                deadline = time.monotonic() + args.bootstrap_timeout
                durations = {}
                while pending and time.monotonic() < deadline:
                    for n in pending[:]:
                        if n.bootstrap():
                            durations[n.name] = time.monotonic() - n.started
                            pending.remove(n)
                    time.sleep(1)
                if pending:
                    raise Blocked("public network bootstrap timeout; distinguish connectivity/censorship using Tor logs")
                metrics["bootstrap_seconds"] = max(durations.values())
                metrics["bootstrap_per_node_seconds"] = durations
            if not test("public_bootstrap", "online", bootstrap):
                raise Blocked("both public bootstraps are required for onion tests")
            idle_start = time.monotonic()
            time.sleep(args.idle_seconds)
            metrics.update(sampler.idle(idle_start))
            address = [None]
            def onion():
                path = nodes[1].hs / "hostname"
                wait_for(path.exists, 30, "onion hostname was not generated")
                address[0] = path.read_text().strip()
                if not re.fullmatch(r"[a-z2-7]{56}\.onion", address[0]):
                    raise AssertionError("service hostname is not v3")
                # Reachability, rather than the existence of a hostname, is the pass condition.
                start = time.monotonic()
                deadline = start + args.connection_timeout
                last = None
                while time.monotonic() < deadline:
                    try:
                        with socks_connect(nodes[0].socks, address[0], 80, min(30, max(1, deadline-time.monotonic()))) as s:
                            if read_frame(s) != SERVER_PAYLOAD:
                                raise AssertionError("wrong service identity fixture payload")
                        metrics["onion_connection_seconds"] = time.monotonic() - start
                        return
                    except (OSError, ConnectionError) as exc:
                        last = type(exc).__name__
                        time.sleep(1)
                raise Blocked("onion reachability timeout: " + str(last))
            if not test("onion_v3", "online", onion):
                raise Blocked("reachable v3 service required")
            def exchange(payloads):
                delivered = 0
                with socks_connect(nodes[0].socks, address[0], 80, args.connection_timeout) as s:
                    if hashlib.sha256(read_frame(s)).digest() != hashlib.sha256(SERVER_PAYLOAD).digest():
                        raise AssertionError("server-to-client push integrity failed")
                    start = time.monotonic()
                    for payload in payloads:
                        send_frame(s, payload)
                        echoed = read_frame(s)
                        if hashlib.sha256(payload).digest() != hashlib.sha256(echoed).digest():
                            raise AssertionError("bidirectional SHA-256 payload mismatch")
                        delivered += 2 * len(payload)
                    return delivered / max(0.000001, time.monotonic() - start)
            test("two_node", "online", lambda: exchange([b"embedded Tor two-node test"]))
            def integrity():
                rng = random.Random(734291)
                payloads = [b"", b"hello\x00\xff", bytes(range(256)), rng.randbytes(4096), rng.randbytes(1024*1024)]
                metrics["throughput_bytes_per_second"] = exchange(payloads)
            test("bidirectional_integrity", "online", integrity)
            def reconnect():
                for _ in range(3):
                    exchange([b"repeated connection"])
                nodes[1].stop(force=True)
                nodes[1].start()
                wait_for(nodes[1].ready, 45, "restarted service failed startup")
                with contextlib.closing(Control(nodes[1].control)) as events:
                    events.authenticate(nodes[1].data / "control_auth_cookie")
                    events.command("SETEVENTS HS_DESC")
                    wait_for(nodes[1].bootstrap, args.bootstrap_timeout, "restarted service bootstrap timeout")
                    if (nodes[1].hs / "hostname").read_text().strip() != address[0]:
                        raise AssertionError("service restart changed its identity")
                    deadline = time.monotonic() + args.connection_timeout
                    while time.monotonic() < deadline:
                        events.sock.settimeout(max(0.1, deadline - time.monotonic()))
                        try:
                            line = events.file.readline(65536).decode("ascii")
                        except TimeoutError:
                            raise Blocked("restarted onion descriptor publication timed out") from None
                        if not line:
                            raise AssertionError("service control connection closed during publication")
                        if " HS_DESC UPLOADED " in line and address[0].removesuffix(".onion") in line:
                            print("RECONNECT B: new descriptor uploaded", flush=True)
                            break
                    else:
                        raise Blocked("restarted onion descriptor publication timed out")
                def fresh_connection():
                    # Tor's supported recovery request purges stale HS descriptors
                    # and intro-point state after a service crash. Preserve keys.
                    with contextlib.closing(Control(nodes[0].control)) as ctl:
                        ctl.authenticate(nodes[0].data / "control_auth_cookie")
                        ctl.command("SIGNAL NEWNYM")
                    return retry_exchange(exchange)
                wait_for(fresh_connection, args.connection_timeout, "reconnection after service crash timed out")
            test("reconnection", "online", reconnect)
        else:
            idle_start = time.monotonic()
            time.sleep(args.idle_seconds)
            metrics.update(sampler.idle(idle_start))
    except Blocked as exc:
        for name in OFFLINE + ONLINE:
            if not any(t["id"] == name for t in rows):
                rows.append({"id": name, "mode": "online" if name in ONLINE else "offline",
                             "status": "BLOCKED", "detail": str(exc), "seconds": 0})
    finally:
        if sampler:
            sampler.stop()
            metrics.update(sampler.metrics())
        for n in nodes:
            n.stop()
        if server:
            server.shutdown()
            server.server_close()
    for name in ONLINE:
        if not any(t["id"] == name for t in rows):
            rows.append({"id": name, "mode": "online", "status": "SKIPPED",
                         "detail": "offline mode selected; public Tor behavior remains unverified", "seconds": 0})
    metrics["persistent_storage_bytes"] = sum(p.stat().st_size for n in nodes for p in n.root.rglob("*")
                                               if p.is_file() and p.suffix != ".log" and p.name not in ("torrc", "defaults-torrc")) if nodes else None
    # Include diagnostics only. Never export service keys, cookies or raw DataDirectory files.
    logs = {n.name: {p.name: p.read_text(encoding="utf-8", errors="replace")[-32768:]
                     for p in n.root.glob("*.log")} for n in nodes}
    version = subprocess.check_output([tor, "--version"], text=True).strip() if tor else None
    per_node = {}
    if sampler:
        for n in nodes:
            rows_for_node = [s["processes"][n.name] for s in sampler.samples if n.name in s["processes"]]
            per_node[n.name] = {"peak_rss_bytes": max((p.get("VmHWM", 0) for p in rows_for_node), default=None),
                                "peak_pss_bytes": max((p["Pss"] for p in rows_for_node if p.get("Pss") is not None), default=None)}
    return {"schema_version": 1, "tests": rows, "metrics": metrics, "logs": logs,
            "tor_version_output": version, "sampling_interval_seconds": 0.2,
            "kernel_version": platform.release(), "architecture": platform.machine(),
            "per_node_memory": per_node}


def retry_exchange(exchange):
    try:
        exchange([b"restart integrity"])
        return True
    except (OSError, ConnectionError):
        return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=("offline", "online"), default="offline")
    parser.add_argument("--tor", default="tor")
    parser.add_argument("--template", type=Path, default=Path(__file__).resolve().parents[1] / "br2-external/board/qemu/torrc.in")
    parser.add_argument("--work-dir", type=Path, default=Path("runtime") / str(time.time_ns()))
    parser.add_argument("--output", type=Path, default=Path("reports/test_results.json"))
    parser.add_argument("--bootstrap-timeout", type=float, default=600)
    parser.add_argument("--connection-timeout", type=float, default=180)
    parser.add_argument("--idle-seconds", type=float, default=10)
    args = parser.parse_args()
    try:
        result = run(args)
    except Exception as exc:
        result = {"schema_version": 1, "metrics": {}, "logs": {}, "tests": [
            {"id": name, "mode": "online" if name in ONLINE else "offline", "status": "FAIL",
             "detail": "harness failure: " + type(exc).__name__ + ": " + str(exc), "seconds": 0}
            for name in OFFLINE + ONLINE]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8")
    print(" ".join(t["id"] + "=" + t["status"] for t in result["tests"]), flush=True)
    return 0 if all(t["status"] == "PASS" for t in result["tests"]) else 1


if __name__ == "__main__":
    raise SystemExit(main())
