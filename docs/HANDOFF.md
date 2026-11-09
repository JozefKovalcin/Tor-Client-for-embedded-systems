# Technical handover

## Mission and state

Build two independent embedded Linux Tor endpoints. Each Windows PC uses
USB to reach its local endpoint. Ethernet carries the endpoint's public Tor
connections. A client reaches the other endpoint's v3 Onion Service through
SOCKS5. Application TCP passes through the onion circuit, with private
identities and runtime state kept on the embedded endpoints. The USB gadget
transport, access policy and physical flash layout are board bring-up work.

The immutable x86 baseline is tagged `baseline` at
`da7c5281145589821ceeb4f6b7aabd4d98833587`. The x86 capacity work ends at
`fec5f291d41599acb3499f3f1639768a44388a78`. ARM source and evidence were
committed at `a7edcb8b96853f6d1fe39f4fc6a20f9bb61ff091`. The ARM run
manifest identifies the pre-commit working tree used during measurement.
Use `git log -1` for the current handover commit.

The [2026-11-09 synchronization audit](../reports/sync-2026-11-09/README.md)
aligns Windows, WSL and GitHub and preserves the earlier WSL work in a stash.
The current [Slovak executive report](../reports/executive/Tor_Executive_Report_SK.pdf)
retains the measured results; only its two-PC launcher note was updated.
Launcher sources and the image-checking packager are in `scripts/two_pc_demo/`
and `scripts/build_two_pc_demo.py`. Generated ZIPs and images remain outside Git.

## Software and configurations

| Component | Pinned version |
| --- | --- |
| Buildroot / Linux | 2026.08.1 / 6.18.7 |
| Tor / BusyBox | 0.4.9.14 / 1.38.0 |
| musl / GCC / binutils | 1.2.6 / 15.3.0 / 2.45.1 |
| OpenSSL / libevent / zlib / libseccomp | 3.6.5 / 2.1.13 / 1.3.2 / 2.6.1 |
| Python test fixture | 3.14.8, separate from capacity32 target |

Source pins and SHA-256 values live in `config/sources.lock.json`,
`config/upstream/` and `br2-external/patches/`. Under
`br2-external/configs/`, `tor_qemu_x86_64_defconfig` is the original
ext4 development baseline. Historical x86 variants include small, tiny,
production, compressed, balanced, fast, ram, tor-o2, fulltor,
fulltor-slim and selective. The current x86 and ARM capacity defconfigs
are `tor_qemu_x86_64_capacity32_defconfig` and
`tor_qemu_arm_capacity32_defconfig`. Corresponding run profiles are
`config/profiles/qemu-x86_64-capacity32.json` and
`config/profiles/qemu-arm-capacity32.json`. The x86 TCP tuning profile
reuses the x86 capacity32 image.

Both capacity32 builds share the BusyBox configuration in `board/qemu-ram/`,
production init and users in `board/qemu-production/`, read-only-root
overlay in `board/qemu-compressed/`, and Tor defaults and genimage layout
in `board/qemu-capacity32/` under `br2-external/`. The layout is an MBR
block image with a 1 MiB prefix, XZ SquashFS root and 32 MiB ext4 state
at `/var/lib/tor`. Native Tor runs as UID/GID 905 with a 32 MiB queue
setting. The ARMv7 hard-float build uses `zImage`, PL011, PL031 RTC,
VirtIO and `virt-8.2,highmem=off`. The x86 build uses `bzImage` and a
pinned external SDK. The ARM toolchain is built from source. Production
test fixtures are mounted separately and read-only.

## Verified results

| Boot set | Total MiB | Non-state MiB | State MiB |
| --- | ---: | ---: | ---: |
| Original x86 reference | 262.35 | N/A | Shared writable ext4 |
| Qualified historical x86 ram | 25.8799 | 9.8799 | 16 |
| x86 fulltor-slim | 24.7354 | 8.7354 | 16 |
| x86 capacity32 | 40.6768 | 8.6768 | 32 |
| ARMv7 capacity32 | 38.6198 | 6.6198 | 32 |

