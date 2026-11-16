# Build directly on Windows

## Current entry points

Run `COMPILE.cmd` in this directory after the initial MSYS2 installation/update.
It installs missing build dependencies and selects the optimized static profile.
The generated `dist/TEST.cmd` selects PC A/B and starts both Tor and the local
test server (8081). The interactive instructions are in README.md.

Create the compact source-and-test ZIP from the repository root:

```sh
python windows-native/scripts/create-clean-zip.py
```

The ZIP contains one short Slovak README and no firmware, executable, runtime
state or historical report tree. Its build downloads exact source archives.
For COMPILE.cmd, archives are in `%LOCALAPPDATA%\TorNativeBuild\downloads`;
extracted sources are in `<input-hash>/<run>/src` below the same build root.
When invoking build-windows.sh directly, its default root is `.work` instead.

## Clean environment

1. Install [MSYS2 x64](https://www.msys2.org/) to `C:\msys64`.
2. Open **MSYS2 UCRT64**. Run `pacman -Syu`, close/reopen the terminal if
   requested, then run `pacman -Syu` again until fully updated.
3. Install build dependencies:

```sh
pacman -S --needed base-devel make perl python curl tar patch diffutils \
  mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-pkgconf
```

4. Obtain the complete repository source (Git clone or source ZIP). Extract
   into a native Windows directory without spaces, such as `C:\src\tor`.
   For Git, use `git -c core.autocrlf=false clone
   https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems.git C:/src/tor`.
   Git itself is optional after obtaining the source. No CMake is required.

Python 3.12+ is required for verified source extraction; the current MSYS2
`python` package supplies it. The [MSYS2 environment documentation](https://www.msys2.org/docs/environments/)
explains UCRT64 and its `/ucrt64/bin:/usr/bin` path. The script rejects the
wrong shell/compiler and reports missing tools before downloading sources.

## One build command

```sh
cd /c/src/tor
bash windows-native/build-windows.sh
```

For the existing checkout whose directory contains a space, keep compiler
outputs in a separate directory without spaces instead:

```sh
cd "/c/Users/jozef/Downloads/Tor Klient"
TOR_WINDOWS_WORK=/c/Users/jozef/tor-native-build bash windows-native/build-windows.sh
```

Alternatively from cmd.exe: `windows-native\build-windows.cmd`. Set
`MSYS2_ROOT` first if MSYS2 is elsewhere. `build.sh` is an alias.

Downloads are HTTPS-only, checked against pinned SHA-256 values on every
build; source license files are checked too. The original generated Tor
`configure` and Makefile are retained. No autoconf regeneration is needed.
The required Windows ordering patch is hash-checked and applied with zero fuzz;
the affected source file is hash-checked before and after application. Sources remain available
in each build's `src` directory for inspection and modification.

Configuration variables (set before the command):

| Variable | Default | Purpose |
| --- | --- | --- |
| `JOBS` | `nproc` | Parallel compiler jobs; reduce for low RAM |
| `TOR_WINDOWS_WORK` | `windows-native/.work` | Native Windows build path in MSYS notation; no spaces |
| `TOR_WINDOWS_CACHE` | `$TOR_WINDOWS_WORK/downloads` | Verified source archive cache |
| `LINKAGE` | `static` | `static` or `shared` source-built dependencies |
| `PROFILE` | `optimized` | `reference` is an explicit full-module measurement control |

Source/config/toolchain/package inventory selects an input-hash directory.
Every invocation uses a new timestamp/PID directory below it: no incompatible
objects are reused and failed logs remain. An existing distribution is renamed,
never recursively deleted. Do not place writable Tor state in a build tree.

The default release flags are `-O2 -g0 -ffunction-sections -fdata-sections
-fstack-protector-strong -fno-ident` plus a build-path prefix map. PE links use
`--gc-sections --dynamicbase --nxcompat --high-entropy-va
--no-insert-timestamp` and `-static-libgcc`. Tor's supported compiler hardening
checks remain enabled. The final package strips unneeded symbols, verifies
PE32+/AMD64, ASLR/DEP/high-entropy VA on every executable/DLL and rejects any
missing DLL or MSYS/Cygwin runtime. The package includes upstream licenses.

Build logs, compiler version, `pacman -Q`, `orconfig.h`, full Tor Makefile,
`config.log`, source manifest and package hashes are kept under the run path
printed by the build. Dependencies cannot come from installed MSYS2 OpenSSL,
zlib or libevent packages: only the private source-built prefix is configured.

## Clean rebuild and size comparisons

Every build command is clean. To also redownload sources use a fresh cache:

```sh
TOR_WINDOWS_WORK=/c/src/tor-build-clean bash windows-native/build-windows.sh
LINKAGE=shared bash windows-native/build-windows.sh
PROFILE=reference bash windows-native/build-windows.sh
```

The second and third commands produce `dist-optimized-shared` and
`dist-reference-static`. Compare `package-report.json` values for Tor, DLLs
and complete package; repeat with the same package/toolchain inventory.
`reference` enables relay/dirauth only when explicitly requested for comparison;
it is never the default. No performance threshold or historical rejection is
changed. Fixed sources and recorded inputs support procedural reproducibility;
bit-identical output across different MSYS2 revisions is not claimed. Retain
the exact `pacman -Q` inventory and signed package cache to recreate a toolchain.

## Native Windows validation

PowerShell (5.1+; included in Windows), from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File windows-native\scripts\smoke-test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File windows-native\scripts\smoke-test.ps1 -Online
```

The smoke script restricts DLL lookup PATH to the package and Windows,
executes `--version`, `--help`, validates both shipped profiles, rejects bad
configuration, creates writable state, authenticates control, checks clean
shutdown/restart and records Windows working-set memory after ten idle seconds. `-Online` additionally
waits for 100% public bootstrap and uses curl SOCKS hostname resolution to
check the public Tor exit. Results and logs stay in ignored `runtime/`.
Offline results mark network checks SKIPPED and do not imply full validation.

For the original nine-function workload, install native Python on the build
PC only (or `pacman -S mingw-w64-ucrt-x86_64-python`). From cmd.exe:

```bat
python -m unittest discover -s windows-native\tests -v
python tests\run_all.py --tor windows-native\dist\tor.exe --mode online --work-dir windows-native\runtime\online-01 --output windows-native\runtime\online-01.json
```

To run those same assertions with native Windows resident-memory sampling:

```bat
python windows-native\scripts\test-native.py --tor windows-native\dist\tor.exe --mode online --work-dir windows-native\runtime\native-01 --output windows-native\runtime\native-01.json
```

This adapter replaces only `/proc` observation with `GetProcessMemoryInfo`.
The RSS/HWM fields then represent Windows working-set resident pages; two-node
totals sum both processes. PSS and unavailable system/CPU metrics remain null.
Do not compare these samples directly with the ARM Linux measurements.

Use an absolute path for `--tor` if running from a different directory.
The unchanged workload checks SAFECOOKIE, SOCKS5 domain-name requests,
public bootstrap of two instances, v3 onion reachability, bidirectional
SHA-256 payload integrity and crash/restart with the same onion identity.
The harness exits nonzero if any mandatory test is SKIPPED, FAIL or BLOCKED.
It records bootstrap, onion connection latency and throughput; its Linux
`/proc` memory sampler has no native Windows RSS/PSS data, which must remain
null. The PowerShell script separately measures Windows working sets.
Runtime directories contain secrets: retain locally, never commit them.
DNS tests demonstrate domain-name delegation in SOCKS; packet-capture proof
of zero host DNS traffic is a separate qualification step.

The unpatched build failed the server-first onion fixture because DATA was
queued before CONNECTED. The Windows source patch fixes that ordering. Run
the additional non-consuming frame diagnostic using the same arguments with
`python windows-native/scripts/trace-fixture.py`. No assertion is relaxed.
See `evidence/README.md` for executed patched results, retained failures and limitations.

No WSL, Wine, QEMU or Linux compilation command is used by this pipeline.
The existing `check_buildroot.py` and image validator require Linux and are
separate from native Windows qualification.
