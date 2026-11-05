#!/usr/bin/env python3
"""Build a new test-only native observer disk; refuses to replace existing work."""
import argparse
from pathlib import Path
import subprocess
from common import ROOT,sha256,write_json,git_metadata


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--compiler',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True,help='new fixture directory; disk has .ext4 suffix')
    args=p.parse_args();directory=args.output.resolve();disk=Path(str(directory)+'.ext4')
    if directory.exists() or disk.exists():p.error('choose a fresh output path; fixtures are immutable')
    directory.mkdir(parents=True)
    flags=['-O2','-Wall','-Wextra','-Werror','-fstack-protector-strong','-fPIE','-pie','-Wl,-z,relro,-z,now']
    sources={'native_agent.c':'native-agent','crypto_capacity.c':'crypto-capacity','crypto_probe.c':'crypto-probe'}
    for source,binary in sources.items():
        subprocess.run([str(args.compiler),*flags,str(ROOT/'tests'/source),'-lcrypto','-o',str(directory/binary)],check=True)
    manifest={'compiler':subprocess.check_output([str(args.compiler),'--version'],text=True).splitlines()[0],
              'flags':flags,'git':git_metadata(),'sources':{s:sha256(ROOT/'tests'/s) for s in sources},
              'executables':{b:sha256(directory/b) for b in sources.values()},
              'purpose':'test-only observer/backend; never installed in production; SAFECOOKIE and private identities remain in guest'}
    write_json(directory/'source-manifest.json',manifest)
    with disk.open('xb') as f:f.truncate(16*1024**2)
    subprocess.run(['mke2fs','-q','-F','-t','ext4','-d',str(directory),str(disk)],check=True)
    write_json(Path(str(directory)+'.json'),{'path':str(disk),'sha256':sha256(disk),'manifest':manifest})
    print(disk,sha256(disk))


if __name__=='__main__':main()