ARM Tor is 2,207,484 bytes and the libraries total 6,232,916 bytes. The
ARM kernel is 2,484,960 bytes and the SquashFS root is 3,407,872 bytes.
ARM passed the frozen nine online tests and native production check at
128 and 256 MiB. The paired native series ran three cold boots per
architecture and RAM budget. All 12 passed functions, state checks and
the 32 MiB resident reservation. At 128 MiB, minimum available RAM was
58.03 MiB on ARM and 46.07 MiB on x86. ARM idle PSS was 22.36 MiB vs
25.76 MiB. The ARM 128 MiB public Onion and transfer medians missed the
20% bounds. The x86 128 MiB group missed local TCP and bootstrap bounds.
Both 256 MiB groups passed the capacity screen. Cross-ISA comparisons
are descriptive. The global ARM comparison is NON_COMPARABLE against
the x86 baseline. Earlier slim and selective rejections remain in scope
for their specific references. Observed ENOSPC in the 16 MiB experiment
motivated the 32 MiB state volume.

## Resume and reproduce

Native WSL checkout used for measurement is `/home/jozef/embedded-tor`.
The Windows checkout is under `C:/Users/jozef/Downloads/Tor Klient`.
Before syncing, inspect both worktrees and preserve local changes and
stashes. The generated builds, downloads and full serial logs are local.
The Git clone carries the measured summaries, configuration snapshots,
artifact checksums and provenance, so expensive reruns are unnecessary
for the handover.

```sh
cd ~/embedded-tor
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
git status --short
python3 scripts/check.py
python3 scripts/check_buildroot.py
./scripts/validate.sh --profile qemu-arm-capacity32 --mode offline
./scripts/validate.sh --profile qemu-arm-capacity32 --mode online
# Existing x86 SDK, baseline and external fixtures are required for this profile
./scripts/validate.sh --profile qemu-x86_64-capacity32 --mode online
```

Install Linux Buildroot dependencies from the repository README and
`qemu-system-arm` for ARM. Offline output must report online tests as
SKIPPED. The validator returns nonzero for NON_COMPARABLE and rejected
runs. Inspect build status, executed tests and the comparison separately.
`scripts/compare.py` applies `config/policy.json`. Use the paired series
recipe in [ARM platform guide](ARM_PLATFORM.md) for native observer
build, host peer, repeated tests and analysis. `tests/run_all.py` is the
frozen functional suite. Keep the host peer directory and keys outside Git.

| Evidence | Location |
| --- | --- |
| Baseline and early images | [Archived runs](../reports/measured/2026-11-04/README.md) |
| x86 recovery gates | [Performance study](../reports/performance-2026-11-05/README.md) |
| State capacity and TCP controls | [Capacity study](../reports/capacity-2026-11-05/README.md) |
| ARM build, tests and paired series | [ARM report](../reports/arm-2026-11-05/README.md) |
| Hardware candidates | [Platform guide](ARM_PLATFORM.md) |
| Report and machine-readable dataset | [Executive package](../reports/executive/README.md) |

ARM full build `20261105T183526Z-c95d541a` and corrected validation
`20261105T190223Z-7002ec13` share the native output
`build/embedded/qemu-arm-capacity32/c93df292837d62737b66/images`.
The Windows copy is in `build/arm-capacity32/20261105T190223Z-7002ec13/`.
x86 capacity32 run `20261105T160654Z-f407769f` is preserved at
`build/iterations/12-capacity32/` in WSL. The original sealed baseline
report and boot artifacts remain in `reports/baseline/` and
`build/baseline/` in WSL.

## First physical milestone

Start with DHCOM i.MX6ULL HI00096 and the picoITX carrier (256 MiB RAM,
128 MiB raw NAND and separate boot SPI). Boot a board-specific kernel
and DTB from removable media. Capture the serial log, Ethernet DHCP,
time and entropy checks, then run public Tor bootstrap. Follow with
board-specific NAND and USB integration. The 128 MiB 100ASK T113s3-PRO
is the smaller research candidate. MYIR MYD-YF135 is the dual-Gigabit
fallback. Board drivers, gadget enumeration, Windows compatibility,
NAND power-loss recovery, flash endurance and long-duration performance
remain open. Use [ROADMAP](ROADMAP.md) for the qualification gates.
