#!/usr/bin/env python3
"""Inventory effective compiler, library, kernel and filesystem capacity inputs."""
import argparse
import difflib
from pathlib import Path
import re
import struct
from common import ROOT,read_json,write_json,sha256


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--matrix',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();args.output.mkdir(parents=True,exist_ok=False);inventory={};configs={}
    for name,(profile,images,rid) in read_json(args.matrix)['images'].items():
        result=read_json(ROOT/'reports/benchmarks'/rid/'results.json');out=ROOT/result['build']['output_directory']
        kernel=next((out/'build').glob('linux-*'));busybox=next((out/'build').glob('busybox-*'))
        files={'buildroot':out/'.config','kernel':kernel/'.config','busybox':busybox/'.config'}
        configs[name]={}
        for kind,path in files.items():
            configs[name][kind]=path.read_text();(args.output/(name+'-'+kind+'.config')).write_text(path.read_text())
        libs={}
        for directory in ('lib','usr/lib'):
            for path in (out/'target'/directory).glob('*'):
                if path.is_file() and not path.is_symlink() and '.so' in path.name:
                    libs[str(path.relative_to(out/'target'))]={'bytes':path.stat().st_size,'sha256':sha256(path)}
        flags={}
        for package in ('tor','libopenssl','libevent','zlib'):
            for makefile in (out/'build').glob(package+'-*/Makefile'):
                flags[package]=[s for s in makefile.read_text(errors='replace').splitlines() if re.match(r'^(?:CFLAGS|CFLAG|CXXFLAGS|LDFLAGS|CONFIGURE_ARGS)\s*[:?+]?=',s)]
        disk=ROOT/images/'disk.img'
        with disk.open('rb') as f:mbr=f.read(512)
        parts=[]
        for n in range(4):
            entry=mbr[446+n*16:462+n*16];start,sectors=struct.unpack_from('<II',entry,8)
            if sectors:parts.append({'number':n+1,'type':entry[4],'offset_bytes':start*512,'bytes':sectors*512})
        inventory[name]={'profile':profile,'source_build':rid,'build':result['build'],'metadata':result['metadata'],
                         'metrics':result['metrics'],'effective_makefile_flags':flags,'libraries':libs,
                         'images':{p.name:{'bytes':p.stat().st_size,'sha256':sha256(p)} for p in (ROOT/images/'bzImage',disk)},
                         'partitions':parts,'state_partition_included_in_complete_image':True}
    write_json(args.output/'inventory.json',inventory)
    for other in inventory:
        if other=='ram':continue
        for kind in ('buildroot','kernel','busybox'):
            diff=difflib.unified_diff(configs['ram'][kind].splitlines(True),configs[other][kind].splitlines(True),fromfile='ram/'+kind,tofile=other+'/'+kind)
            (args.output/('ram-'+other+'-'+kind+'.diff')).write_text(''.join(diff))
    print(args.output)


if __name__=='__main__':main()
