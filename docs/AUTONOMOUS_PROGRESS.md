# Windows Tor Manager development

## Current Windows qualification, Task 14

| ID | State | Source and evidence | Remaining |
| --- | --- | --- | --- |
| WIN-01 / WIN-02 | VERIFIED offline | `PeerProfileStore`, `PeerDeliveryStore`, `ReliablePeerSession` and Peers add approved recovery, session-local send progress, and local storage error reporting. | Independent-host acceptance remains pending. |
| WIN-03-HOST | VERIFIED single host | Draft #39 reran the bundled same-address restart twice. The original client reconnected without NEWNYM, with mutual TLS, both directions and exit 0. Draft #35 retains the earlier unassisted failures and explicit recovery evidence. | Independent-host acceptance and public endurance. |
| WIN-03-MODES | VERIFIED offline | Standalone draft #36 at `c5ef5f2` preserves connection, workspace and runtime choices across launches without starting Tor. Windows preview rendered light and dark and passed keyboard entry, Peer navigation and Developer gating. | Minimum-size and DPI matrix NOT RUN. |
| WIN-04 | PARTIAL | Draft #37 at `f8cd37a` verifies actual recipe tool paths, blocks incomplete local install plans and preserves installer exit through refresh cancellation. | Real system install BLOCKED because no eligible missing package was found. |
| WIN-05 | VERIFIED offline | Static `run-mm7s33j8` and shared `run-3zhx3mpr` both compiled from verified sources. Shared compiler exit 0, PE closure, eight offline smoke checks, verified 25-file export and real isolated activation/rollback passed. | Shared public-network qualification NOT RUN. |
| WIN-06 | VERIFIED on development host | Draft #39 adds build-time publish hashes and a locked self-contained Win-x64 publish. Candidate .7 preserves the Tor payload that passed all nine frozen online tests. Its Manager and native Tor lifecycle passed, including the public persistent-host check. | Clean-host acceptance and physical two-PC interoperability. |
| WIN-07 | VERIFIED for offline fixtures | 100 authenticated local reconnects, interrupted TLS, lost ACK, TTL, queue capacity, failed atomic replacement, simulated disk full, corrupt DPAPI and owned process cleanup. | Public endurance remains NOT RUN. |
| WIN-2PC | NOT RUN | Field acceptance is deferred to the user. | Clean Windows and two physical PCs after software gates. |

Main was verified at `32c7dce`. Draft #35 is stacked on #33. Draft #36 and
draft #37 target main independently. Draft #38 stacks on #36. Draft #34
retains the original stacked redesign provenance. The
[Task 14 evidence](../reports/windows-task14-2026-11-19/README.md)
records tested heads, telemetry, offline logs and the package checksum.

