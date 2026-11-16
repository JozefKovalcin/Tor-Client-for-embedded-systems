# Version 1 contracts

Eight self-contained JSON Schema 2020-12 documents are under `v1/`, matching
the reviewed `docs/architecture/01-system-architecture.md`. W1 owns these
schemas and `scripts/contracts.py`; W2 and W3 consume the public API:

```python
from scripts.contracts import validate_contract, require_valid
errors = validate_contract("DependencyCheck", instance)  # [] means valid
require_valid("DependencyCheck", instance)              # ValueError on failure
```

Run from repository root, without downloads or installations:

```sh
python -B scripts/contracts.py TaskProgress tests/fixtures/contracts/valid/TaskProgress.json
python -B -m unittest discover -s tests -p test_contracts.py -v
```

CLI exit 0 means contract validity; 1 means invalid/unreadable input; argparse
returns 2 for incorrect usage. JSON output contains `valid` and diagnostics,
never input values. Contract validity does not execute a test, compare measured
performance, verify a signature, prove a physical board, or grant consent.

The validator is an explicitly bounded **stdlib JSON Schema subset**, not a
general implementation. It enforces every keyword used by these schemas:
`$schema`, `$id`, `$defs`, `$ref`, `title`, `description`, `type`, `properties`,
`required`, `additionalProperties`, `items`, `enum`, `const`, `minimum`,
`maximum`, `exclusiveMinimum`, `minLength`, `maxLength`, `minItems`,
`uniqueItems`, `pattern`, `format`, `anyOf`, `allOf`, `not`. Annotation keywords
are accepted as annotations. Only local `#/$defs/Name` references resolve;
no network/file reference fetch occurs. Unknown keywords, dialects, formats,
contract names and schema versions fail closed. Date-time is asserted and
must be UTC (`Z` or `+00:00`), not merely treated as an annotation. Repository
patterns use the shared Python/ECMAScript-compatible regex subset.

`validate_contract` additionally enforces cross-field invariants that plain
JSON Schema cannot express (completed <= total, null measurement reasons,
matching nested policy/target/protocol/scope, signature evidence and truthful
execution metadata). Consumers must call this API, not rely on structural
validation alone. Accepted evidence requires all nine mandatory PASS results,
online modes for the six public assertions and all required measured fields,
but validity **does not recompute** policy comparison or establish measurement
authenticity. A caller must still run the existing comparator on equivalent
conditions; no acceptance threshold changes here.

Dependency status is exactly AVAILABLE/MISSING/INCOMPATIBLE/UNKNOWN/BLOCKED.
AVAILABLE needs verified compatibility and any expected hash must match an
observed hash. BLOCKED means denied/timed-out/unexecuted probe, not missing
software. Missing, incompatible and unknown never become PASS. `TaskProgress`
uses explicit `interrupted` with `app-crash`, `runner-crash` or `runner-lost`;
unknown exits remain null. Retry/parent cannot reuse the current task ID.
Progress sequence monotonicity and lifecycle reconciliation require task
history and remain the later orchestrator's responsibility.

Selected target `linux-x86_64-glibc239-v1` is x86_64-linux-gnu/glibc 2.39.
Generic native Linux descriptors may describe other explicit glibc/musl ABI
versions, without implying any supported preflight/build policy. Other ABIs
require a future target policy; W3 accepts only the selected v1 policy and never
silently falls back. No additional ABI implementation is included. BuildRequest
rejects simultaneous Yocto/Buildroot profiles and native-Linux image profiles.
Complete arbitrary adapter-to-target applicability, output-directory ownership,
symlink containment, source/digest verification and resource admission need
the consumer's target registry/preflight; opaque IDs do not prove them.

Refs are scoped identifiers or safe POSIX relative references: no absolute
paths, URLs, drive prefixes, traversal or known private-state/key paths. This
is a transport validation boundary, not a replacement for export redaction or
filesystem containment. No private state or measurement is fabricated.

All fixtures under `tests/fixtures/contracts/` are synthetic, with explicit
synthetic IDs, year-2000 timestamps and placeholder zero digests. They are
validation inputs, never benchmark/qualification reports. Valid fixtures
include interrupted tasks, blocked devices/probes and NOT RUN evidence with
null measurements. Invalid fixtures exercise semantic as well as structural
failures. Existing snake_case legacy results retain their original schema,
bytes/statuses/nulls and loader; W1 neither rewrites nor silently upgrades them.
Future adapters must preserve raw legacy bytes/digest in a separate envelope.
