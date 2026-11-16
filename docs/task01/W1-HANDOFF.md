# W1 — Contracts and fixtures handoff

Scope: TASK 01 W1 only, based on reviewed documentation commit `67b9d423`.
Branch: `codex/task01-w1-contracts`. W1 owns shared contracts; W2 consumes W1,
then W3 consumes W2. No implementation from W4–W15 is included.

## Files and implemented behavior

- `config/contracts/v1/{TargetDescriptor,DependencyCheck,BuildRequest,TaskProgress,BuildArtifact,DeviceInfo,TestResult,EvidenceManifest}.schema.json`:
  eight self-contained, strict, versioned JSON Schema 2020-12 documents.
- `config/contracts/README.md`: contract/API/CLI boundary, exact stdlib subset,
  semantic invariants, reference restrictions and legacy compatibility.
- `scripts/contracts.py`: read-only offline schema/API/CLI validator. Public
  `validate_contract(name, instance) -> list[str]` and
  `require_valid(name, instance) -> None/ValueError`.
- `tests/fixtures/contracts/{valid,invalid}/`: sixteen synthetic fixtures,
  including interrupted runner, blocked probe/device and unexecuted/null-metric
  evidence; no fixture is benchmark or physical-board evidence.
- `tests/test_contracts.py`: 23 behavior tests including nested evidence,
  unknown versions/keywords/remote references, invalid types/nonfinite values,
  private-state/path rejection, crash reasons, execution metadata and CLI exits.
- This handoff. Parent integrator owns `docs/PROGRESS.md` and PR metadata.

Dependency status is exactly AVAILABLE/MISSING/INCOMPATIBLE/UNKNOWN/BLOCKED;
AVAILABLE requires verified compatibility, and an expected hash cannot be
missing/mismatched. Versionless architecture/resource checks remain valid.
TaskProgress explicitly represents interruption after app/runner crash/loss,
with unknown exit observations null. Selected target `linux-x86_64-glibc239-v1`
requires x86_64/glibc 2.39. Generic descriptors can describe another explicit
ABI without implying support; W3 separately restricts its supported policy.
Known native image/profile contradictions are rejected. Evidence rejects
mismatched target/policy/protocol/scope and incomplete mandatory online results
labelled ACCEPTED. Existing policy and legacy snake_case reports are unchanged.

## Executed checks

| Command | Result |
| --- | --- |
| `python -B -m unittest discover -s tests -p test_contracts.py -v` | PASS: 23 tests, no skips. |
| `python -B scripts/contracts.py TaskProgress tests/fixtures/contracts/valid/TaskProgress.json` | PASS: JSON `valid:true`, exit 0. |
| `python -B scripts/contracts.py EvidenceManifest tests/fixtures/contracts/valid/EvidenceManifest.json` | PASS: JSON `valid:true`, exit 0. |
| CLI valid and invalid TaskProgress tests within suite | PASS: respective exits 0/1, no input value echo. |
| Historical `reports/arm-2026-11-05/offline-128.json` compatibility check | PASS: readable, original PASS/SKIPPED statuses and bytes preserved. |
| `python -B scripts/check.py` (parent, outside known sandbox restrictions) | PASS: exit 0, 66 discovered, 64 PASS / 2 platform SKIPPED. Full output retained in ignored `build/task01-checks/W1-portable.txt`. |
| Git scope / preservation / whitespace review | PASS: only new W1 files and additive PROGRESS evidence; existing protected inputs unchanged. |
| Tor/native compilation, Buildroot/Yocto, public network, physical tests | NOT RUN: outside TASK 01 W1. |

## Limits / dependencies

The stdlib validator supports only explicitly documented keywords and local
in-document schema references, failing closed otherwise. Structural schema
validation alone does not apply every cross-field invariant: consumers must
use the shared API. Schema validity never recomputes the existing comparator,
authenticates measurement facts or proves hardware/signatures. Sequence/history
reconciliation, target registry applicability and filesystem/symlink containment
remain consumer responsibilities; W1 adds no orchestrator or legacy migrator.
All fixtures are synthetic. Exact PICO SKU/carrier/DTB remain BLOCKED; Yocto RAM
does not block W1–W3. No sources, Tor patch, Buildroot configuration, historical
results, thresholds, existing tests, stashes or baseline were changed.
