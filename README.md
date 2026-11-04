# Tor Client for Embedded Systems

**Work in progress.** The Tor optimization laboratory below is implemented;
the complete embedded image and physical hardware still require validation.

Repository: [JozefKovalcin/Tor-Client-for-embedded-systems](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems).

The separate [Nym mixnet client project](https://github.com/JozefKovalcin/Nym-MixNet-Client-for-embedded-systems)
has its own repository and validation status.

This repository implements reproducible Buildroot builds, real target Tor tests,
resource measurements, immutable baseline storage, and regression decisions.
The supplied workspace was empty. No original Windows distribution or working
Linux image was available to preserve. No baseline or hardware measurement has
been invented.

The reference image is an **x86_64 QEMU validation platform**, with musl, Tor,
OpenSSL, libevent, zlib, BusyBox, and a Python test harness. Python is deliberately
included in this development image; this is not a claimed minimum production
footprint. The image boots a kernel and ext4 root filesystem directly in QEMU.
Tor remains inactive unless validation is selected on the kernel command line.

On a Linux host, in a path **without spaces**, install prerequisites (Debian/Ubuntu):

```sh
sudo apt-get update
sudo apt-get install build-essential bc bison flex cpio rsync unzip wget file \
  python3 git patch libncurses-dev libssl-dev qemu-system-x86 shellcheck
python3 scripts/check.py
python3 scripts/check_buildroot.py
```

Buildroot and its source archives are downloaded and checked against the pinned
SHA-256 values automatically. Use a normal user account. Reserve at least 40 GiB
free disk and 6 GiB RAM as an initial build-host budget, not a measured target
minimum. QEMU must support machine `pc-i440fx-8.2` (QEMU 8.2 or later).

First establish the baseline from a clean committed source tree:

```sh
./scripts/validate.sh --record-baseline --change "Initial working Linux baseline"
```

This builds the complete image, checks artifacts and ELF hardening, boots the
target, runs both Tor nodes through public bootstrap and v3 onion communication,
collects measurements, and exclusively creates `reports/baseline/`. The script
refuses skipped mandatory tests, missing measurements, dirty source trees and
existing baselines. A failed first build leaves reports, not a fake baseline.

Then change one configuration and run:

```sh
./scripts/validate.sh --change "Describe the single optimization"
python3 scripts/compare.py baseline reports/benchmarks/<run-id>
```

Exit 0 means `BASELINE` or `ACCEPTED`. Exit 1 means `INCOMPLETE`, `REJECTED`,
`NON_COMPARABLE`, build failure or blocked prerequisites. Invalid CLI/comparison
input and an already-running validation return nonzero as well. The default is
online mode. `--mode offline` still builds and boots, but cannot accept an
optimization. `--checks-only` produces an incomplete report and exits nonzero;
use `python3 scripts/check.py` for lightweight CI success/failure checks.

Read [the workflow guide](docs/OPTIMIZATION.md) for metric definitions,
baseline preservation, adding hardware profiles, and source-lock maintenance.
[Current verified state](docs/PROGRESS.md) records the local execution limits.

Generated outputs:

- `reports/latest.md` and `reports/optimization-history.csv` summarize decisions.
- `reports/benchmarks/<run-id>/` contains JSON, CSV, per-run Markdown, resolved
  configs, source provenance, artifact hashes, commands and failure logs.
- `reports/test_results.json` and `reports/test_summary.md` summarize tests.
- `build/embedded/<profile>/<input-hash>/` isolates complete build outputs.

The GitHub workflow always runs lightweight tests and actual Kconfig resolution.
Full public-network builds are manually dispatched and resource gated. A local
self-hosted Linux runner is supported; its setup is documented in the guide.
Windows can run the portable checks and the standalone Tor harness when a real
Tor executable is supplied. Buildroot itself requires Linux. This Windows path
contains a space, so clone into a Linux path such as `~/embedded-tor` for builds.
