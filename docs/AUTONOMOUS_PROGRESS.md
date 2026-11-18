# Windows messaging development

## CURRENT_STATE / CURRENT_BASE

Remote `main` is `266e29a29cab4b465415d8388614493a661e77f8`.
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