The bundled same-address reconnect previously failed and now passed twice
on a single Windows development host. **FIELD QUALIFIED** is NOT RUN.
See the [audit follow-up evidence](../reports/windows-task14-2026-11-19/README.md#audit-follow-up-draft-39).

## Historical persistent hosted identity batch

Verified GitHub main at `68002b9b4a31e5da5acc152a3c17b3e49da7daed` and
draft #30 at `383f861850b99503a62d20a666654fc06120bb3d`, with
[successful CI](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/actions/runs/44310924252).
The dependent branch is `codex/windows-persistent-onion` in
[draft #31](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/31).

| ID | State | Source, gate and result | Remaining |
| --- | --- | --- | --- |
| WIN-03-HOST | VERIFIED | `TorControlClient`, `HostedOnionStore`, `PeerViewModel`, Windows transport and Peers page. Offline loopback generation/import, cleanup and redaction passed. Windows DPAPI fixture passed same-address restart, explicit rotation and corruption refusal. | Real public Tor lifecycle and current GUI visual/DPI acceptance are NOT RUN. |
| WIN-03-MODES | PARTIAL | PR #30 separates the Peers page from Developer pages. | Independent connection, workspace and runtime selectors. |
| WIN-04/WIN-05 | PARTIAL | Real Developer adapter, offline diagnosis, cancellation, activation, rollback and export retain the PR #29/#30 evidence. | Separate authorization for heavyweight compilation, package installation and any missing downloads. |
| WIN-06 | PARTIAL | Current Avalonia Release build passed from an offline locked restore. | Current peer-enabled self-contained publish and bundle acceptance. |
| WIN-2PC | NOT RUN | User-operated two-PC and clean-host tests are deferred. | Field qualification after development. |

Windows Release compiled with zero warnings. `python -B scripts/check.py`
returned 174 PASS and 10 platform skips. The isolated Windows peer fixture
returned 23 PASS. The full and focused logs are in the ignored
`build/windows-persistent-onion-regression.log` and
`build/windows-persistent-onion-fixture.log`. The restart tests use a
simulated Tor Control endpoint and fresh fixture profiles.

The mode and packaging follow-up is recorded in the current batch above.

## CURRENT_BASE

GitHub `main` matched `68002b9b4a31e5da5acc152a3c17b3e49da7daed`
on 2026-11-18. PR #29 integrated the exact heads of #19–#28 by Git
ancestry. GitHub returned zero open PRs at the start of the batch.
The integration base includes both the reliable peer and TASK 12 Developer
stacks. The active development worktree is `build/windows-product-completion`
on `codex/windows-product-completion`.

## COMPLETED_THIS_RUN

| ID | State | Implementation and evidence | Remaining |
| --- | --- | --- | --- |
| WIN-01 | VERIFIED | `PeerProfileStore` and `PeerViewModel` edit a saved onion endpoint while preserving its pin, and atomically revoke and pair a replacement after explicit comparison. Windows fixture reopened the profile and refused the revoked outbox. | Explicit local TLS credential replacement and recovery. |
| WIN-02 | VERIFIED | PR #29 contains the DPAPI outbox, authenticated ACK and expiry/deduplication. The replacement workflow checks that old queued messages never follow a new pin. | Per-message sent/retrying state and exhaustive storage recovery tests. |
| WIN-03 | PARTIAL | Real Offline and Real Online share a Peers page. Each selected contact shows bounded session-local events. The fixture checked restart intent, isolation of drafts and histories, and offline queueing. Avalonia compiled and the initial contact card rendered on Windows. | Stable hosted onion address across Tor restarts, final Peers page visual and DPI checks. |
| WIN-04 | PARTIAL | PR #29 contains real preflight, a pinned local install plan, signed-package gates and rescan. The earlier isolated package transaction is historical evidence. | Exercise the full current GUI install/repair path on an approved fixture or host. |
| WIN-05 | PARTIAL | `DeveloperViewModel`, `WindowsDeveloperRunner` and `scripts/windows_developer/build.py` add export of a verified build package. Fixture checked gate refusal, provenance and payload hashes, ZIP inventory, tampering and overwrite refusal. | Run the integrated static/shared source builds with approval, then smoke, activation, rollback and export on their actual outputs. |
| WIN-06 | PARTIAL | Current peer-enabled Avalonia GUI compiled from an offline locked framework-dependent restore. PR #29 has prior self-contained build evidence. | A versioned current self-contained Windows x64 package with bundled verified Tor and clean-host verification. |
| WIN-07 | PARTIAL | `TaskJournal` uses a short bounded retry for Windows rename denial or sharing conflicts. The failing bounded-stream fixture exposed an access denial while publishing `task-logs.json`. | Longer offline soak, disk-full/profile recovery matrix, and public endurance qualification with approval. |

## TEST_EVIDENCE

- Windows .NET SDK 11.0.100, offline locked framework-dependent restore and
  Avalonia Release build. Presentation harness 63 assertions PASS and
  private-profile peer workflow 16 assertions PASS.
- Windows Build Adapter 7 tests PASS and unchanged native pipeline 6 tests
  PASS. The export test uses a deterministic package fixture and exercises
  the real ZIP publication and byte verification paths.
- `python -B scripts/check.py` passed after the journal fix with 174 PASS
  and 10 platform-specific SKIPPED. The log is `build/portable-windows-final.log`.
- The existing pinned Tor passed native offline shutdown, unexpected exit,
  owner death and recovery, active-session protection and runtime rollback.
  Independent fixture profiles passed loopback mutual TLS, lost ACK,
  restart deduplication, bidirectional ACK, expiry and opt-in history.
- Git confirmed the baseline tag and both stashes. The diff preserves
  Buildroot inputs, source locks, native Tor recipe, reports and original
  Tor pipeline and protocol tests.
- The first two unsandboxed portable runs failed the bounded task-journal
  case. The focused failure and exception are retained in
  `build/core-streams-repro.log` and `build/core-streams-diagnostic.log`
  relative to the batch worktree. The diagnostic source edit was removed.
- The initial GUI launch used a disposable state directory and pinned Tor,
  with Tor stopped. Computer Use was interrupted and continued reporting
  the stop after reauthorization. The rebuilt Peers page was compiled and
  exercised through presentation tests. Visual navigation and DPI checks
  remain NOT RUN for that page.

## OPEN_PULL_REQUESTS

The current batch is [draft PR #30](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/pull/30).
The tested implementation is `a3cee73d0d592ca7b34e74cdb639c6abe910662f`,
including journal fix `04c7028a322ef2a46bbca483299e0cc542337f87`.
PR #29 is the integrated base. The previous draft inventory below is historical.

## BLOCKED_AWAITING_APPROVAL

Real dependency installation, heavy native compilation, public Tor tests
and production publishing remain approval-gated. Downloads and existing
real peer credentials were outside the execution scope for this batch.

## PENDING_PHYSICAL_TWO_PC_CHECKS

Current physical two-PC, public bootstrap, endurance and clean-host
qualification are NOT RUN. The [operator runbook](WINDOWS-PEER.md) covers
pairing, data integrity, restart, reconnect and cleanup.
Development is partial. Field qualification is pending.

## NEXT_ACTION

Implement a Tor-owned persistent v3 Onion Service identity under the
private profile and restore its host registration across Tor restart.
Keep the temporary-service transport available for the existing harness.
Exercise stop, same-address reopen, failed registration and the remote
client's pinned reconnect with fresh fixture profiles and the reviewed
runtime in offline mode. Then finish the independent connection/workspace
selectors and the peer-enabled self-contained release packaging.

---

# Historical messaging stack before PR #29

## CURRENT_STATE / CURRENT_BASE

At the start of that historical cycle, remote `main` was `266e29a29cab4b465415d8388614493a661e77f8`.
Draft PR #19 targets `main` at head `97ccff6a00c886606501f72f58dffbb310de8f99`.
The Windows messaging work starts from that peer head in isolated worktrees.
TASK 12 is a sibling effort stacked as #19 → #20 → #21 → #22 → #23 → #24.
The baseline tag is `da7c5281145589821ceeb4f6b7aabd4d98833587`. Both
original stashes and the root worktree's untracked report are preserved.

## ACTIVE_PHASE / ACTIVE_PR

Review of phases 0–3. The four isolated branches are
`codex/windows-peer-{audit,trust,delivery,workflow}`.
The workflow uses the reliable protocol with a paired contact and leaves
the existing transient protocol available to the lower-level harness.

## VERIFIED / TEST_EVIDENCE

GitHub returned passing push and pull request checks for the exact heads of
#19–#24. Raw metadata is in `build/windows-autonomous-evidence/github-initial.json`.
Against `97ccff6a00c886606501f72f58dffbb310de8f99` on Windows x64 with
Python 3.14.2 and SDK 11.0.100, `python -B scripts/check.py` returned 0.
160 tests passed and 10 platform-specific tests were skipped. The suite
includes the offline negative TLS and SOCKS peer groups. The Windows
pipeline returned 0 with six tests. The real pinned Tor runtime passed
offline SAFECOOKIE shutdown, exit and owner-death tests with exit 0.
Logs are in `build/windows-autonomous-evidence/phase0-baseline/`.
`git diff --check` and comparison of the 566 protected SHA-256 hashes passed.
These checks establish head and software status. Physical PCs and a clean
Windows host require their own qualification.

The following commands returned 0 on Windows x64. New profiles were
generated under the ignored evidence directory.

| Tested code | Command or harness | Evidence and scope |
| --- | --- | --- |
| `1af50a51cd5640adc0f63eaeb42c29f03e172f12` | Peer test assembly `--private-profile <fresh>` | `phase1-trust/identity-profile.log`. Explicit pairing, stable DPAPI identity, durable revocation and corruption refusal. |
| `8643e995e55f4035d246d72c0944c1f0bf8190ea` | Peer test assembly `--private-delivery <fresh>` | `phase2-delivery/peer-delivery.log`. Actual loopback TLS, v1 mismatch, reordered teardown, lost ACK, recovered queue, replay rejection, TTL. |
| `b5e287672d9e9b4268ef925626076ee8a0e91161` | `python -B scripts/check.py`, Windows pipeline, pinned native offline tests | `phase3-final/portable-windows.log`, `windows-pipeline.log`, `offline-tor-control.log`, `offline-tor-application.log`. 160 portable PASS and 10 platform skips, six pipeline PASS and native offline lifecycle PASS. |
| `b5e287672d9e9b4268ef925626076ee8a0e91161` | `dotnet publish --no-restore` after an offline locked restore, presentation test assembly `--private-peer-ui <fresh>` | `phase3-final/`. 62 presentation assertions, nine peer workflow assertions. The graphical preview also showed the pending fixture and opt-in control with Tor stopped. |

The Windows-only focused delivery and profile methods generated isolated
fixture identities. The workflow tests additionally exercised encrypted
history opt-in, retention and reconnect intent. All evidence paths above
are relative to `build/windows-autonomous-evidence/`.

## IMPLEMENTED / COMPLETED_THIS_RUN

The repository backlog now preserves the supplied checklist with explicit
Windows work items. The architecture decisions track the proposed security
boundaries and compatibility before implementation.

Phase 1 adds an explicit first-use P-256 credential protected by Windows
DPAPI, an owner-private trust store, consent bound to the typed fingerprint,
contact selection and durable revocation. Run Only and the existing
transient protocol remain available in the lower-level transport.
Phase 2 adds protocol v2, idempotent acknowledgements and a bounded
per-peer encrypted queue. Phase 3 projects queued and acknowledged
messages, retries the same paired endpoint, and adds per-contact opt-in
history. The receiver preserves accepted content through the delivery
window. No automatically trusted contact or fallback network path is added.

## FAILED

The focused Phase 2 test initially produced `EndOfStreamException` while
both authenticated sessions were being cancelled. The fix distinguishes
ordinary I/O closure during cancellation and preserves ordinary errors
while the operation is active. The focused retry passed.

## OPEN_PULL_REQUESTS

| PR | Role |
| --- | --- |
| #19 | Mutual TLS peer messaging over Tor, baseline for this cycle |
| #20–#24 | Existing Developer Manager drafts, outside this cycle's ownership |
| #25 | Exact-head baseline, checklist and security decisions |
| #26 | Per-user TLS credential, owner-private contact pins and revocation |
| #27 | Reliable protocol, durable encrypted queue and offline replay tests |
| `codex/windows-peer-workflow` | Desktop pairing, delivery and opt-in history. Exact draft and final head in `github-drafts.json`. |

## BLOCKED / BLOCKED_AWAITING_APPROVAL

System changes, dependency downloads, public Tor tests, real identity
changes, production signing and merges require separate approval. Embedded
and later Windows release phases are deferred.

## PENDING_PHYSICAL_TWO_PC_CHECKS

Repeat pairing, delivery, identity restart and controlled reconnect on
two Windows PCs after the software gates. The original one-host and
operator-assisted observations remain historical evidence.
Clean-host self-contained release qualification, long soak tests and
the original nine public Tor cases are NOT RUN in this cycle.

## NEXT_ACTION

Review #19 and the four stacked drafts in dependency order. With separate
public Tor authorization and two Windows x64 PCs, use the
[two-PC GUI procedure](WINDOWS-PEER.md#two-physical-pcs-with-the-graphical-workflow)
and a fresh profile per endpoint. Record pinned app and Tor hashes, both
bootstraps, authenticated bidirectional delivery, one delivered copy after
reconnect and clean shutdown. Keep private state on each endpoint.

The cycle stops at four draft PRs. TASK 12 remains a separately reviewed
stack and requires integration review where its desktop files overlap.
