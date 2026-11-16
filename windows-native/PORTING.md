# Provenance and ARM feature mapping

## Forensics

The reference is `br2-external/configs/tor_qemu_arm_capacity32_defconfig`,
`br2-external/external.mk`, the package recipe inside pinned Buildroot
2026.08.1 (`34fb7bea8a213ffde39cc02e61703391560f26e0850f03138c722d0b09880f2b`),
and `reports/arm-2026-11-05/build-tor-actual-flags.txt`. The archived report
identifies run `20261105T183526Z-c95d541a` and its corrected validation.
The pinned Buildroot tar was inspected directly: its `package/tor` contains
`Config.in`, `tor.hash` and `tor.mk`, with no Tor C patch. The external patch
tree also supplies no Tor C patch. Thus the ARM reduction is configuration,
linking and packaging, not a custom fork of Tor's routing/crypto source.

`tor.mk` supplies `--disable-gcc-hardening --disable-unittests`, private
dependency directories, `--disable-systemd --disable-lzma --disable-zstd`
and ARM `--enable-seccomp`. Buildroot provides stack protector, PIE and
RELRO separately. `external.mk` adds `--disable-module-relay
--disable-module-dirauth`, O2, function/data sections and `--gc-sections`;
its post-configure hook removes diagnostic `-rdynamic`. Actual ARM flags
confirm `-O2 -g0 -ffunction-sections -fdata-sections` and ELF `-ztext
--gc-sections -z relro -z now`. Windows does not copy those ELF flags or
the musl diagnostic-export hook. Upstream probes decide supported flags.

`source-manifest.json` reproduces all four source hashes and license hashes
from `config/upstream/{tor,libopenssl,libevent,libzlib}.hash`. The zlib URL
uses upstream's release mirror, with the identical archive hash. No runtime
dependency version is silently taken from rolling MSYS2 packages. SHA-256
pinning inherits the existing HTTPS/hash trust boundary; detached release
signatures were not independently verified. This project does not claim
signature validation that did not occur.

## Feature matrix

| Feature | ARM capacity32 | Native Windows build |
| --- | --- | --- |
| Tor source | 0.4.9.14, upstream | Identical pinned archive plus one reviewed Windows ordering fix |
| Relay, bridge, directory authority | Compiled out | Same upstream module switches |
| SOCKS5, remote DNS, circuits, public bootstrap | Executed PASS in ARM QEMU | Executed PASS natively on Windows build 26100 |
| Onion client and v3 service endpoint | Executed PASS in ARM QEMU | Executed native PASS with ordering fix |
| Bidirectional payload integrity / same-identity reconnect | Executed PASS in ARM QEMU | Executed native PASS in both optimized variants |
| TLS, crypto, entropy, validation, routing | Upstream | Upstream Windows code; no security reductions |
| O2, section GC, symbol stripping | Enabled | Enabled with PE-compatible flags |
| Compression | zlib; no lzma/zstd | Same |
| OpenSSL/libevent/zlib | 3.6.5 / 2.1.13 / 1.3.2 | Same versions, compiled for UCRT64 |
| libevent OpenSSL helper | Built by Buildroot | Omitted: Tor uses its own TLS/OpenSSL integration |
| Library deployment | Shared ELF | Static default; shared comparison option |
| Queue setting | 32 MB | 32 MB; not an RSS ceiling |
| Control | Loopback, cookie authenticated | Off by default; tests enable authenticated loopback |
| SafeLogging / AvoidDiskWrites | Enabled | Enabled |
| State | `/var/lib/tor`, ext4 endpoint storage | Relative local Windows directories |
| Identity persistence | Executed ARM tests | Same local identity retained through the native restart test |
| Security hardening | ELF PIE/RELRO/NOW/stack protector | PE ASLR/DEP/high-entropy VA/stack protector |
| Seccomp / UID / syslog / init | Linux facilities | Not applicable; upstream Windows sockets, threads, RNG and console |
| Kernel / musl / SquashFS / NAND / USB gadget | Separate Linux platform | Not ported; no claim of embedded hardware qualification |

Upstream `configure.ac` describes `--disable-module-relay` as disabling relay,
bridge and authority; it implies `--disable-module-dirauth`. These compile-time
switches differ from runtime `ClientOnly 1`. Both are retained. Onion-service
hosting is not relay hosting and is not disabled by these module switches.

The historical **2,207,484-byte ARM executable** is a reference only. PE and
ELF imports, CRTs, architectures and static/shared deployment differ. Compare
optimized Windows with an explicitly built Windows reference under matching
toolchains/workloads. Compare Tor EXE and total dependencies separately. ARM
and Windows memory/performance are not equivalent-condition measurements.

## Patches and build systems

One Tor source patch is applied only by this Windows project:
`patches/0001-queue-connected-before-reading.patch`. Upstream Windows
paths are selected by the native MinGW compiler and upstream configure probes.
zlib uses its own `win32/Makefile.gcc`; OpenSSL uses `Configure mingw64`;
libevent and Tor retain Autotools. A CMake wrapper would add another dependency
without replacing any necessary step, so none is introduced. Future source
patches must be separate reviewed files, listed and hashed in the manifest,
with rationale, security effects, license and upstream compatibility.

The unpatched native build dropped the initial server greeting. Instrumented
fixture runs showed DATA reaching the client while its AP stream was still
in CONNECT_WAIT. The bytes were intact at relay decoding; this was ordering,
not cryptographic corruption. On Windows, socket read readiness can precede
the write-ready completion callback, leaving a greeting in `conn->inbuf`.
`connection_edge_finished_connecting()` set OPEN then enabled reads before
queuing CONNECTED. `connection_start_reading()` synchronously processes a
nonempty inbuf, so DATA went first and the client correctly rejected it.

The patch queues CONNECTED before calling `connection_watch_events()`. It
preserves normal successful-connect checks, relay message encoding, routing,
crypto, authentication and logging; it does not accept out-of-order DATA on
the client or discard assertions. This is a local fix for 0.4.9.14, not a claim
of upstream acceptance. The manifest pins the patch and exact source-file
preimage/postimage hashes. Linux/ARM builds do not apply it. No dependency C
patch is needed. Earlier failed native runs remain in the evidence history.

Build-only adjustments: explicit `LD`, `NM`, `OBJDUMP` and `DLLTOOL` names avoid
whitespace-sensitive compiler path discovery. Libevent receives explicit
Windows `iphlpapi`/`ws2_32` dependencies and private zlib include/library paths.
Its libtool import-library heuristic is set to `pass_all`; the actual PE linker
still validates symbols and ABI. This avoids the old heuristic rejecting
Windows system import libraries under a relocated toolchain. zlib links are
serialized and stripping is deferred to packaging to avoid observed Windows
sharing violations. None of these changes alters Tor protocol or crypto code.

Static linking removes the need to ship crypto/event/zlib DLLs and allows
unused-section elimination. Shared builds retain recursively resolved imports.
Default crypto providers are not algorithm-pruned; no TLS versions, certificate
checks, signature checks, entropy sources or protocol checks are bypassed.
`no-tests` affects OpenSSL test executables only. Neither build nor package
contains a Tor identity. Compiler runtime DLL licensing is included as needed.
