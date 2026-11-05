# QEMU capacity experiments

These measurements concern one x86_64 Tor endpoint in QEMU TCG under Ubuntu
WSL2. They do not verify a physical embedded board, its CPU speed, Ethernet,
boot firmware, flash endurance or power-loss behavior. Application buffers and
the board-specific kernel/firmware reservation need separate budgets.

The original regression fixture executes two Tor processes plus Python in the
guest. Its peak guest memory is a laboratory budget, not the memory required
by one production endpoint. Keep that frozen suite and its acceptance history.
The additional `native-one-endpoint-v1` protocol puts the controller and second
Tor endpoint on the host. It uses the preserved production kernel/root disk,
one guest Tor with privilege drop, real writable ext4 state and read-only
SquashFS. A separate read-only diagnostic disk contains a C observer/backend;
it is never included in the production image. Total guest memory includes its
overhead. SAFECOOKIE is verified inside the guest; cookies and private onion
keys never leave it. The diagnostic command port is forwarded only to host
loopback in this isolated laboratory.

All nine named native assertions must execute successfully. Payload contents,
sizes (including 1 MiB), server push, byte comparisons, hard process restart,
same-identity reconnection, 600-second bootstrap and 180-second onion deadlines
are retained. The frozen fixture remains the optimization acceptance test.
Results from different protocols must not be compared as equivalent workloads.

Local measurements transfer three 32 MiB duplex framed workloads through the
native backend, and time the candidate's OpenSSL EVP AES-CTR, SHA256, SHA3,
SHAKE256 and X25519 implementation. These isolate local execution from public
circuits; they do not measure a local Tor network. The optional Chutney attempt
did not converge and is recorded as BLOCKED. Nothing from that attempt is used
as successful performance evidence.

At 4 Hz the observer records MemAvailable, RSS/PSS/HWM, CPU counters, major
faults, reclaim/allocstall, swap, OOM counters, I/O where supported and state
filesystem free space. Phase CPU is recomputed from per-PID counter differences
in raw samples; lifetime counters are not phase durations. Peaks between
samples and long-duration consensus growth remain limitations.

The supplementary RAM planning screen requires at least 20% of usable Linux
MemTotal and 16 MiB absolute MemAvailable at the observed peak. Local scaling
uses the existing 20% performance limit against the same image at 256 MiB.
RAM is deliberately varied here; this is not an equivalent-environment
optimization acceptance. Public performance, artifact and mandatory functional
gates remain separate and unchanged. No missing sample is discarded.

The additional state-capacity screen requires no observed write errors and at
least 4 MiB available during the run. It can reject a nine-test functional PASS;
it does not change the historical functional suite or its recorded outcomes.

## Reproduction

Run on Linux without spaces in the repository path. Use the preserved image
hashes and source build IDs in `config/experiments/capacity-2026-11-05.json`.
The SDK and source locks retain their existing provenance. Full clean builds:

```sh
./scripts/validate.sh --profile qemu-x86_64-selective --jobs 4
./scripts/validate.sh --profile qemu-x86_64-capacity32 --jobs 4
python3 scripts/build_capacity_fixture.py \
  --compiler build/toolchains/baseline-sdk/bin/x86_64-buildroot-linux-musl-gcc \
  --output build/capacity-fixture-new
```

The helper refuses existing fixture paths. A new ext4 UUID/timestamps can change
its disk digest: pin the newly generated digest in the plan and use that same
fixture throughout the experiment. The 2026-11-05 v2 fixture also pins the
previous frozen bulk crypto executable; its exact source/executable manifest
is retained in the capacity report. No diagnostic library replaces target
libraries.

In another terminal, start the pinned native host peer (the preserved full-Tor
build must exist). Its runtime directory is new, private and ignored:

```sh
python3 scripts/capacity_peer.py --directory runtime/capacity-peer-new
```

Then run the grid, ensuring there are no concurrent builds or other QEMUs:

```sh
python3 scripts/capacity_native.py --fixture build/capacity-fixture-new.ext4 \
  --peer-session runtime/capacity-peer-new/session.json \
  --matrix config/experiments/capacity-2026-11-05.json \
  --output reports/benchmarks/capacity-new \
  --memory-mib 256 192 160 128 112 96 --rounds 1
python3 scripts/analyze_capacity.py --native reports/benchmarks/capacity-new \
  --output reports/capacity-new
```

Touch `runtime/capacity-peer-new/stop` to stop only that owned peer. Keep its
private runtime out of Git. Public repetitions use `performance_study.py` with
an explicit predeclared matrix containing every round; retain all failed and
blocked attempts. Paired temporal-round bootstrap intervals describe measured
variability and do not prove public-network reliability.

## Storage accounting

`complete_image_bytes` is the separate kernel plus the raw disk logical bytes.
The disk contains MBR/alignment, compressed root and persistent-state partition.
The state partition is already counted: never add it twice. Sparse allocated
host bytes, compressed download bytes and installed files are different metrics.

The 25.8799 MiB and 24.7354 MiB images each contain a 16 MiB state partition.
During the native workload that partition reached zero available space and
Tor logged failed microdescriptor writes despite passing functional assertions.
The capacity32 experiment preserves the selective kernel/root/Tor design and
allocates 32 MiB state. This increases total storage intentionally; it is not a
claimed size reduction. Retain the smaller images and their historical gates.

Physical flash also needs bootloader/firmware, erase alignment, reserved blocks,
update slots and ongoing state headroom. Short runs cannot establish flash
endurance or bound future consensus/cache sizes. Consult the measured report
for the tested trade-off and qualification status before choosing a board.
