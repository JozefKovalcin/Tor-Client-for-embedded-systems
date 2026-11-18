# Windows persistent Onion Service test, 2026-11-18

Two separately authorized public Tor runs each used two fresh isolated profiles
on one Windows x64 development host. The application source was based on
`32c7dcefed0c4e2fe8eee493fb37a1d16705b622`. The native input was Tor
0.4.9.14 from Developer build `run-mm7s33j8`, profile `optimized-static`.
The isolated selector verified the EXE, DLL and build report before launch.
See the [native build evidence](../windows-product-2026-11-18/README.md)
for the runtime inventory.

## First run

| Step | Result | Evidence |
| --- | --- | --- |
| Initial bootstrap | PASS | A and B authenticated with SAFECOOKIE, reached 100% and had separate owned SOCKS listeners. |
| Initial hosting and exchange | PASS | Authenticated `HS_DESC UPLOADED`. B reached A through SOCKS5. Mutual TLS and exact text in both directions passed. |
| Host restart | PASS | A's original process exited with code 0. B stayed running. A reopened the DPAPI-protected Onion Service and the original TLS identity from its profile, then bootstrapped under a new PID. |
| Address and publication after restart | PASS | Tor returned the original service ID. Authenticated `HS_DESC UPLOADED` was observed again. |
| Exchange after restart | FAIL | The test exited with code 1 after an `IOException` in the restored exchange path. The captured result does not resolve the stage or root cause. |
| Teardown | PASS | The final exit assertion inside the test was not reached. The two local task journals recorded exit code 0 for the sessions torn down after the failure, and the process check found no running `tor.exe`. |
| Overall | FAIL | The client did not demonstrate a successful exchange after the host restarted. |

The failed run's sanitized console log was retained locally at
`build/windows-persistent-host-qualification/public-host-20261118-172936.log`.
Both profiles and the journals remain in the ignored test workspace.

The runner now distinguishes SOCKS connection, mutual TLS, bidirectional
transfer and authenticated close. SOCKS refusal diagnostics report the
numeric reply code without the destination.

## Diagnostic run

The repeat started on 2026-11-18 at 16:49:56 UTC from
`3295802c594a6275f8ce95a7f7ff084c96b0c6b9`. The PowerShell entry point built
the runner with zero warnings and errors from an empty local NuGet feed.
The EXE and DLL matched the recorded hashes. B stayed running across A's
restart.

| Step | Result | Evidence |
| --- | --- | --- |
| Initial bootstrap and publication | PASS | Both native clients authenticated with SAFECOOKIE, reached 100% and exposed their own SOCKS listeners. A received `HS_DESC UPLOADED`. |
| Initial exchange | PASS | Mutual TLS and exact text in both directions through B's verified SOCKS listener. |
| Host restart and republish | PASS | A's first process exited 0. New A reached 100%, reopened the same TLS identity, imported the original v3 address and received `HS_DESC UPLOADED` again. |
| Reconnect from B | FAIL | All three attempts ended in the SOCKS tunnel stage with `OperationCanceledException`. No numeric SOCKS reply surfaced. |
| Restored peer TLS and transfer | NOT RUN | The SOCKS tunnel never became ready. |
| Teardown | PASS | Both task journals recorded exit code 0 and the post-run check found no running Tor process. |
| Overall | FAIL | The wrapper returned exit code 1. |

The console log is retained locally at
`build/windows-persistent-host-qualification/public-host-retry-20261118-164956Z.log`.
Both runs kept the address, pins and keys out of their console logs and
reports. The repeated result identifies the failed stage. The SOCKS
timeout alone leaves the underlying cause open.

A further public diagnostic would record only allowlisted descriptor-fetch
events and outcomes from B's authenticated control connection. A separate
fresh client against the same restarted host could show whether the symptom
is specific to the original client session. Keep the original live-client
attempt as the reconnect criterion. These checks require separate approval.
[Tor Control descriptor events](https://spec.torproject.org/control-spec/replies.html#hiddenservice-descriptors)

## Offline checks

The diagnostic revision passed seven offline peer test groups, both Windows
private-profile and delivery fixtures, and 21 loopback Control and online
protocol groups. All three PowerShell test modes denied execution without
approval. The portable repository suite passed 178 tests with 10 platform
skips on retry. Its first run hit Windows access denied while replacing a
temporary manifest in the Linux executor deadline fixture. Both logs are
retained in the ignored test workspace.

The mandatory public suite, clean-host acceptance and physical two-PC
acceptance were NOT RUN.
