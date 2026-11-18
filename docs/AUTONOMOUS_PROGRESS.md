# Windows messaging development

## CURRENT_STATE / CURRENT_BASE

Remote `main` is `266e29a29cab4b465415d8388614493a661e77f8`.
Draft PR #19 targets `main` at head `97ccff6a00c886606501f72f58dffbb310de8f99`.
The Windows messaging work starts from that peer head in isolated worktrees.
TASK 12 is a sibling effort stacked as #19 → #20 → #21 → #22 → #23 → #24.
The baseline tag is `da7c5281145589821ceeb4f6b7aabd4d98833587`. Both
original stashes and the root worktree's untracked report are preserved.

## ACTIVE_PHASE / ACTIVE_PR

Phase 0 audit is in PR #25. Phase 1 is in PR #26. Phase 2 adds the peer
delivery protocol and encrypted queue. Phase 3 will connect the desktop.

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

## IMPLEMENTED / COMPLETED_THIS_RUN

The repository backlog now preserves the supplied checklist with explicit
Windows work items. The architecture decisions track the proposed security
boundaries and compatibility before implementation.

Phase 1 adds an explicit first-use P-256 credential protected by Windows
DPAPI, an owner-private trust store, consent bound to the typed fingerprint,
contact selection and durable revocation. Run Only and the existing
transient protocol remain available in the lower-level transport.

## FAILED

No executed gate has failed at the start of the cycle.

## OPEN_PULL_REQUESTS

| PR | Role |
| --- | --- |
| #19 | Mutual TLS peer messaging over Tor, baseline for this cycle |
| #20–#24 | Existing Developer Manager drafts, outside this cycle's ownership |
| #25 | Exact-head baseline, checklist and security decisions |
| #26 | Per-user TLS credential, owner-private contact pins and revocation |

## BLOCKED / BLOCKED_AWAITING_APPROVAL

System changes, dependency downloads, public Tor tests, real identity
changes, production signing and merges require separate approval. Embedded
and later Windows release phases are deferred.

## PENDING_PHYSICAL_TWO_PC_CHECKS

Repeat pairing, delivery, identity restart and controlled reconnect on
two Windows PCs after the software gates. The original one-host and
operator-assisted observations remain historical evidence.

## NEXT_ACTION

Wire the authenticated protocol and per-peer delivery state into the GUI.
Add bounded reconnect for transient transport errors, retain the paired pin
and expose pending, delivered and expired state separately from connectivity.
