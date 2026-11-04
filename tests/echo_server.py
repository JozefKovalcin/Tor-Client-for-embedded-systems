"""Length-framed bidirectional integrity fixture; loopback only."""
import hashlib
import socketserver
import struct
import threading

SERVER_PAYLOAD = hashlib.sha256(b"embedded-tor-server-push-v1").digest() * 2048


def receive(sock, count):
    data = bytearray()
    while len(data) < count:
        block = sock.recv(min(65536, count - len(data)))
        if not block:
            raise ConnectionError("unexpected EOF")
        data.extend(block)
    return bytes(data)


def send_frame(sock, data):
    sock.sendall(struct.pack("!I", len(data)) + data)


def read_frame(sock):
    length = struct.unpack("!I", receive(sock, 4))[0]
    if length > 4 * 1024 * 1024:
        raise ValueError("oversized fixture frame")
    return receive(sock, length)


class Handler(socketserver.BaseRequestHandler):
    def handle(self):
        self.request.settimeout(180)
        try:
            send_frame(self.request, SERVER_PAYLOAD)
            while True:
                data = read_frame(self.request)
                send_frame(self.request, data)
        except (ConnectionError, OSError, ValueError):
            return


class EchoServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def start_server():
    server = EchoServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server


if __name__ == "__main__":
    with EchoServer(("127.0.0.1", 17777), Handler) as server:
        server.serve_forever()
