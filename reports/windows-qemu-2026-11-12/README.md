# Native Windows QEMU short smoke test — 2026-11-12

**ARMv7 capacity32: PASS. x86_64 capacity32: BLOCKED on the short public
bootstrap deadline. Full validation was not executed.** The user requested a
short test after initially asking to install QEMU and test both images.

QEMU 11.1.0 (`v11.1.0-12130-ge470268ff4`) was installed in the user's
`LocalAppData/Programs/qemu`. Both emulators executed natively on Windows,
with TCG single-thread, one vCPU, 256 MiB RAM, the profiles' pinned QEMU 8.2
machine types, VirtIO Ethernet, user-mode NAT and disposable disk snapshots.
WSL was used only to retrieve preserved input files. No firmware was rebuilt.

| Executed short check | ARMv7 | x86_64 |
| --- | --- | --- |
| Kernel boot and serial shell | PASS | PASS |
| Ethernet DHCP | PASS | PASS |
| UTC within 60 seconds of Windows | PASS | PASS |
| Read-only SquashFS, separate writable ext4 state | PASS | PASS |
| Production init script, UID/GID 905 and safe listener configuration | PASS | PASS |
| Actual SOCKS5 negotiation (`05 00`) | PASS | PASS |
| SAFECOOKIE authentication completed inside guest | PASS | PASS |
| Public Tor bootstrap reaches 100% within 180-second short-test window | PASS | BLOCKED |
| Original kernel and disk hashes unchanged after test | PASS | PASS |

ARM reached 100% about **16.43 seconds after QEMU launch**. x86 reached **62%**
(`loading_descriptors`) before its 180-second bootstrap observation window
ended; its complete case lasted 185.65 seconds. This deadline is specific to
the short smoke runner; the normal profiles retain their 600-second bootstrap
timeout. This result does not establish an x86 firmware failure. No observed
bootstrap duration is invented for the incomplete case.

The read-only diagnostic observer disks match the archived ARM study hashes.
They provide access to guest loopback SOCKS and perform SAFECOOKIE locally;
no cookies, private identities or writable Tor state are exported. The smoke
runner boots through a serial `/bin/sh`, invokes `rcS`, then explicitly starts
the existing production `S50tor` script. It is not an unattended normal-init
boot qualification. The nine rows above are **short smoke checks**, not the
project's frozen nine-test integration suite.

## Evidence and limitations

- [Summary and classification](summary.json), [unchanged raw result](results.json),
  [result image](vysledky.png), [installer provenance](installation.json),
  [raw log SHA-256 records](log-digests.json).
- Full serial logs, QEMU commands and intermediate attempts remain under the
  ignored `build/windows-qemu-smoke-2026-11-12-*` directories. Earlier attempts
  exposed a multiline parser issue and missing `nc`/`logread` in the deliberately
  minimal guest; those are harness attempts, not firmware rejection evidence.
- The executed runner labelled x86 bootstrap timeout `FAIL`. `summary.json`
  explicitly records the correction to **BLOCKED**, retaining the raw status
  and original result file. The reusable runner now reports deadline exhaustion
  as BLOCKED. No acceptance threshold or historical rejection was changed.
- `python scripts/check.py`: 43 tests run, 41 passed and 2 platform-specific
  tests SKIPPED. Initial sandbox-restricted checks hit file-operation permissions;
  the checks passed outside that sandbox. Kconfig/build checks and
  `validate.sh` were not run: this was a Windows smoke test of existing images,
  with no target configuration changes.
- Onion Service v3 connectivity, bidirectional payload integrity, reconnection,
  sustained performance and physical hardware checks were **SKIPPED** by the
  requested short scope. These results do not satisfy full validation.
- Windows screenshot capture stopped because Computer Use could not confidently
  determine the browser URL. `vysledky.png` is an explicitly labelled rendering
  of measured JSON results, not a screenshot of a QEMU window.

The installer was downloaded from the Windows binary publisher linked by
[QEMU](https://www.qemu.org/download/#windows), and its SHA-512 matched the
publisher's HTTPS checksum. Authenticode did not validate because the signing
certificate expired; [the publisher documents this](https://qemu.weilnetz.de/w64/).
The exact trust boundary is recorded in `installation.json`. QEMU is a host
tool; the pinned target dependency lock and firmware contents are unchanged.

## Repeat

The Windows runner is `scripts/windows_qemu_smoke.py`. It requires the original
ARM images at the path documented in HANDOFF, x86 capacity32 images copied to
`build/windows-qemu-inputs/x86`, and the two original observer disks copied to
`build/windows-qemu-inputs/fixtures`. It verifies all of these against archived
SHA-256 evidence before boot. Use a fresh output directory:

```powershell
python scripts/windows_qemu_smoke.py --output build/windows-qemu-smoke-new-run
```

`--qemu-dir`, `--arm-images`, `--x86-images` and `--bootstrap-timeout` override
the defaults. The runner neither installs dependencies nor changes PATH.
