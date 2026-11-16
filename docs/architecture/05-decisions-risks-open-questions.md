# 05 — Decisions, risks and open questions

Status: Phase 0 review ledger, 2026-11-16, audited HEAD
`018923de4376fb8c030c670fcb61124ce1386d4b`.
Repository anchors: [AGENTS](../../AGENTS.md), [HANDOFF](../HANDOFF.md),
[PROGRESS](../PROGRESS.md), [ROADMAP](../ROADMAP.md),
`config/policy.json`, `windows-native/source-manifest.json`.
Owner roles below are proposed responsibilities, not claims that people/runners
have been assigned. Unverified implementation choices are labelled explicitly.

## Decision register

| ID / status | Decision and reason | Evidence / reopening condition |
| --- | --- | --- |
| D01 / required | Tor-only; preserve upstream protocol/crypto/TLS/RNG, public bootstrap, SOCKS5, v3 services, bidirectional integrity and same-identity reconnect. No replacement Tor implementation; no bare-metal/RTOS v1 work. | `AGENTS.md`, `config/policy.json`, `tests/run_all.py`. Optional bare-metal feasibility is research only after v1, never a dependency. |
| D02 / required | Keep native Windows and Buildroot x86/ARM reference, immutable tag/sealed outputs, all historical failures and rejection scopes. No threshold or baseline redefinition. | [audit](00-repository-audit.md), reports under `reports/{measured,performance-2026-11-05,capacity-2026-11-05,arm-2026-11-05}/`. New board comparison uses explicitly named evidence/reference. |
| D03 / approved requirement; execution BLOCKED | TechNexion PICO-IMX6UL is the primary physical v1 platform. DHCOM HI00096/picoITX is historical only, not an active parallel target. Exact PICO SoM/carrier/DTB remain BLOCKED. | Existing `docs/ARM_PLATFORM.md`/ROADMAP name DHCOM; [03](03-hardware-yocto-plan.md) records official PICO BSP/support and unresolved mapping. Do not overwrite old selection implicitly. |
| D04 / required | Exactly two Yocto image profiles: baseline and minimal, own meta-tor-minimal layer on vendor BSP. Keep Buildroot reference. Yocto profile baseline is not the Git baseline. | TASK 00 requirement, [03](03-hardware-yocto-plan.md); no layer/build currently exists. |
| D05 / required | Approximately 32 MiB immutable system footprint is an aspiration only. Report bootloader/kernel/DTB/root/state/update/recovery budgets separately. | ARM QEMU total includes 32 MiB state; eMMC layout/resource needs UNVERIFIED. No mandatory new size gate. |
| D06 / approved requirement; design ASSUMPTION | Avalonia/.NET PC UI, platform-neutral core, separate Windows/Linux/WSL/device adapters; no .NET runtime requirement on embedded board. | No tracked .NET code; [01](01-system-architecture.md). Exact SDK/TFM/Avalonia versions and Linux distro support need W4 decision/pins. |
| D07 / required | Self-contained run-only packages; opt-in managed external build tools for Developer/Embedded. Portable runtime does not imply portable compiler/BSP or no OS dependencies. | [02](02-dependency-matrix.md); [Microsoft publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/) and [Avalonia platforms](https://docs.avaloniaui.net/docs/supported-platforms). |
| D08 / conditional ASSUMPTION | USB Ethernet management only after OTG/carrier/kernel/Windows proof; Ethernet for public Tor. Paired mutual authentication, loopback Tor control and bounded SOCKS/application bridge. | H0–H5 in [03](03-hardware-yocto-plan.md). No unpaired access, USB↔Ethernet forwarding or default-route/DNS override. |
| D09 / required | State/identity stays on owning endpoint, never in firmware/evidence. Local desktop mode has separate local state. Deployment must preserve device identity, validate target and permit rollback. | Existing protected state/init configuration; [01](01-system-architecture.md), [03](03-hardware-yocto-plan.md). Exact update solution BLOCKED on storage/bootloader facts. |
| D10 / required | Reliable phase progress only; indeterminate otherwise. Persist task state/logs, cancellation outcome, original exit code, retry lineage and evidence scope. | Existing validator exit 1 includes rejected/non-comparable outcomes; [contracts](01-system-architecture.md) prevent false UI success. |
| D11 / required | Read-only checks before install plans; installation and flashing require explicit scoped consent. No system changes during discovery. | `windows-native/scripts/compile.sh` currently auto-installs; integration must gate that path. Phase 0 did not change it. |
| D12 / required | Versioned source hashes/licenses remain mandatory. Signature status must reflect actual verification; future signed updates require provisioned verified trust roots. | Existing source manifests explicitly disclaim detached signature verification. No invented signature PASS. |

## Risk / unknown ledger

| ID / severity / status | Impact | Mitigation / owner | Evidence needed / unlock |
| --- | --- | --- | --- |
| R01 / critical / BLOCKED | Wrong PICO SKU/carrier/DDR/DTB may prevent boot or damage interfaces; candidate capacities are not physical facts. | Hardware custodian: acquire full part/revision, schematics and safe power/UART/recovery information before board work. | H0 inventory/photos with sensitive identifiers redacted; voltage/pin verification; UART-reported RAM/SoC/storage. |
| R02 / high / BLOCKED | BSP release says PI-FL; kit/README labels differ. Generic `pico-imx6ul` machine may select wrong carrier/DTB. | BSP owner: inspect immutable manifest, machine, kernel and U-Boot mappings; resolve vendor naming. | Matching DTB source/hash and configuration for exact board, vendor confirmation if ambiguity remains. Developer portal/raw config access was unavailable during research. |
| R03 / high / UNVERIFIED | OTG feature may not be routed/usable in device mode; power shares connector; Windows driver setup may disrupt USB access. | USB owner: electrical/role/UDC audit and H4 transport matrix before enabling gadget. No universal ECM/NCM/RNDIS assumption. | Data+power schematic, actual enumeration/replug/suspend records on selected Windows/Linux versions with simultaneous Ethernet. |
| R04 / critical / BLOCKED | Wrong eMMC offsets/layout or interrupted update can brick board or erase Tor identity. | Storage owner: boot areas/user areas, recovery and A/B plan; explicit deployment approval, inactive-slot verification, rollback-compatible state. | Actual eMMC part/geometry/boot config, signed bundle and rejected-wrong-target tests, controlled power-loss results and recovery logs. |
| R05 / high / UNVERIFIED | Tor cache/state growth, writes or log exhaustion can cause ENOSPC and loss of service/identity. | Runtime/storage owner: separate state, private permissions, bounded logs, reserve/headroom/error monitoring. Do not assume AvoidDiskWrites prevents writes. | Historical ENOSPC in [capacity report](../../reports/capacity-2026-11-05/README.md); physical cache-refresh/long traffic/min-free/error/endurance measurements. 32 MiB is a reference, not a final minimum. |
| R06 / critical / UNVERIFIED | Public control/agent exposure, unpaired USB clients or broad proxy forwarding bypass intended management boundary. | Security/device owner: mutually authenticated pairing/revocation, allowlisted RPC, loopback SAFECOOKIE, IPv4/IPv6 interface firewall, no shell/secret export. | Negative auth, replay/revoked credential, wrong-peer, listener and network-isolation tests; protocol review before deployment. USB itself provides no authentication. |
| R07 / high / UNVERIFIED | Cold time/entropy failures block TLS/bootstrap or tempt unsafe workarounds. | BSP/runtime owner: RTC/time synchronization and kernel CSPRNG readiness; fail closed and report BLOCKED, preserve Tor validation. | H2 serial evidence on cold boot/no-network/no-battery cases, before/after time source and RNG readiness. |
| R08 / high / known gap | Run-only portability can be confused with needing MSYS2/SDK/WSL; Windows build launcher can silently install packages. | Desktop/dependency owner: separate modes and read-only probes, reviewable install commands/ownership/privilege/reboot, direct non-installing build adapter after consent. | Clean-host launch without SDK; no install in probe mode; approval-boundary behavior test. See `windows-native/scripts/compile.sh`. |
| R09 / high / UNVERIFIED | Yocto host requirements/WSL limits and disk caches may exceed actual available resources; parallel jobs can exhaust RAM. | Runner owner: one heavy build/host, reservation and cancellation, native Linux ext4 no-space path; dedicated runner if budgets fail. | Linux/WSL resource probe and measured peak build disk/RAM; [04](04-task-dag-and-codex-execution.md) conservative budget currently not met for Yocto RAM. No runner dispatched. |
| R10 / high / UNVERIFIED | Vendor BSP libc/kernel/Tor recipes may conflict with pinned Buildroot versions/security capabilities. | BSP/dependency owner: compatibility/provenance/license lock before build; review version change separately; preserve Linux hardening/seccomp and upstream security features. | Resolved recipe versions, source/license hashes, actual flags/config, supported host manifest and build/functional results; no QA bypass. |
| R11 / high / UNVERIFIED | Pinned hashes alone do not establish independent source signatures; signing keys/update trust may be absent. | Supply-chain owner: document current HTTPS/hash boundary; verify detached signatures with audited keys when introduced; fail closed on update signatures and wrong target. | `source-manifest.json`, `config/sources.lock.json`, public key provenance/revocation procedure and signature failure tests. No secrets stored in repo. |
| R12 / medium / open product decision | Linux GUI may manage only embedded devices; local Linux Tor adds packaging/support cost even if not essential to UI workflow. | Product owner: retain approved native Linux runtime capability, decide v1 UI mode and supported distro/ABI separately. Do not drop native Linux build requirement silently. | User decision plus native host build/package and clean-host runtime evidence; Buildroot/QEMU does not supply this. |
| R13 / high / UNVERIFIED | GUI/framework and agent/tunnel runtime budgets may consume target RAM/CPU and invalidate minimum-hardware expectations. | Desktop/device owner: select pinned .NET/Avalonia for PC, small native agent/library after review; bounded concurrency/buffers and measured telemetry. | PC package size/working set, board agent+Tor peak RAM/CPU/I/O, throughput and queue/backpressure measurements. All unmeasured values null. |
| R14 / high / known evidence limit | QEMU/public-network variability or cross-ISA results can be presented as physical/causal performance claims. | Validation owner: label simulation, keep equivalent workloads/control, interleaved repetitions/uncertainty and explicit NON_COMPARABLE outcomes. | Existing ARM 128 MiB timing rejection, x86 Windows 180 s smoke timeout and historical slim/selective rejections retained; new physical matched repeated evidence required. |
| R15 / medium / known gap | Windows 10, two actual PCs and long-duration native Windows behavior remain untested; separate memory run was BLOCKED. | Windows validation owner: execute distinct authorized compatibility/reliability gates, keep failed run unmerged. | `windows-native/evidence/{README.md,functional-history.json,functional-memory.json}`; exact OS/package/host evidence. Do not infer from single-host 24H2 PASS. |
| R16 / medium / UNVERIFIED | New contract statuses/units could rewrite historical reports or hide a cancelled/failed build as success. | Contracts/validation owner: additive wrapper, raw-file digests, separate task/test/comparison results and null measurements. | Schema fixtures, legacy-adapter behavioral tests, cancellation/restart/log evidence; `scripts/common.py` schema remains unchanged. |

## Questions to settle before the dependent work

| Question | Proposed working position / next owner action |
| --- | --- |
| Is PICO now the first physical target, or a parallel candidate behind DHCOM? | RESOLVED by TASK 01 review: PICO-IMX6UL is primary physical v1; DHCOM is historical only. Exact hardware facts remain BLOCKED. |
| What exact PICO part and carrier are physically available? | BLOCKED H0. Do not invent an orderable SKU from family specifications. |
| Which BSP release/host and carrier DTB are supportable? | UNVERIFIED candidate in [03](03-hardware-yocto-plan.md); freeze only after mapping/host review, not by choosing latest blindly. |
| Which Windows/Linux versions and local-Tor UI modes ship in v1? | Native Linux v1 reference is Ubuntu 24.04 x86_64-linux-gnu/glibc 2.39; future ABIs remain extensible. Exact .NET/Avalonia pins and local-Tor UI exposure wait for W4/W6. Windows 10 remains a separate evidence gap. |
| Which agent language/RPC/data-tunnel library and pairing UX? | UNVERIFIED. Require existing maintained TLS library, narrow RPC and bounded streams; no new cryptography. Prototype auth/stream/cancel behavior before committing to board footprint. |
| Which eMMC slots, trust roots, recovery path and state migration policy? | BLOCKED H0/H1 facts, storage/security review. No flashing/updater implementation selected by Phase 0. |
| What resource/reliability envelope is acceptable? | UNVERIFIED on PICO: RAM/storage/boot time, USB data rate, thermal/endurance and supported sustained load need explicit measurement plan. Existing 20% timing/5% RSS/zero-growth policies remain unchanged. |

## Phase 0 outcome and next approval

Created six new documents only: repository audit, architecture/contracts,
dependency matrix, hardware/Yocto plan, task DAG/execution plan and this ledger.
Current checks are recorded in [00](00-repository-audit.md): 41 repository unit
PASS / 2 SKIPPED, 6 Windows pipeline PASS after permitted sandbox retries;
Kconfig BLOCKED on Windows. No new native compilation, firmware build, QEMU,
public Tor, Yocto or physical qualification was run. Historical evidence is
explicitly separated from these checks. No commit was requested or made.

The next three independent implementation PRs are:

1. **W1:** versioned eight-contract schemas and synthetic validation fixtures.
2. **W2:** read-only dependency/environment CLI with machine-readable results.
3. **W3:** native Linux x86_64 source-build plan/preflight adapter consuming
   existing pins, with no compilation or download in plan mode.

**Exact approval required before Phase 1:** user accepts this architecture as
the implementation baseline and explicitly authorizes W1–W3 as separate PRs
(contract/config/test additions and the read-only tooling described in
[04](04-task-dag-and-codex-execution.md)). This phase boundary comes from the
user's Phase 0-only request and TASK 00 execution policy, not an inferred need
to approve reversible documentation changes. Suggested approval text:

> Approve Phase 1 limited to W1–W3 in separate PRs, retaining the Tor-only scope,
> current source pins, immutable baseline, policy and frozen acceptance suite.

That approval alone would not authorize installs, heavy builds, public-network
qualification, firmware flashing or hardware procurement. Request a concrete
separate authorization only when such a dependent operation is prepared for
review. TASK 01 explicitly authorizes W1–W3 and the documentation commit/PR. PICO
priority is resolved above; exact hardware and desktop version choices remain
open. Yocto build RAM does not block W1–W3. Dependent later packages remain
outside this authorization.
