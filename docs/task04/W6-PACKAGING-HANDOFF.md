# W6 packaging inventory/design handoff — TASK 04, 2026-11-17

Branch: `codex/task04-w6-linux-packaging`.
Dependent base: `codex/task04-w6-linux-executor`, integrated ordinarily through
`df733a58fcf9972714d9e0b566dca5d9c9bd0485`. This includes the separately
reviewed W2 GCC-banner fix `fcd8df8c0bd4988d922276c9ac0e06d9d88c834d` via merge
`6b474051880a943b1b2937641a76674963148491`. W2 owns that behavior change; the
packaging PR does not modify checker logic, shared schemas or source locks.

## Changed files and implementation

- `scripts/native_linux/packaging.py`: read-only build-reference verification,
  immutable bounded snapshots, pure ELF64 metadata parser, runtime dependency
  inventory and explicit packaging allowlist. No subprocess, ldd, installer,
  downloader, executable invocation or package-writing operation.
- `scripts/native_linux/PACKAGING.md`: supported ABI, private-state exclusions,
  trust boundary and unexecuted final packaging/runtime gates.
- `tests/test_native_linux_packaging.py`: eleven synthetic behavior/security
  cases; fixtures are byte arrays with no executable program code.
- This handoff. Common progress/evidence aggregation belongs to the parent.

Only an exact producer-supplied expected build-manifest SHA-256 is accepted.
Successful finished build, deterministic input digest, W1 build-mode request,
selected source/license pins/epoch and all fifteen command/log outcomes must
agree. Runtime candidate path is strictly `prefix/bin/tor`; its size/hash and
every pinned source license are rechecked. A supplied hash alone does not
authenticate an otherwise untrusted producer.

The bounded ELF parser checks x86_64/ELF64/LE, program/dynamic/string ranges,
duplicate critical fields, interpreter/PIE, non-executable stack, RELRO/NOW,
absence of text relocation and embedded search paths, and a conservative
declared system-SONAME inventory. Both **DT_TEXTREL and DT_FLAGS/DF_TEXTREL**
are rejected. The regression for flag-only text relocation retains NOW through
DF_1_NOW so rejection cannot be attributed to a separate missing binding flag.

The proposed package has exactly `bin/tor` plus five individually pinned license
files. No recursive prefix copying: identities, DataDirectory, hidden-service
keys, cookies, credentials, runtime state, home/tmp/cache and logs are excluded.
No package or W1 BuildArtifact is fabricated:

```text
packagingExecutionStatus = NOT RUN
packageArtifact = null
readyForDistribution = false
```

## Executed tests

No source configure/make, compilation, actual package, Tor execution, public
network, firmware or hardware operation occurred.

| Check | Result |
| --- | --- |
| Packaging focused unittest on Windows | 11 discovered / 10 PASS / 1 POSIX symlink SKIPPED |
| Final combined W6 full portable suite on Windows | 148 discovered / 141 PASS / 7 platform SKIPPED |
| Final combined W6 full portable suite on native Linux test paths in WSL | 148 discovered / 145 PASS / 3 Windows-only SKIPPED |
| Existing Windows pipeline tests | 6 PASS |
| `git diff --check` | PASS |

Full logs remain in ignored `build/task04-checks/`; native test snapshot is
`/tmp/tor-task04-w6-package-p9njd8rn`. Earlier passing logs and the executor's
DrvFS timestamp-only failure remain preserved. No original tests were edited
or weakened. The native snapshot also executes the actual read-only W3 probe:
available local WSL is Ubuntu 26.04/glibc 2.43 and remains incompatible with
the selected ABI. Its explicit empty cache is not a global missing-archive
claim. Supported native CI observation is collected separately by the executor
lane; synthetic ELF or WSL unit PASS is not native Tor qualification.

Tests cover metadata correctness, wrong ISA/bounds/string/dynamic tables,
missing hardening/search paths/unreviewed dependencies, independent DF_TEXTREL,
untrusted/changed manifest, incomplete/failed commands, changed input/log/binary/
license, unsafe candidate ref, symlink input, exact no-private-state allowlist,
no filesystem mutation, empty successful logs and always-unexecuted distribution.

## Blocked/unverified and next integration

- No real source build or package exists for inventory. Actual packaging and
  runtime qualification remain BLOCKED/NOT RUN pending separately authorized
  supported-host inputs/build and subsequent reviewed packaging implementation.
- ELF metadata PASS does not prove GLIBC symbol compatibility, fortify/stack
  protector coverage, complete runtime closure or clean-host execution. These
  checks and transitive/bundled license-notice review are explicit next gates.
- A future package writer must consume stable reverified snapshots and inspect
  final archive allowlist/digests before emitting a real W1 BuildArtifact.
- Signature status remains under the existing SHA-256 trust boundary; detached
  source/update signatures are not newly verified.
- W5 typed adapter consumes executor W1 progress and verified manifests. W4
  should present the package plan/evidence uncertainty through its UI seam;
  no UI component runs this CLI directly. No W13 integration was implemented.
- Baseline, historical results, source pins, Tor/Windows patches, Buildroot,
  tests/thresholds, stashes and existing worktrees remain preserved. W7–W15 are
  outside TASK 04. No PR merge is authorized here.

Progress insertion for the parent: separate W6 packaging design/inventory is
implemented and tested with synthetic ELF/manifests. It proposes only a hashed
binary/license allowlist, rejects unsafe hardening/dependencies/references and
always reports package/runtime distribution NOT RUN and no artifact.
