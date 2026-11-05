#!/usr/bin/env python3
"""Pinned public host peer; private identities remain in ignored runtime/."""
import argparse
import os
from pathlib import Path
import shlex
import subprocess
import sys
import time
from common import ROOT,write_json,sha256,utcnow
sys.path.insert(0,str(ROOT/'tests'))
from run_all import Node,Control,socks_connect
from echo_server import start_server
from capacity_native import wait_until

FULL=ROOT/'build/embedded/qemu-x86_64-fulltor/190f8d9b05e2c3b823a6'

class Peer:
    def __init__(self,data):self.data=data
    @property
    def hostname(self):return Path(self.data['hostname_file']).read_text().strip()
    @property
    def private_lines(self):return None
    def control(self):
        c=Control(self.data['control']);c.authenticate(Path(self.data['cookie']));return c
    def connect(self,hostname,timeout=180):return socks_connect(self.data['socks'],hostname,80,timeout)

def serve(directory):
    directory=directory.resolve();directory.mkdir(parents=True,exist_ok=False);directory.chmod(0o700)
    wrapper=directory/'tor';loader=FULL/'target/lib/libc.so'
    libraries=str(FULL/'target/lib')+':'+str(FULL/'target/usr/lib')
    wrapper.write_text('#!/bin/sh\nexec '+shlex.join([str(loader),'--library-path',libraries,str(FULL/'target/usr/bin/tor')])+' "$@"\n');wrapper.chmod(0o755)
    server=start_server();nodes=directory/'nodes';nodes.mkdir(mode=0o700)
    peer=Node('peer',nodes,str(wrapper),(ROOT/'br2-external/board/qemu/torrc.in').read_text(),False,server.server_address[1])
    try:
        peer.start();wait_until(peer.ready,45,'host public peer startup');wait_until(peer.bootstrap,600,'host public bootstrap')
        state={'created':utcnow(),'owner_pid':os.getpid(),'public':{'socks':peer.socks,'control':peer.control,'cookie':str(peer.data/'control_auth_cookie'),'hostname_file':str(peer.hs/'hostname')},'provenance':{'tor_sha256':sha256(FULL/'target/usr/bin/tor'),'libcrypto_sha256':sha256(FULL/'target/usr/lib/libcrypto.so.3'),'version':subprocess.check_output([str(wrapper),'--version'],text=True).strip(),'production_config_changed':False}}
        write_json(directory/'session.json',state);print('CAPACITY_PEERS_READY',directory/'session.json',flush=True)
        while not (directory/'stop').exists():time.sleep(.5)
    finally:peer.stop();server.shutdown();server.server_close()

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--directory',type=Path,required=True);args=parser.parse_args();serve(args.directory)
