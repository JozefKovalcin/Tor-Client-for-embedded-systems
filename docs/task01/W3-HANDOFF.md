# W3 — Native Linux build preflight

Scope: TASK 01 W3 only; depends on W1 contracts and W2 read-only probe API.
Primary physical v1 is PICO-IMX6UL; its unresolved SKU/carrier/DTB and future
Yocto build RAM do not gate these native-PC planning checks. Existing Tor,
Windows patch, Buildroot configuration, source locks, tests and evidence remain
unchanged. No compiler/configure, network test, install, download or flash ran.

## Changed files and behavior

- `config/native-linux/v1.json`: selected Ubuntu 24.04 x86_64-linux-gnu,
  glibc **2.39** ABI. Other ABIs/distro versions require a separate policy;
  no implicit musl or newer-glibc fallback. Initial native-source 2 GiB RAM /
  8 GiB disk budget is an ASSUMPTION, not measured minimum or Yocto budget.
- `scripts/native_linux/{__init__.py,__main__.py,preflight.py}`: read-only CLI
  and reusable `preflight(source_dir, output_dir, archive_cache)` API. Uses W2
  allowlisted version/target/package probes and W1 validation. Reports the
  five dependency statuses, full checks, source/license observations and a
  descriptive topological build DAG whose stages remain `NOT RUN`.
- `tests/test_native_linux.py`: synthetic behavior and cached archive tests,
  real W2 collection with injected facts, and Windows CLI blocked smoke.
- This compact handoff; integrator owns `docs/PROGRESS.md` and PR summaries.

CLI, from the checkout root (use `-B` to suppress Python import caches):

```sh
python3 -B -m scripts.native_linux \
  --source-dir /home/user/embedded-tor \
  --output-dir /home/user/tor-native-output/new-input \
  --archive-cache /home/user/tor-source-cache
```

JSON goes to stdout; exit 0 means all listed prerequisites permit a plan, exit
1 means prerequisites unavailable, exit 2 means invalid policy/metadata/input.
No output directory or report file is created. A valid `BuildRequest` appears
only when every required check and clean committed source provenance is
available. A plan never proves a successful build or runtime qualification.

Local cached Tor/OpenSSL/libevent/zlib archive and license pins are cross-checked
against the existing Windows manifest, upstream hash files and Tor version/epoch
lock; libseccomp is read from its existing upstream hash file. Archive SHA-256
and the unique bounded regular license member are verified without extraction.
Windows-only patches are explicitly excluded. Unknown source signatures retain
their existing unverified status. Denied reads remain BLOCKED, absence MISSING,
hash/unsafe-member mismatch INCOMPATIBLE. Unsafe source/cache paths short-circuit
archive/Git reads, including runtime-state and symlink paths.

Stable input metadata hashes source pins/lock inputs, selected policy,
toolchain version/path observations and adapter source. Timestamps, currently
free RAM/disk and observation prose are excluded from that hash. Source epoch
is derived from existing metadata. Installed pkg-config libraries are optional
diagnostics; the future production build must use its private pinned prefix.
Required libc header presence is observed without compilation/linking. Resource
gates consider current/ancestor cgroup-v2 limits; unavailable limit evidence
cannot silently pass. Resource observations do not hold a real reservation.

## Validation and evidence

Pre-integration independent check, using `PYTHONPATH` read-only references to
the W1 and W2 worktree modules (no shared files copied or edited):

```text
python -B -m unittest discover -s tests -p test_native_linux.py -v
15 tests PASS, 0 skipped, exit 0
```

Covered: valid synthetic BuildRequest and DAG, wrong ABI/distro/architecture/
compiler target, every unavailable dependency status, native-vs-Yocto budget
separation, unknown/nonfinite resources, stable hashes, unchanged source/license
pins, unsafe paths, cgroup restriction/denial, verified unbounded root and namespace mismatch, archive hash/license checks,
duplicate/link/oversized members, source-read denial, W2 API wiring, Git environment redirection rejection, and a real
native Windows CLI `NOT RUN`/exit 1 with no output directory created.

W1 → W2 → W3 isolated worktree integration and broad repository results are
separate from those preliminary results. After fast-forward integration of W2
commit `239e5ad47de360a2c1b603a16f9456c97c43a957` (including W1), the same command
was re-executed in the isolated W3 checkout with `PYTHONPATH` removed:
**15 PASS, 0 skipped, exit 0**. This includes the real `-B` Windows CLI blocked
smoke and assertion that no output/cache files are created. `git diff --check`
also passed. The integrator records the subsequent broad repository suite.
Syntax parsing of the new modules/test also passed. Native Linux filesystem/runtime checks,
source compilation, ELF linkage, public Tor, Yocto and physical board tests are
NOT RUN; no historical acceptance outcome is replaced.

## Open limits and downstream dependencies

- Linux-positive readiness is synthetic test evidence only. A supported native
  Linux host with cached exact sources is needed for actual positive preflight.
- Cgroup-v1 or unrecognized namespaces intentionally leave memory UNKNOWN;
  additional limit backends need a separate reviewed extension, not a fallback
  claiming unrestricted capacity.
- Header/pkg-config/version observations do not prove configure, compile or
  runtime ABI compatibility. W6 is outside this task; no executor exists here.
- Toolchain metadata fingerprints observations and policy, not a hermetic SDK
  or bit-identical output promise. Exact distro package revisions/build output
  provenance remain future build work.
- Flash, deployment, pairing, GUI and additional ABI support remain outside W3.

## Parent final verification

- Full `python -B scripts/check.py`, outside known sandbox restrictions:
  exit 0, **105 discovered / 103 PASS / 2 platform SKIPPED**. Full output
  retained in ignored `build/task01-checks/W3-portable.txt`.
- `python -B -m unittest discover -s windows-native/tests -v`: **6 PASS**.
- All 566 protected Git inputs unchanged in each implementation worktree.
  Initial raw-byte audit flagged 36 checkout-only line-ending differences per
  worktree; EOL-normalized contents match with no other differences. Original
  checkout files, exact stashes, main and baseline unchanged.
- Whitespace/scope review PASS. No later implementation phase started.
