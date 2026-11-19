# Windows Task 14 development evidence

Validation ran on one Windows x64 development host with .NET SDK 11.0.100
and the existing locked release feed. Main was verified at
`32c7dcefed0c4e2fe8eee493fb37a1d16705b622`. PR #33 was at `c4f235d`
and the redesign in #34 was at `044f10d`, stacked on #33. Both heads had
successful GitHub checks. The full workflow was skipped. No change was
merged into main.

| Review | Source and tested head |
| --- | --- |
| [Peer ingress and diagnostics, draft #35](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/35) | `98b7aaba16e796e40f88829c500512974e65717c`, based on #33. The underlying transport fix is `0a784ce`. |
| [Standalone GUI, draft #36](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/36) | `c5ef5f292dc4fbbb976c3a2c080320d2b536928b`, based on main. Interface files came from `a74df30` in #34. |
| [Developer, draft #37](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/37) | `f8cd37aa76841ddf5f6a14236a5e97f181676542`, based on main. |
| [Peer delivery and recovery, draft #38](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/38) | `4e78695f6c1e6d996c84d6256f86edc5239f5db9`, based on #36. |
| Integrated Windows candidate | `b84b0cde114c924c79b664cf8345bf299d1e56a8`. |

## Public persistent Onion Service

The two verified Tor 0.4.9.14 builds used the same Manager harness. Each run
started in fresh owner-private profiles. The restart cases imported the
original Onion Service key and reopened the same pinned TLS identity. The
comparison client had a fresh Tor process and the same in-memory TLS
credential as the original client. A comparison pass leaves the original
live-client reconnect result unchanged.

| Runtime and tested source | Initial service | Original client after restart | Fresh comparison |
| --- | --- | --- | --- |
| Bundled, `469af09`, 18 Nov 17:33 UTC | Published. First TLS attempt stalled before any host ClientHello was observed. Second attempt passed mutual TLS and exact bidirectional text. | Same identity and publication. First reconnect returned SOCKS5 `0x06` without a descriptor event or host accept. Session was interrupted before the remaining attempts finished. | NOT RUN |
| `optimized-static` `run-mm7s33j8`, `469af09`, 19 Nov 07:18 UTC | Same first-attempt boundary. The second attempt passed mutual TLS and exact bidirectional text. | FAIL. Three requests returned `0x06` without descriptor activity or host TCP accept. | PASS, fresh descriptor received, mutual TLS and exact text both directions. |
| Bundled, `0a784ce`, 19 Nov 07:28 UTC | Second attempt passed mutual TLS and exact bidirectional text. | FAIL. Each attempt requested and received a descriptor. All three exhausted the SOCKS recovery budget before host TCP accept. | PASS, mutual TLS and exact text both directions. |
| `optimized-static` `run-mm7s33j8`, `0a784ce`, 19 Nov 07:40 UTC | Second attempt passed mutual TLS and exact bidirectional text. | PASS on attempt 2 after the descriptor refresh in attempt 1. The original client remained running. The reopened host used the same address and TLS identity. Mutual TLS and exact text passed in both directions. Both Tor processes exited 0. | NOT RUN because the original client passed. |

In the separate bundled `98b7aab` experiment at 08:03 UTC, initial
publication, mutual TLS and both directions passed on attempt 1. The
unassisted reconnect after same-address restart exhausted the SOCKS
budget after descriptor receipt. The controller then observed an explicit
`NEWNYM` on the same Tor PID. A new SOCKS tunnel to the original address
passed mutual TLS and exact text in both directions. Both owned Tor
processes exited 0. The wrapper intentionally returned 1 for the
unassisted failure. This experiment verifies the operator recovery in
[Peers](../../docs/WINDOWS-PEER.md#recover-a-stalled-connection-on-the-existing-tor-process).

The first initial failure was upstream of certificate authentication and
ALPN on the host. The later reconnect failure was earlier, before SOCKS
success and before the host accepted a TCP connection. A received event
shows descriptor receipt. The event alone leaves the descriptor revision
in use and the state of the reused Tor introduction or rendezvous circuit
open. The pinned Tor source maps its own stream timeout to SOCKS reply
`0x06`. That reply previously bypassed the application retry. The fix
performs one authenticated target-only refresh within the existing bound.
It changes neither the Tor binary nor the peer security policy.
The tested custom result qualifies that executed scenario. The package
still contains the known bundled binary, whose live-client result is FAIL.

The unassisted bundled failure remains open. The next diagnostic should
compare only the cached descriptor revision change and allowlisted
introduction/rendezvous outcomes in the original and renewed client.
Console output retains timings, stage names and result codes. It excludes
addresses, pins, raw certificates, cookies, keys and payloads.

## Offline regression and independent review

| Area | Executed result |
| --- | --- |
| Peer ingress and deadlines | PASS. Delayed SOCKS/TLS, TLS versus ready deadline, caller cancellation, rejecting a socket before TLS without Tor ownership, alternate accepted handshake, and 100 authenticated reconnect cycles (0.41–0.48 s in recorded runs). |
| Protocol and Windows policy | PASS. Nine base Control groups, 13 online loopback groups, Windows PID/tuple checks, targeted `0x06` refresh and refusal of local proxy or authorization faults. The bundled test runner refused public execution without approval. |
| Peer delivery | PASS. Lost ACK, persisted queue reopening, deduplication, TTL, session-local Sent, acknowledgement precedence, queue limit, locked writer, failed replacement and injected disk-full. The encrypted queue remains intact on rejection. |
| Local TLS recovery | PASS. A healthy primary was preserved. Explicit recovery restored the original trust-bound DPAPI credential, contacts and message queue from a protected copy. Missing primary recovered and an unrelated copy was refused. The fixture kept original damaged bytes private. |
| Repository, core and application | PASS. 180 portable tests with 10 platform skips, 19 .NET Core task groups and 11 application groups. Cancellation reaped the real Developer fixture worker and its child. |
| GUI | PASS. 69 presentation assertions and 26 Windows peer assertions. Preview .2 rendered both themes. Keyboard focus moved Dashboard → Peers → Settings. Developer scan showed loading, then AVAILABLE. A missing archive path gave BLOCKED and disabled Compile. Closing the window reaped its active offline Tor with exit 0. Preview .3 passed launch and exit from a path with spaces and a System32-only PATH. The operator stopped further Computer Use. DPI and minimum-size matrix NOT RUN. |
| Developer preflight | PASS. 24 Windows Python fixtures. Read-only shared preflight accepted the original four-archive cache. The live local install plan was BLOCKED with zero eligible missing packages. No system transaction was attempted. |

Independent review identified and prompted corrections for host failure
attribution, exact PID ownership, alternate-slot lifetime and trace
mixing, completed installer exit reporting, and corrupt local storage
classification. Local CLI, native process and fixture checks preserve the
reviewed pinning, ALPN, authenticated Control and package approval boundaries.

## Integrated candidate

The local archive `TorManager-win-x64-0.1.0-dev.20261119.3.zip` is under
`build/task14-evidence/`. Its manifest records the exact integrated
source commit above. Locked Win-x64 restore, self-contained publish,
319 payload hashes and 20 native import inventories passed. The bundle uses
the existing bundled Tor 0.4.9.14 EXE and DLL.

- Size 55,426,677 bytes.
- SHA-256 `db509db03521c430e6d4a3d8729f662eb1234427efb19c2644253a276c039b66`.
- Native offline SAFECOOKIE startup and bootstrap 0, normal and unexpected
  exit, cancellation, kill-on-close, runtime tamper refusal and staged DLL
  closure passed.

The archived payload only includes allowlisted build output. Public test
profiles, raw local logs and writable state remain in ignored folders.
The mandatory full public suite, clean-host qualification and physical
two-PC acceptance were NOT RUN. This is a development candidate.

## Native optimized-shared client

Developer draft #37 used the existing verified four-archive cache and
reviewed native recipe for `run-3zhx3mpr`. The real compiler returned 0
and the PE/package checks passed. The offline smoke recorded eight PASS
and two online SKIPPED. The packaged Tor reports version 0.4.9.14.

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `tor.exe` | 2,839,010 | `fb28a49b8be1fc1c9d40093f8fbf6418d9d4f524a1f70eb7596298dc43e3636e` |
| Five DLLs combined | 6,902,456 | Pinned separately in the verified candidate inventory. |
| EXE and DLLs | 9,741,466 | — |
| Shared runtime ZIP | 4,265,777 | `14bb8c9fb0fad0e259859a6dcb14b5e28f486e890d1e28c08ea1e13614f602f3` |

The package is under
`%LOCALAPPDATA%/TorManagerDeveloper/builds/run-3zhx3mpr/pipeline/dist-optimized-shared/`.
The export `tor-0.4.9.14-optimized-shared-run-3zhx3mpr.zip` in the
Developer exports directory passed its member check for all 25 files.
The isolated selection activated the actual shared build, authenticated
an offline Tor process, refused activation and rollback while the process
held the lease, stopped it with exit 0, and rolled back to the bundled
pin. The user's runtime selection was outside the test scope.

## Audit follow-up, draft #39

[Draft #39](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/39)
addresses the ten repository audit findings. Both bundled Windows
same-address restart tests passed with the original client alive, mutual
TLS, exact two-way transfer and zero exit codes. The restored exchanges
took about 123 seconds and 8.5 seconds. The retry uses a target-only
descriptor refresh and a fresh SOCKS isolation token. The tests ran
without NEWNYM or a fresh client. The successful manual NEWNYM comparison
and earlier automatic failures above remain part of the record.

Windows passed 183 portable tests with 10 platform skips, 26 private
Peers workflow assertions, the native offline lifecycle suite, and all
nine frozen online tests with the extracted bundled client. The tests
exercised journal rollover and reopening, Tor lifetime and explicit stop,
host identity creation and rotation, and removal of disposable runtime
files after exit. Buildroot resolved all 15 Kconfig profiles on Ubuntu
24.04 WSL. The checked cache remained reusable and rejected added files
in the regression fixture.

The self-contained Windows x64 archive
`TorManager-win-x64-0.1.0-dev.20261119.6.zip` came from
`160c6ee659d9f182c1ca92c93162665d4c1835d3`. Locked restore, 320 payload
hashes, 20 native import inventories, the build-time provenance record,
launch from a path with spaces and a System32-only PATH, and controlled
exit 0 passed.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| Windows x64 ZIP | 55,433,613 | `0bcae1c41034bfe852b3f295f00242bdb85f5da9298edb7b626eb03035a1628c` |
| Windows Manager EXE | 73,728 | `fbbaff47b2970e451e1be69ee7dbf2c83257a75f53b8f41a2baf9cdad010723a` |
| Bundled Windows Tor EXE | 8,642,974 | `3a65024937320fb53a76615273d9cdf7718d9cf90855081dab4b73ae52d2c7f9` |
| Bundled Windows companion DLL | 73,308 | `94334eab25e1a4043a3efdcf88636e722f387fe62fcc57c2533876a019baa04c` |
| Bundled Windows Tor EXE and DLL | 8,716,282 | — |
| Linux x64 runtime archive | 4,409,217 | `81c115c8ea753b8f3c085bf3ae65a29d320805ea5d81314c71f247ed22e61976` |
| Linux Tor ELF | 11,734,256 | `6c4c0f0d85e3b8802c97c79f3ee55a0332802ceb8890103f07cd2035e771a886` |

Ubuntu 24.04 x86_64 on WSL used GCC 13.3.0 and glibc 2.39 under a
regular build account. Run `run-8d35be6545f340ecb3fdec4056f36b62` compiled
all five pinned sources from cache. The earlier libevent timestamp and
missing gperf failures are preserved. The offline adapter then verified
the real Linux executable, waited for its loopback control event loop,
and collected exit 0.

The Linux archive `tor-linux-x64-0.4.9.14-audit.tar.gz` contains Tor,
five licenses and the runtime inventory. Its system dependencies are
`libc.so.6`, `libm.so.6` and the x86_64 glibc loader. It was extracted and
the resulting executable passed all nine frozen online assertions,
including SOCKS5, v3 onion reachability, two-way SHA-256 integrity and
same-address reconnection. The build manifest SHA-256 is
`447025b46c01cffc0de3689123c86959341309d846825456d3475b86b0be1d05`.

The same .NET Manager published and started under Xvfb on Ubuntu 24.04
WSL. The packaged adapter listed the actual build and preserved a
cancelled build manifest when its controller closed stdin. The Linux
offline suite passed 190 tests with three platform skips. These results
cover one Windows host and its Ubuntu WSL environment. Independent clean
hosts, physical two-PC testing, public endurance and Windows DPI checks
remain NOT RUN.

Two Windows-to-WSL cross-platform attempts exercised the exact extracted
runtimes and the existing echo and SAFECOOKIE helpers. The first exchanged
the expected data in both directions, then timed out after Windows
restarted. It did not wait for restored descriptor publication before
asking the Linux client to reconnect. The second confirmed publication,
requested the target descriptor from the original Linux Tor process and
opened a fresh isolated SOCKS stream. That run passed before and after
restart, including the same onion address, 1 MiB echo SHA-256, the
server-push SHA-256, and exit 0 from both final processes.

### Windows private file ownership

The hosted Windows CI runner first exposed two environment differences.
Test temporary paths used a short Windows alias, and its elevated process
created new files with Administrators as the owner. The tests now use
canonical fixture paths. Windows private writers assign the actual
account SID and the private ACL at creation. They still check existing
files before use. The shared helper covers peer TLS, contact locks,
delivery data, Onion Service keys and disposable runtime staging.

Candidate `TorManager-win-x64-0.1.0-dev.20261119.7.zip` was built from
`71c32ceb9650479ea67a4494c80dec25ee4434c7`. It passed locked publish,
320 payload hashes, extracted Manager launch and clean close, native
offline SAFECOOKIE and lifecycle checks, and 26 Windows peer workflow
assertions. A third Windows public persistent-host test passed with the
original client. The host reopened the same onion address and TLS
identity and both peers closed with exit 0. The .7 Tor EXE and DLL hashes
match the .6 binaries used for the nine frozen online tests.

The [Windows offline job](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/actions/runs/44540579755/job/135617492756)
and [Ubuntu checks](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/actions/runs/44540579755/job/135617492556)
passed on `71c32ce`. The Windows job passed the isolated private-peer
workflow under the elevated account that reproduced the ownership error.
The separately dispatched full embedded build remained SKIPPED.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| Windows x64 .7 ZIP | 55,434,062 | `3c50190c5f700b4e634d93b0d745d29bd0c3e5026ae7c0527cdcfc421a5fb0d3` |
