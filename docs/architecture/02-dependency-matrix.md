# 02 — Dependency and environment matrix

Status: Phase 0 inventory plus ASSUMPTION for future components. No packages
installed, downloaded SDKs or source locks changed. Repository references:
[README](../../README.md), `config/sources.lock.json`, `config/upstream/`,
`config/profiles/qemu-{arm,x86_64}-capacity32.json`,
`windows-native/{BUILD.md,source-manifest.json,build-windows.sh,scripts/compile.sh}`.
See [audit](00-repository-audit.md) for executed checks and host observations.

Ownership: **bundled** = shipped in verified run package; **app-local** =
isolated verified cache/download after opt-in; **package-manager** = reviewed
consent before changes; **OS/manual** = privileged/manual setup (possibly
reboot); **hardware** = physical equipment/provisioning. A version listed in
historical evidence is not proof the current machine has it installed.

## 1. Run Only — portable Windows x64 / Linux x64

| Dependency / capability | Version and compatibility evidence | Ownership / detection / status |
| --- | --- | --- |
| Avalonia GUI + .NET runtime | NOT IMPLEMENTED. Exact mutually compatible SDK/TFM/Avalonia versions UNVERIFIED; pin in Phase 1 before restore/publish. Separate `win-x64` and `linux-x64` packages proposed. | Bundled self-contained runtime; no installed SDK, MSYS2, WSL, Python or compiler required for normal run mode. |
| Native Tor runtime and dependencies | Windows pinned Tor 0.4.9.14, OpenSSL 3.6.5, libevent 2.1.13, zlib 1.3.2; existing static package still needs `libwinpthread-1.dll`. Linux host package NOT IMPLEMENTED. | Bundled, artifact/license manifest and recursive dependency check. Build-time source patch provenance must accompany Windows package. |
| Windows system/UCRT APIs | Native build historically tested on Windows 11 24H2/26100. Windows 10 NOT RUN. GUI support matrix must be pinned separately. | OS/manual; detect architecture/build, reject unsupported combination rather than promise universal portability. |
| Linux native libraries/display | glibc/X11 and font/native rendering libraries depend on selected Avalonia/.NET version. Avalonia currently lists X11 packages `libx11-6`, `libice6`, `libsm6`, `libfontconfig1`; exact chosen release closure UNVERIFIED. | OS/manual or package-manager with consent. Verify native imports on selected supported distro; self-contained .NET does not bundle the entire OS. |
| Private writable data, credentials, local ports | Per-user protected state/cache/log directory; separate local-Tor and device identities. No inherited host torrc, insecure shared writable directory or wildcard control listener. | App-local writable state, OS-protected credentials; preflight permission/port collision checks. Never ship pre-created identities. |
| Embedded mode hardware path | Compatible paired endpoint, USB role/driver and Ethernet/time on device | Hardware + OS/manual; BLOCKED until [03](03-hardware-yocto-plan.md) gates pass. Run-only PC does not need a firmware compiler. |

