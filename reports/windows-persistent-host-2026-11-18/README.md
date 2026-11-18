# Windows persistent Onion Service test, 2026-11-18

One explicitly authorized public Tor run used two isolated profiles on one
Windows x64 development host. The application source was based on
`32c7dcefed0c4e2fe8eee493fb37a1d16705b622`. The native input was Tor
0.4.9.14 from Developer build `run-mm7s33j8`, profile `optimized-static`.
The isolated selector verified the EXE, DLL and build report before launch.
See the [native build evidence](../windows-product-2026-11-18/README.md)
for the runtime inventory.

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
numeric reply code without the destination. The next separately approved run
should use the same runtime and keep B running across A's restart to preserve
the original scenario.

The diagnostic revision passed seven offline peer test groups, both Windows
private-profile and delivery fixtures, and 21 loopback Control and online
protocol groups. All three PowerShell test modes denied execution without
approval. The portable repository suite passed 178 tests with 10 platform
skips on retry. Its first run hit Windows access denied while replacing a
temporary manifest in the Linux executor deadline fixture. Both logs are
retained in the ignored test workspace.

The mandatory public suite, clean-host acceptance and physical two-PC
acceptance were NOT RUN in this batch.
