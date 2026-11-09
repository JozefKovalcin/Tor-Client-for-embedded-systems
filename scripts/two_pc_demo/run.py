#!/usr/bin/env python3
"""Run a disposable two-PC capacity32 echo test."""
import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import sys
import threading
import time

HERE = Path(__file__).resolve().parent
IMAGES = {
    "zImage": "7ece8439f6adc4c06960aa475cf1d6e08d3e64d6772dee56ee7273bd1bbe5e63",
    "disk.img": "688786b1ea3198249cc6af5202d437fcb683872f46dc103d917b8a5ac4972f5c",
}
TOR_SHA256 = b"8430a2d79d3b74620ea9629b9487411ee444cd44ffb31cf9bd8686a9fd2d5caf"
ONION = r"[a-z2-7]{56}\.onion"
PROMPT = rb"(?:^|\n)[/~] # "
HASH = rb"(?:^|\n)([a-f0-9]{64})[ \t]+/usr/bin/tor\r?\n"
READY = rb"(?:^|\n)CAPACITY32_BOOTSTRAP_OK\r?\n"
GUEST_INIT = """mount -t proc proc /proc &&
mkdir -p /dev/pts /dev/shm &&
mount -a &&
mkdir -p /run/lock/subsys &&
hostname -F /etc/hostname &&
/etc/init.d/rcS &&
mkdir -p /run/tor /var/lib/tor &&
chown tor:tor /run/tor /var/lib/tor &&
chmod 0700 /run/tor /var/lib/tor &&
printf '\\n' &&
sha256sum /usr/bin/tor"""
TOR_COMMAND = (
    "/usr/bin/tor --defaults-torrc /etc/tor/defaults-torrc -f /etc/tor/torrc"
    " --Log 'notice file /run/tor/demo.log'"
)


def say(message):
    print(message, flush=True)


def verify_images():
    for name, expected in IMAGES.items():
        with (HERE / name).open("rb") as file:
            actual = hashlib.file_digest(file, "sha256").hexdigest()
        if actual != expected:
            raise ValueError(f"{name} nemá očakávaný SHA-256. Rozbaľ balík znova.")


class Guest:
    def __init__(self, proxy_port=None):
        net = "user,id=n0"
        if proxy_port is not None:
            net += f",hostfwd=tcp:127.0.0.1:{proxy_port}-:9050"
        self.command = [
            "qemu-system-arm", "-machine", "virt-8.2,highmem=off", "-cpu", "cortex-a7",
            "-accel", "tcg,thread=single", "-m", "256", "-smp", "1",
            "-nographic", "-monitor", "none", "-no-reboot", "-kernel", "zImage",
            "-drive", "file=disk.img,if=virtio,format=raw,snapshot=on",
            "-append", "root=/dev/vda1 ro console=ttyAMA0,115200 init=/bin/sh tor_validation_external=1",
            "-netdev", net, "-device", "virtio-net-pci,netdev=n0", "-device", "virtio-rng-pci",
        ]
        self.updated = threading.Condition()
        self.output = b""
        self.finished = False

    def __enter__(self):
        self.process = subprocess.Popen(
            self.command, cwd=HERE, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, start_new_session=True,
        )
        self.reader = threading.Thread(target=self.read_console, daemon=True)
        self.reader.start()
        return self

    def __exit__(self, *_):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        self.reader.join()
        self.process.stdin.close()
        self.process.stdout.close()

    def read_console(self):
        try:
            while chunk := self.process.stdout.read1(8192):
                with self.updated:
                    self.output = (self.output + chunk)[-128 * 1024:]
                    self.updated.notify_all()
        finally:
            with self.updated:
                self.finished = True
                self.updated.notify_all()

    def wait(self, pattern, seconds, phase):
        deadline = time.monotonic() + seconds
        with self.updated:
            while True:
                match = re.search(pattern, self.output)
                if match:
                    return match
                remaining = deadline - time.monotonic()
                if self.finished or remaining <= 0:
                    tail = self.output[-400:].decode("utf-8", errors="replace")
                    tail = re.sub(ONION, "<onion>", tail)
                    # Keep only printable text from the serial console.
                    tail = "".join(c for c in tail if c.isprintable() or c == "\n")
                    raise RuntimeError(f"{phase}\nPosledný výstup QEMU\n{tail.strip()}")
                self.updated.wait(remaining)

    def send(self, command):
        self.process.stdin.write(command.encode("ascii") + b"\n")
        self.process.stdin.flush()

    def start(self, echo_port=None):
        self.wait(PROMPT, 60, "QEMU nespustil obraz ARMv7.")
        self.send(GUEST_INIT)
        if self.wait(HASH, 45, "Kontrola binárky v obraze zlyhala.").group(1) != TOR_SHA256:
            raise RuntimeError("Tor v QEMU nemá očakávaný SHA-256.")
        say("Obraz aj binárka Tor 0.4.9.14 (capacity32) overené. Čakám na bootstrap.")
        onion_command = ""
        if echo_port is None:
            options = "--SocksPort 10.0.2.15:9050"
        else:
            options = (
                "--SocksPort 0 --HiddenServiceDir /var/lib/tor/demo-onion "
                f"--HiddenServiceVersion 3 --HiddenServicePort '80 10.0.2.2:{echo_port}'"
            )
            onion_command = (
                "until [ -s /var/lib/tor/demo-onion/hostname ]; do sleep 1; done; "
                "printf '\\n%s\\n' \"$(cat /var/lib/tor/demo-onion/hostname)\"; "
            )
        self.send(
            f"{TOR_COMMAND} {options} && {{ {onion_command}"
            "until grep -Fq 'Bootstrapped 100%' /run/tor/demo.log; do sleep 1; done; "
            "printf '\\nCAPACITY32_BOOTSTRAP_OK\\n'; }"
        )
        self.wait(READY, 180, "Tor nedokončil bootstrap do troch minút.")
        say("Tor dosiahol Bootstrapped 100%.")
        if echo_port is not None:
            return self.wait(
                rb"(?:^|\n)(" + ONION.encode("ascii") + rb")\r?\n",
                5, "Tor nevypísal onion adresu.",
            ).group(1).decode("ascii")
        return None


