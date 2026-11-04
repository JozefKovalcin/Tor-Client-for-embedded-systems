# Tests

`python3 scripts/check.py` runs configuration/script checks and stdlib unit tests
for comparison gates, baseline immutability, measurement validation, security
configuration rejection, actual local TCP framing and SOCKS domain encoding.
Those fixture tests do not prove public Tor behavior. Local loopback sockets
must be permitted by the host sandbox.

On Linux or Windows with a supplied native Tor executable:

```sh
python3 tests/run_all.py --mode offline --tor /path/to/tor
python3 tests/run_all.py --mode online --tor /path/to/tor
```

Each invocation creates new state under `runtime/`; never point `--work-dir` at
an existing production identity. The harness refuses an existing directory.
Default mode is offline, with public tests SKIPPED and exit 1. All mandatory
PASS results are required for exit 0. The build validator defaults to online.
Unavailable Tor or failed prerequisites produce BLOCKED results and exit 1.
Target-only memory metrics are null on hosts without Linux `/proc`.

The QEMU image runs this exact harness, staging only `run_all.py`,
`echo_server.py` and `torrc.in`. It returns sanitized JSON over the serial console.
Tor diagnostics are preserved, but cookie contents and private keys are excluded.
Short tests use real sockets; synthetic benchmark fixtures are explicitly labeled
and never published as measured project results.
