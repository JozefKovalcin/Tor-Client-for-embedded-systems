"""Exercise real local sockets and reject unsafe Tor configuration changes."""
import contextlib
from pathlib import Path
import socket
import sys
import threading
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
from echo_server import SERVER_PAYLOAD, read_frame, receive, send_frame, start_server
from run_all import check_security, socks_connect

SAFE = """CookieAuthentication 1
CookieAuthFileGroupReadable 0
ClientOnly 1
SafeLogging 1
RunAsDaemon 0
SocksPort 127.0.0.1:9050
ControlPort 127.0.0.1:9051
"""


class ProtocolTests(unittest.TestCase):
    def test_configuration_checks(self):
        check_security(SAFE)
        for text in (SAFE.replace("SafeLogging 1", "SafeLogging 0"),
                     SAFE.replace("127.0.0.1:9051", "0.0.0.0:9051"),
                     SAFE + "ControlPort 9052\n", SAFE + "ORPort 9001\n",
                     SAFE.replace("CookieAuthentication 1", "CookieAuthentication 0")):
            with self.subTest(config=text), self.assertRaises(AssertionError):
                check_security(text)

    def test_actual_bidirectional_fixture(self):
        server = start_server()
        try:
            with socket.create_connection(server.server_address, timeout=5) as sock:
                self.assertEqual(read_frame(sock), SERVER_PAYLOAD)
                for payload in (b"", b"\x00\xffbinary", bytes(range(256))*4096):
                    send_frame(sock, payload)
                    self.assertEqual(read_frame(sock), payload)
        finally:
            server.shutdown()
            server.server_close()

    def test_socks_onion_transmitted_as_domain_name(self):
        address = "a" * 56 + ".onion"
        observed = []
        errors = []
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            def server():
                try:
                    with listener.accept()[0] as sock:
                        sock.settimeout(5)
                        observed.append(receive(sock, 3))
                        sock.sendall(b"\x05\x00")
                        observed.append(receive(sock, 5))
                        observed.append(receive(sock, len(address) + 2))
                        sock.sendall(b"\x05\x00\x00\x01\x00\x00\x00\x00\x00\x00")
                except Exception as exc:
                    errors.append(exc)
            worker = threading.Thread(target=server, daemon=True)
            worker.start()
            # Only SOCKS loopback can be resolved. A DNS lookup of the onion name fails this test.
            real_getaddrinfo = socket.getaddrinfo
            def guard(host, *args, **kwargs):
                self.assertEqual(host, "127.0.0.1")
                return real_getaddrinfo(host, *args, **kwargs)
            with patch("socket.getaddrinfo", side_effect=guard):
                with socks_connect(listener.getsockname()[1], address, 80, 5):
                    pass
            worker.join(timeout=10)
            self.assertFalse(worker.is_alive())
            self.assertEqual(errors, [])
            self.assertEqual(observed[0], b"\x05\x01\x00")
            self.assertEqual(observed[1][:4], b"\x05\x01\x00\x03")
            self.assertEqual(observed[2][:-2], address.encode())

    def test_invalid_onion_rejected_before_connection(self):
        with patch("socket.create_connection") as connect:
            with self.assertRaises(ValueError):
                socks_connect(9050, "invalid.onion", 80, 1)
            connect.assert_not_called()


if __name__ == "__main__":
    unittest.main()