class Echo(BaseHTTPRequestHandler):
    def setup(self):
        super().setup()
        self.connection.settimeout(15)

    def log_message(self, *_):
        pass

    def do_POST(self):
        size = self.headers.get("Content-Length", "")
        if self.path != "/probe" or not re.fullmatch(r"[0-9]{1,4}", size):
            self.send_error(400)
            return
        count = int(size)
        if not 1 <= count <= 4096:
            self.send_error(400)
            return
        payload = self.rfile.read(count)
        if len(payload) != count or not payload.startswith(b"capacity32:"):
            self.send_error(400)
            return
        self.send_response(200)
        self.send_header("Content-Length", str(count))
        self.end_headers()
        self.wfile.write(payload)
        say(f"PC B prijal a vrátil {count} bajtov.")


def serve():
    with ThreadingHTTPServer(("127.0.0.1", 0), Echo) as server:
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with Guest() as guest:
                onion = guest.start(echo_port=server.server_port)
                say(f"\nAdresa pre PC A\n{onion}\n")
                say("Nechaj toto okno otvorené. Po výsledku na PC A ukonči PC B klávesmi Ctrl+C.")
                while guest.process.poll() is None:
                    time.sleep(0.5)
                raise RuntimeError("QEMU na PC B sa nečakane ukončil.")
        finally:
            server.shutdown()
            thread.join()


def test(onion):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as port:
        port.bind(("127.0.0.1", 0))
        proxy_port = port.getsockname()[1]

    with Guest(proxy_port) as guest:
        guest.start()
        with socket.create_connection(("127.0.0.1", proxy_port), timeout=5) as proxy:
            proxy.sendall(b"\x05\x01\x00")
            if proxy.makefile("rb").read(2) != b"\x05\x00":
                raise RuntimeError("SOCKS5 v obraze neodpovedá podľa očakávania.")

        payload = b"capacity32:" + secrets.token_hex(24).encode("ascii")
        say("SOCKS5 funguje. Skúšam prenos cez verejný Tor.")
        for attempt in range(1, 4):
            result = subprocess.run(
                [
                    "curl", "-q", "--silent", "--show-error", "--fail",
                    "--connect-timeout", "45", "--max-time", "60",
                    "--noproxy", "", "--socks5-hostname", f"127.0.0.1:{proxy_port}",
                    "--data-binary", "@-", f"http://{onion}/probe",
                ],
                input=payload, capture_output=True, timeout=65,
            )
            if result.returncode == 0:
                if result.stdout != payload:
                    raise RuntimeError("PC B vrátil odlišné dáta.")
                say(f"PASS: PC A → PC B → PC A. Všetkých {len(payload)} bajtov sa zhoduje.")
                return
            if attempt < 3:
                say(f"Pokus {attempt}/3 bez úspechu. Ďalší pokus o 10 sekúnd.")
                time.sleep(10)
        error = re.sub(ONION, "<onion>", result.stderr.decode(errors="replace")).strip()
        raise RuntimeError(f"PC B neodpovedal po troch pokusoch. {error}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--role", choices=("A", "B"))
    parser.add_argument("--onion", help="Adresa PC B pre rolu A.")
    args = parser.parse_args()
    say("Tor capacity32 | Test komunikácie medzi dvoma PC")
    role = args.role
    if role is None:
        say("A = odoslať test; B = spustiť Onion Service")
        while role not in ("A", "B"):
            role = input("Rola [A/B]: ").strip().upper()
    if args.onion and role != "A":
        parser.error("--onion patrí k role A.")

    onion = args.onion
    if role == "A" and onion is None:
        onion = input("Vlož adresu .onion z PC B: ")
    if role == "A":
        onion = onion.strip().lower()
        if not re.fullmatch(ONION, onion):
            raise ValueError("Očakávam celú v3 adresu z PC B vrátane prípony .onion.")
    for tool in ("qemu-system-arm", "curl"):
        if not shutil.which(tool):
            raise RuntimeError(f"Chýba {tool}. Spusť balík cez run-wsl.sh.")
    verify_images()
    say(f"PC {role}: spúšťam QEMU s obrazom ARMv7 capacity32.")
    if role == "B":
        serve()
    else:
        test(onion)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        say("\nTest ukončený.")
        sys.exit(130)
    except (EOFError, OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        say(f"FAIL: {error}")
        sys.exit(1)