Microsoft documents that [self-contained publishing includes the .NET runtime](https://learn.microsoft.com/en-us/dotnet/core/deploying/).
[Avalonia's platform matrix](https://docs.avaloniaui.net/docs/supported-platforms)
also distinguishes Windows/Linux support and native Linux dependencies. A musl
Buildroot target is not a drop-in desktop GUI host; the proposed GUI runs on PCs.

## 2. Windows native source build — Developer

| Dependency / capability | Version / evidence | Ownership / remediation |
| --- | --- | --- |
| MSYS2 x64, UCRT64 environment | `MSYSTEM=UCRT64`, `/ucrt64/bin/gcc`, `x86_64-w64-mingw32`; enforced in `build-windows.sh`. Git Bash, MSYS/MINGW64/WSL are incompatible for this adapter. | OS/manual MSYS2 installation; package-manager updates with consent and possible terminal restart. Detect actual compiler target, not only executable name. |
| GCC/binutils/CRT | Historical executed inventory: GCC 16.2.0, binutils 2.47, MinGW-w64 CRT 14.0.0 (`windows-native/evidence/toolchain-packages.txt`). Current rolling package compatibility UNVERIFIED. | Package-manager, consent; record exact `pacman -Q`, signatures/package cache and compiler output in toolchain manifest. Never call these source-version pins. |
| Build tools | `base-devel make perl python curl tar patch diffutils`, `mingw-w64-ucrt-x86_64-gcc`, `mingw-w64-ucrt-x86_64-pkgconf`; shell also probes ar/ranlib/windres/objdump/nm/dlltool/sha256sum. Python >=3.12 enforced. | Package-manager with consent; no CMake required by this pipeline. Installation and source-build status remain separate. |
| Tor/OpenSSL/libevent/zlib | 0.4.9.14 / 3.6.5 / 2.1.13 / 1.3.2; `windows-native/source-manifest.json` archive/license/patch hashes; private source-built prefix. | App-local verified source cache. Do not substitute rolling MSYS2 crypto/event libraries. Hash mismatch blocks; detached signatures are not currently verified. |
| Work path and resource cap | Build outputs must be native Windows paths without spaces; COMPILE default `%LOCALAPPDATA%/TorNativeBuild`. Existing checkout may remain in its spaced path. Measured RAM/disk minimum UNVERIFIED; shell defaults JOBS to nproc. | App-local outputs, wizard sets bounded JOBS after resource probe; preserve failed runs and previous dist. |
| Test support | PowerShell >=5.1, native Python for frozen suite; curl for helper. Six source-pipeline unit tests run without Tor/MSYS2. | OS/manual or optional test tooling. Public tests only at explicit gates, never dependency detection. |

[MSYS2 environments](https://www.msys2.org/docs/environments/) explain UCRT64
versus other runtimes. Existing `scripts/compile.sh` auto-installs missing
packages with `--noconfirm`; future GUI must intercept this with an approved
install plan, then invoke the non-installing build adapter. Do not run
`COMPILE.cmd` as a probe.

## 3. Linux native source build — Developer

| Dependency / capability | Version / compatibility | Ownership / status |
| --- | --- | --- |
| Native Linux x86_64 host | Standalone host package/adapter NOT IMPLEMENTED. Selected v1 reference: Ubuntu 24.04 LTS x86_64-linux-gnu with glibc 2.39, separately from Buildroot musl target; record exact package/toolchain revisions. WSL compilation alone will not qualify a native Linux desktop runtime. | OS/manual Linux host or approved runner; execution on that selected host/ABI UNVERIFIED. |
| C compiler, GNU make, linker, binutils, Perl, shell, pkg-config | Exact compiler/libc versions UNVERIFIED. Preserve upstream configure probes and Linux hardening; never transfer PE flags. | Package-manager with consent; pin toolchain inventory before execution. |
| Python/download/archive tools | Proposed stdlib adapter uses Python >=3.12 to align safe source tooling; this is ASSUMPTION, not a current Linux gate. Need HTTPS client, tar and patch facilities. | Package-manager or app-local tool after explicit opt-in. Read-only version/target probes first. |
| Runtime sources | Propose reusing existing archive/license pins for Tor/OpenSSL/libevent/zlib, plus Linux seccomp policy review against `config/upstream/libseccomp.hash` (2.6.1 in handoff). Native source patch set must be explicit; Windows patch is not applied silently. | App-local verified sources and private prefix; feature/config compatibility UNVERIFIED until configure/build/test. |
| Packaging / deployment | ELF architecture, loader/dependency closure, PIE/RELRO/NOW/NX, licenses; package for selected runtime distro | App-local output. Build, offline runtime smoke and later unchanged online suite are separate gates. No measured footprint or performance predicted. |

## 4. Buildroot ARM/QEMU (and preserved x86 reference)

| Dependency / capability | Pinned/required state | Ownership / detection |
| --- | --- | --- |
| Linux host, native path without whitespace | `scripts/validate.py::check_host`; README initial host budget 6 GiB RAM/40 GiB free disk, not target minimum | OS/manual native Linux or WSL2; WSL enabling, virtualization and reboot require explicit setup. Windows filesystem mount is not the native Linux build path. |
| Host build packages | README: build-essential, bc, bison, flex, cpio, rsync, unzip, wget, file, python3, git, patch, libncurses-dev, libssl-dev, shellcheck, cmake; Perl/tar/bash also probed | Package-manager with consent. Exact versions generally not locked; record them. `check_buildroot.py` can fetch Buildroot if cache missing, so it is not unconditionally offline. |
| Buildroot / kernel / sources | 2026.08.1 / 6.18.7; `config/sources.lock.json`, upstream and custom kernel hashes | App-local verified downloads. Existing Linux signature-verification limitation retained. |
| ARM toolchain | Built from source: GCC 15.3.0, binutils 2.45.1, musl 1.2.6; `tor_qemu_arm_capacity32_defconfig` | App-local build output; hard-float ARMv7 ABI must match fixtures. Never use QEMU drivers/DTB as a board port. |
| x86 external SDK | capacity32 profile SHA-256 `5fd7659021ec80cf0bb47794be75776867270e672166210453acbaab89800e1e`; GCC 15.3.0, baseline commit recorded | App-local preserved archive `downloads/toolchains/...`; preinstalled output `build/toolchains/baseline-sdk`. BLOCKED if absent; do not silently replace. |
| Target packages | Tor 0.4.9.14, OpenSSL 3.6.5, libevent 2.1.13, zlib 1.3.2, libseccomp 2.6.1, BusyBox 1.38.0 | Source-built/pinned. Separate Python 3.14.8 fixture is not a capacity32 production runtime dependency. |
| QEMU | Machine compatibility >=8.2: `virt-8.2,highmem=off` for ARM, `pc-i440fx-8.2` for x86 | Package-manager or verified app-local host tool. `qemu-system-arm` in addition to x86 package. Host versions in archived studies differ; preserve version/TCG/CPU provenance. |
| Fixtures and references | ARM separate Buildroot fixture; x86 baseline and diagnostic fixture hashes in profile | Preserved app-local artifacts. Never download/regenerate a replacement baseline during detection. |

## 5. Yocto host build — Embedded

| Dependency / capability | Compatibility / evidence | Ownership / status |
| --- | --- | --- |
| Supported Linux host | TechNexion manifest README lists Ubuntu 22.04, 4 cores, 8/16 GB RAM, 250 GB storage, 16 GB swap. Yocto 6.0 quick build recommends >=32 GB RAM/140 GB free for its reference build. These are different workloads, not a guarantee. | OS/manual; provisional conservative scheduling budget in [04](04-task-dag-and-codex-execution.md). Exact BSP/host combination UNVERIFIED. WSL2 requires Linux ext4 path and a separate preflight; native Windows/MSYS2 cannot run BitBake. |
| Git/repo/BitBake/Python/host tools | Yocto 6.0 lists Git >=1.8.3.1, tar >=1.28, Python >=3.9, GCC >=10.1, GNU make >=4.0. Selected vendor layers may constrain more; actual versions BLOCKED until frozen manifest resolves. | Package-manager consent for compiler/tools/locale; app-local verified repo bootstrap. Never copy vendor README's HTTP repo download into product tooling; require HTTPS, reviewed digest/signature and license. |
| Host packages/locale | Yocto: build-essential, chrpath, cpio, diffstat, gawk, git, locales, Python helpers, socat, texinfo, unzip, wget, xz-utils, zstd and supporting OS packages; vendor list has additional BSP tools | Package-manager consent; derive exact list from frozen release, not an unchecked global install script. Test UTF-8 locale and supported distro first. |
| Vendor BSP + own layer | Candidate Wrynose 6.0 / `tn-wrynose_6.18.37-2.1.0_20261012`; not yet repo-locked or built. Details and carrier mismatch in [03](03-hardware-yocto-plan.md). | App-local vendor sources plus tracked future `yocto/meta-tor-minimal/`. Freeze all revisions; no AUTOREV. |
| Downloads / sstate / outputs | Separate caches/outputs for BSP revision, target tune, libc, config, compiler and profile; sufficient free space plus failure retention | App-local, opt-in source retrieval. EULA acceptance is explicit where applicable; no unnecessary GPU/Wi-Fi firmware in headless image. |

Sources checked 2026-11-16: [vendor manifest README](https://github.com/TechNexion/tn-imx-yocto-manifest),
[vendor release list](https://github.com/TechNexion/meta-tn-imx-bsp/releases),
[Yocto 6.0 quick build](https://docs.yoctoproject.org/6.0/brief-yoctoprojectqs/index.html).
Their host recommendations do not establish this machine's WSL resources.

## 6. PICO device runtime

| Dependency / capability | Required compatibility / uncertainty | Ownership / gate |
| --- | --- | --- |
| Exact SoM + carrier | PICO-IMX6UL/ULL; candidate 528 MHz / 512 MB DDR3L / 4 GB eMMC UNVERIFIED on any physical SKU here | Hardware; H0 in [03](03-hardware-yocto-plan.md). Different from DHCOM HI00096/picoITX/raw NAND. |
| Bootloader, kernel, DTB, eMMC | Vendor-supported boot chain and exact carrier routing; partitions/usable capacity/power-loss behavior BLOCKED | Hardware + OS/manual flashing only after reviewed layout/recovery/approval. Firmware must match board and preserve state. |
| Tor and libraries | Prefer pinned Tor/runtime sources from existing locks; vendor libc/kernel ABI and recipe compatibility UNVERIFIED | Bundled in baseline/minimal image, hash/license manifest. UID isolation, seccomp/hardening, source crypto/TLS/RNG preserved or capability gap blocks qualification. |
| Network, clock and entropy | Ethernet DHCP/link, valid UTC before Tor, initialized kernel CSPRNG; RTC/source and cold-boot behavior UNVERIFIED | Hardware/firmware; never bypass TLS, fake time, seed with predictable data or lower bootstrap security to pass. |
| USB/device agent | OTG device role, routed data+power, driver/Windows compatibility, paired auth, firewall, loopback Tor proxy | Hardware + firmware + optional OS driver setup with consent. No automatic public Ethernet management access. |
| State/update/recovery | Writable `/var/lib/tor`, agent credentials separate, protected ownership, bounded logs; two slots/recovery design pending | Firmware provisioning; storage sizing from actual peak writes and endurance tests, not compressed root size. |

## Wizard / machine-readable plan

ASSUMPTION: future `config/dependencies/manifest-v1.json` describes checks,
versions, architecture, hash/trust source, ownership and remediation; resolver
emits `DependencyCheck` records ([01](01-system-architecture.md)). Example
report fragment (schema illustration, **not** a detected measurement):

```json
{
  "schemaVersion": 1,
  "id": "yocto-board-dtb",
  "targetId": "pico-imx6ul-v1",
  "status": "BLOCKED",
  "requirement": "DTB must match confirmed SoM and carrier revision",
  "detectedVersion": null,
  "architecture": null,
  "requiredFor": ["embedded"],
  "optional": false,
  "owner": "hardware",
  "approval": "privileged-manual",
  "expectedSha256": null,
  "observedSha256": null,
  "reason": "No exact physical board and DTB evidence supplied",
  "remediationPlanRef": null,
  "evidenceRefs": [],
  "certainty": "BLOCKED"
}
```

TASK 01 discovery/preflight is read-only: no install, download or COMPILE.cmd
execution. W2 reports AVAILABLE/MISSING/INCOMPATIBLE/UNKNOWN/BLOCKED; a blocked
probe is distinct from absent or incompatible software. Yocto RAM is not a
prerequisite for W1–W3.

Wizard checks OS/arch and versions; CPU/RAM/free disk/cache and Linux limits;
SDK/compiler target; archives/licenses/hashes; actual board, USB, kernel/DTB,
Ethernet/time/entropy/storage. It shows detected/incompatible/unknown status,
optional versus required, app-managed versus manual, rationale, exact planned
commands/privileges/bytes/reboot and remediation before consent. No reliable
download length means indeterminate progress. Keep check results immutable and
re-probe after installation. Cancel/retry must retain logs and incomplete caches
without promoting them to trusted tools.
