# Windows native validation

Executed on 2026-11-12, native Windows x64 build **10.0.26100 / 24H2**.
No WSL, Linux, QEMU or Wine was used for compilation or these Windows tests.
The default source pipeline completed cleanly with exit code 0 using MSYS2
UCRT64, GCC 16.2.0, binutils 2.47 and MinGW-w64 CRT 14.0.0.

| Gate | Executed result |
| --- | --- |
| Exact source and license hashes; zero-fuzz verified Tor patch | PASS |
| Clean default native Autotools source build | PASS |
| Native AMD64 PE32+, ASLR, DEP, high-entropy VA; recursive DLL closure | PASS for both variants |
| Version/help, both shipped configs, compiled modules, invalid config rejection | PASS |
| Writable state, authenticated control, clean shutdown and restart | PASS |
| Public bootstrap and SOCKS hostname DNS delegation with clean runtime PATH | PASS |
| Original nine assertions, optimized static release | **9 PASS** — `functional-static-repeat-17` |
| Original nine assertions, optimized shared release | **9 PASS** — `functional-shared-patched-15` |
| Source-pipeline unit tests | 6 PASS |
| Existing repository tests | 41 PASS, 2 Linux-specific SKIPPED |
| Linux Kconfig resolution | BLOCKED: requires Linux; target inputs unchanged |
| Full Linux image validation / physical boards | SKIPPED; outside this Windows source-build change |

The nine assertions cover security configuration, Tor configuration, startup,
SOCKS5, public bootstrap of both instances, v3 onion reachability, two-node TCP,
bidirectional SHA-256 integrity (including a 1 MiB payload), and crash/restart
with the same onion identity. Tests, payloads and timeouts were not relaxed.
The two peers ran on this Windows PC and communicated through public Tor.

## Release footprint and imports

| Optimized variant | tor.exe | Required DLLs | Executable plus DLLs |
| --- | ---: | ---: | ---: |
| Static dependencies (default) | 8,642,974 B | 73,308 B | **8,716,282 B** |
| Shared dependencies | 2,838,019 B | 6,910,678 B | **9,748,697 B** |

The default includes `libwinpthread-1.dll`, a native MinGW Windows DLL, and
Windows system/UCRT imports. Shared additionally includes
`libcrypto-3-x64.dll`, `libssl-3-x64.dll`, `libevent-2-1-7.dll` and `zlib1.dll`.
Neither variant imports an MSYS or Cygwin runtime. Complete distribution sizes,
including licenses, configuration, documentation and report, are in
[summary.json](summary.json); detailed imports and hashes are in
[package-static.json](package-static.json) and [package-shared.json](package-shared.json).
Static is the smaller of the two measured complete distributions.

## Runtime observations

| Native run | Bootstrap (slower peer) | Initial onion connection | Bidirectional payload rate |
| --- | ---: | ---: | ---: |
| Static, nine PASS | 25.660 s | 7.068 s | 36,269 B/s |
| Shared, nine PASS | 6.586 s | 48.274 s | 25,506 B/s |

These are single public-network observations with different circuits, not a
controlled performance comparison or an acceptance claim. The ARM executable
reference remains 2,207,484 bytes; it is not a Windows size target. ARM/Linux
and native Windows measurements are not equivalent-condition comparisons.

The separate [native memory run](functional-memory.json) used the same frozen
assertions with only the `/proc` observer replaced by
[GetProcessMemoryInfo](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo).
It measured two Tor processes: mean idle resident working set **89,711,512 B**
after ten idle seconds; combined sampled peak **91,496,448 B** at 0.2 s sampling.
Per-process OS high-water marks were 45,694,976 B and 46,473,216 B. This run
passed eight assertions and had reconnection BLOCKED by timeout; it is not
merged into the later nine-PASS record. PSS, system memory and CPU values remain
null when unmeasured. Short smoke processes briefly overlapped this run, so
these are descriptive process samples, not a controlled memory benchmark.

## Preserved failures and source fix

Unpatched static, shared and O0 Tor diagnostics failed the server-first onion
fixture. Instrumentation showed its header intact after relay decoding, but
DATA arrived while the client remained in CONNECT_WAIT. Windows can deliver
read readiness before the write-ready connect-completion callback. Tor set
the exit stream OPEN and enabled reads; `connection_start_reading()` then
synchronously processed an already buffered greeting before CONNECTED was
queued. The client correctly dropped those early DATA cells.

The separately reviewed patch moves CONNECTED enqueue before read activation.
No crypto, protocol validation, routing, entropy or logging checks are removed.
The patch and exact source preimage/postimage are hash-locked. It applies only
inside this Windows project; ARM/Linux sources and archives remain unchanged.

The patched O0 diagnostic passed all nine tests. Patched optimized shared and
static releases subsequently passed all nine. An earlier patched shared run
had onion reachability BLOCKED, and a patched static memory run had reconnection
BLOCKED, under the unchanged public-network limits. All earlier FAIL/BLOCKED
outcomes remain in [functional-history.json](functional-history.json), with
raw-result digests. `unpatched-*` files preserve the earlier reporting snapshot.
Full failed logs and runtime state remain in ignored local build/runtime paths.
No private keys, cookies or DataDirectory contents are exported here.

## Limits and reproduction

Only this Windows host was executed; Windows 10 needs separate validation.
There was no full-module Windows reference benchmark, no packet-capture DNS
leak audit, no long-duration reliability test and no physical-board test.
SOCKS domain-name delegation was executed, including an exit check with
`curl --socks5-hostname`; packet-capture proof is distinct. No full performance
qualification is claimed from one successful public-network run.

The shared comparison reused unchanged source-built OpenSSL/zlib from its
retained clean build; libevent and Tor were rebuilt from fresh verified sources
after import-library fixes, then Tor was rebuilt with the verified ordering patch.
The default pipeline was executed end to end from fresh verified sources with
that patch. Detached upstream signatures were not independently verified; the
existing repository SHA-256/HTTPS trust boundary is stated in the manifest.

See [BUILD.md](../BUILD.md) for exact clean-machine commands, both linkage
variants, optional full-module reference, native PowerShell smoke and original
functional tests. [PORTING.md](../PORTING.md) contains the ARM feature matrix.
[Toolchain inventory](toolchain-packages.txt), [actual Tor flags](tor-actual-flags.txt),
[static assertions](functional-static.json), [shared assertions](functional-shared.json),
[source checks](pipeline-tests.txt), [repository checks](repository-tests.txt) and
[summary](summary.json) retain executed evidence and hashes.
