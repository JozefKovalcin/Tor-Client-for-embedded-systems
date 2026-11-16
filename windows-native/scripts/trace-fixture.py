"""Run the frozen workload with a read-only peek at its public fixture preface.

Accepts the same arguments as tests/run_all.py. Does not alter its assertions,
payloads or Tor configuration. Use a fresh --work-dir each time.
"""
from pathlib import Path
import socket
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tests'))
import echo_server

send_frame = echo_server.send_frame


def trace_send(sock, data):
    if data == echo_server.SERVER_PAYLOAD:
        print('FIXTURE SEND bytes=', len(data), 'header=', struct.pack('!I', len(data)).hex(), flush=True)
    return send_frame(sock, data)


echo_server.send_frame = trace_send
import run_all

read_frame = run_all.read_frame


def trace_frame(sock):
    # MSG_PEEK leaves all data for the original, unchanged frame assertion.
    prefix = sock.recv(4, socket.MSG_PEEK)
    print('FIXTURE CLIENT initial_four_bytes=', prefix.hex(), flush=True)
    return read_frame(sock)


run_all.read_frame = trace_frame
if __name__ == '__main__':
    raise SystemExit(run_all.main())
