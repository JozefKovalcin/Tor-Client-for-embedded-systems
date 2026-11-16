# Native Windows gates

Run `python -m unittest discover -s windows-native/tests -v` for pinned-source,
archive safety, PE hardening and fail-closed DLL resolution checks.

`scripts/smoke-test.ps1` checks real native processes, shipped configurations,
client-only modules, failure handling, writable state and clean restart.
Its `-Online` mode adds public bootstrap and SOCKS hostname DNS delegation.

Use the unchanged repository `tests/run_all.py` as documented in BUILD.md for
the nine-function workload. Do not reduce its assertions or treat partial
passes as full qualification. Initial ordering failures and executed patched
results are documented in `../evidence/README.md`. The unchanged server-first
fixture is the regression test for the CONNECTED-before-DATA source patch.
