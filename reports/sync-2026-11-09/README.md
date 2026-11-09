# Repository, report and two-PC launcher synchronization — 2026-11-09

Windows, native Ubuntu WSL and GitHub were reconciled from the existing
measured handover. No target optimization or new hardware result is claimed.
The next milestone remains physical board boot with serial, Ethernet, valid
time, entropy and public Tor bootstrap.

## Git and preserved work

| Item | Observed / action |
| --- | --- |
| Windows and remote starting main | `938b6d2b329ec04a9765d95eb65b48cd24a6e6ec`, clean Windows worktree |
| WSL starting main | `fec5f291d41599acb3499f3f1639768a44388a78`, eight modified and 29 untracked files |
| Content comparison | 32/37 equal after CRLF normalization; five differing documentation/report files |
| WSL preservation | Full tracked/untracked snapshot in stash `ce09ea68b91503598dfdefba1c135c404a544f50`, labelled `Preserve measured ARM working tree before 2026-11-09 synchronization` |
| Existing Windows stashes | Both previous capacity/performance draft stashes retained |
| History integration | Fast-forward only; no reset, rebase, force push or baseline replacement |
| Baseline tag | Unchanged `da7c5281145589821ceeb4f6b7aabd4d98833587` |
| Generated data | Existing approximately 83 GiB WSL build tree, downloads, failed logs and sealed artifacts preserved |

[File-by-file pre-sync digests](worktrees-before.json) retain the comparison.
The WSL stash is retained as a historical snapshot; do not apply it wholesale
over the synchronized tree. Its newer ARM test summary was recovered from the
already archived ARM result, while newer handover documentation was retained.
Use `git log -1` for the resulting synchronization commit and `git status -sb`
in both checkouts to verify alignment with `origin/main`.

## Executed checks and limits

| Check | Status | Evidence / scope |
| --- | --- | --- |
| Native Linux `python3 scripts/check.py` | PASS | 43 discovered: 42 executed, Windows-only launcher test SKIPPED; [log](repository-checks.txt) |
| Windows `python scripts/check.py` | PASS | 43 discovered: 41 executed, Linux-only symlink and shell-gate tests SKIPPED; [log](windows-checks.txt) |
| Initial sandboxed Windows check | BLOCKED | Four permission errors in temp directories, sockets and hardlinks; [original failed log](windows-sandbox-checks.txt). Unrestricted repeat passed. |
| `python3 scripts/check_buildroot.py` | PASS | All 15 profiles resolved against verified pinned Buildroot sources; [log](buildroot-checks.txt) |
| Launcher ShellCheck | PASS | `shellcheck scripts/two_pc_demo/run-wsl.sh`; [execution record](checks.json) |
| Executive HTML/JSON regeneration | PASS | Byte-identical to committed package; `python3 reports/executive/generate.py` |
| Archived evidence integrity | PASS | All 102 SHA-256 checks matched; [records](evidence-integrity.json) |
| Windows Ubuntu discovery | PASS | Actual local `Ubuntu` identified as Ubuntu 26.04; no VM test started |
| Actual WSL launcher preflight | PASS | `bash scripts/two_pc_demo/run-wsl.sh --check`; no Tor or update started |
| Launcher compatibility scenarios | PASS | Older/renamed Ubuntu, UTF-16 listing, selection and argument/exit forwarding mocked on Windows. Five Linux scenarios cover compatible tools, old Python, old QEMU, EOF/decline and explicit opt-in to a mocked upgrade. |
| Slovak PDF verification | PASS | All six pages rendered and visually reviewed. Pages 1–5 render identically; page 6 is identical outside the launcher note; [records](deliverables.json) |
| Repacked ZIP integrity | PASS | CRC check and launcher-content checks passed; `run.py`, `zImage` and `disk.img` unchanged; [manifest](deliverables.json) |
| New complete image build / `validate.sh` target run | SKIPPED | Firmware unchanged; no expensive rebuild or target rerun required for launcher/documentation work |
| New public-network suite and actual two-PC session | SKIPPED | Earlier measured online passes remain historical evidence, not new execution |
| Physical board qualification | BLOCKED | No physical board evidence supplied or produced; milestone remains open |

This audit is not full online qualification. The platform-specific unit-test
skips above are separate from the mandatory target online tests. No threshold,
historical rejection, policy, frozen suite or source pin changed. Missing target
measurements were not synthesized. [Command exits and timings](checks.json).

## Reports reconciled

`reports/latest.md`, `reports/test_results.json` and `reports/test_summary.md`
now point to the same archived ARM run, `20261105T190223Z-7002ec13`, rather
than mixing an ARM introduction with older x86 measurements. The original
`fec5f29` dirty-tree provenance, all function PASS results and overall
`NON_COMPARABLE` decision are unchanged. `optimization-history.csv` and archived
measurements were not rewritten. ARM and x86 256 MiB QEMU groups passed their
complete screens; the 128 MiB performance rejections remain in force.

The current [Slovak executive PDF](../executive/Tor_Executive_Report_SK.pdf)
was supplied as `Tor_Executive_Report_SK.pdf` from Downloads. Its measurement
data matched the archives. Only the two-PC launcher note on page 6 was updated.
The [original PDF](../executive/source/Tor_Executive_Report_SK_2026-11-06.pdf)
is retained, as are the earlier English HTML/PDF and dataset.

## Two-PC package

The supplied local `Tor_Capacity32_Test_Dva_PC.zip` contained the two ARM image
files and a 59-byte echo demonstration. Its CMD launcher tried only three
fixed distribution names before falling back to the default WSL distribution;
older named Ubuntu installations could be missed when Docker was the default.

The corrected CMD delegates to a bundled PowerShell script. It enumerates
registered distributions, checks `/etc/os-release` for Ubuntu, and offers
selection when several exist. Names and versions are not limited to a fixed
list. Before Tor starts, the Linux launcher verifies Python 3.11+ and QEMU
`virt-8.2` support. Older tools trigger an explanation and an explicit opt-in
Ubuntu upgrade offer. No upgrade occurs on decline/EOF. A separate newer WSL
Ubuntu installation is also suggested. After an upgrade the user reruns the
launcher; the test does not continue with stale processes.

Ubuntu 24.04 provides suitable [Python](https://packages.ubuntu.com/en/noble/python3)
and [QEMU ARM](https://packages.ubuntu.com/de/noble/amd64/qemu-system-arm) packages.
The launcher still checks actual capabilities instead of accepting an OS
version alone. No Ubuntu upgrade was executed during this work.

Sources live in `scripts/two_pc_demo/`. `scripts/build_two_pc_demo.py` creates
a fresh ZIP from those sources and checksum-verified original image entries,
with a fixed allowlist excluding private runtime state. The images and TCP
test implementation remain byte-identical. The small demonstration is not
the nine-test target qualification or a physical USB/Ethernet test.

The updated ZIP and extracted launchers are delivered in Downloads. The
original ZIP is preserved under ignored `build/sync-2026-11-09/package-original/`;
its SHA-256 and all new entries are recorded in [deliverables.json](deliverables.json).
Generated firmware and ZIPs remain outside Git.
