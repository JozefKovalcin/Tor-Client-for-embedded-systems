# 03 — PICO hardware and Yocto plan

Status: ASSUMPTION / planning only. **No PICO boot, Yocto build, USB gadget,
eMMC write, flash or physical Tor test has been executed in Phase 0.**
Repository context: [ARM_PLATFORM](../ARM_PLATFORM.md), [ROADMAP](../ROADMAP.md),
[HANDOFF](../HANDOFF.md), `br2-external/configs/tor_qemu_arm_capacity32_defconfig`,
`config/profiles/qemu-arm-capacity32.json`,
[ARM evidence](../../reports/arm-2026-11-05/README.md).

## Hardware/BSP evidence and unresolved mapping

External primary sources were inspected on 2026-11-16. Their support statements
describe products/software; they do not verify the user's physical assembly.

| Source | Supported statement / boundary |
| --- | --- |
| [TechNexion SoM product/part codes](https://www.technexion.com/products/system-on-modules/pico/pico-imx6ull/) | UL/ULL family Cortex-A7; part codes distinguish 528/900 MHz, 256/512 MB/1 GB DDR3L and 4 GB eMMC. Candidate 528 MHz/512 MB/4 GB remains UNVERIFIED until full ordered part number and physical configuration are recorded. |
| [PICO-PI kit](https://technexion.com/products/system-on-modules/evk/pico-pi-imx6ull/) | Documents 512 MB DDR3L, 4 GB eMMC, Fast Ethernet PHY and Type-C OTG; this is a kit/carrier claim, not evidence every PICO carrier routes those signals. |
| [SoM hardware manual v1.02, Rev B1](https://www.technexion.com/wp-content/uploads/2021/06/manual-pico-imx6ull-rev102.pdf) | Separates module pinmux/interfaces, USB host/OTG and UART electrical connections. Match revision/schematic before attaching a UART adapter or assuming Ethernet/OTG wiring. A SoC feature diagram is insufficient. |
| [PICO-PI quickstart](https://www.technexion.com/wp-content/uploads/2021/01/qig-pico-pi-imx6ull.pdf) | Identifies kit connectors and eMMC boot jumper configuration. Use only after matching actual carrier revision; it does not prove a functioning gadget driver or Windows enumeration. |
| [TechNexion manifest README](https://github.com/TechNexion/tn-imx-yocto-manifest) | Observed branch `wrynose_6.18.y-stable`, XML `imx-6.18.37-2.1.0.xml`, machine `pico-imx6ul` covering UL/ULL. README lists BASEBOARD values including `pi`; exact DTB selection needs source/config audit. |
| [BSP releases](https://github.com/TechNexion/meta-tn-imx-bsp/releases) | 2026Q4 release `tn-wrynose_6.18.37-2.1.0_20261012` lists PICO UL/ULL 256/512 MB **with PI-FL**. Published kernel revision `3aa0bb46809d994656c787d0f9afce7f6bb5a957`, U-Boot `849aa387f9d5e690e497c8785a8d177244bcf320`. These are candidate provenance, not repository locks or successful builds. |

**BLOCKED:** reconcile release-list PI-FL with the PICO-PI kit/carrier and
README BASEBOARD naming. Developer portal 2026Q4 page and attempted raw machine
configuration URL were inaccessible through the research tool; no exact DTB,
USB role configuration or matching carrier source was verified. Before freezing
the BSP, inspect machine/kernel/U-Boot sources at immutable revisions and record
the actual carrier-to-DTB mapping. Avoid selecting a moving default branch.

Existing repo milestone is **DHCOM i.MX6ULL HI00096 + picoITX, 256 MiB DDR,
128 MiB raw NAND plus boot SPI**, not TechNexion PICO/eMMC. Keep its historical
selection/evidence intact. TASK 01 review selects TechNexion PICO-IMX6UL as the **primary physical v1
platform**. DHCOM is a historical candidate, not an active parallel target.
The older ROADMAP/HANDOFF entries are retained as historical source context;
this reviewed decision governs new v1 planning. Exact PICO SKU/carrier/DTB
remain BLOCKED; no board implementation is authorized in W1–W3. QEMU's PL011/PL031/VirtIO are unrelated physical peripherals.

## Required facts and go/no-go gates

All physical gates below are **NOT RUN / BLOCKED** until the named inputs and
evidence exist. Future public testing belongs only to explicitly authorized
qualification gates, not Phase 0 preflight.

| Gate / owner | Prerequisites / facts to acquire | GO evidence; otherwise NO-GO |
| --- | --- | --- |
| H0 — hardware custodian | Full SoM part number/UL vs ULL, PCB revision, carrier model/revision/schematics, CPU speed/fuses, DDR part/config, storage part/size, power supply/cable, UART voltage/pins, recovery straps | Sanitized hardware inventory and schematic references; no inferred capacities. Confirm simultaneous Ethernet and OTG data path and a safe recovery route before powering/flashing. |
| H1 — BSP/bring-up engineer | Frozen vendor manifest; DDR training/U-Boot configuration; boot media, exact kernel+DTB hashes, UART configuration | UART capture from power-on through login, detected CPU/RAM/storage, boot source and DTB match; boot from removable/recoverable media before touching eMMC. UART baud/logic voltage UNVERIFIED until manual/revision confirmed. |
| H2 — network/runtime engineer | Correct Ethernet PHY reset/clock/pinmux, cable/switch/DHCP; time source/RTC and entropy driver | Serial + command evidence of Ethernet link/DHCP/default route, correct UTC source and synchronization, initialized CSPRNG before Tor/TLS. Handle cold boot/no battery; no security bypass for time or RNG. |
| H3 — Tor integration engineer | Pinned Tor and libraries, least privilege, private mounted state, safe loopback listeners/control, H2 | Production daemon public bootstrap reaches 100%, real SOCKS5 and v3 service checks with authenticated local control. H3 alone is not the nine-function or two-board qualification. |
| H4 — USB/platform engineer | OTG controller/pins/VBUS/device role/Type-C power arrangements, matching DTB+kernel UDC/configfs/functions, Windows/Linux driver choice, paired agent | USB enumeration, correct data role, power stability, repeat disconnect/reconnect, management authentication and IPv4/IPv6 isolation while Ethernet stays up. No default route/DNS leakage/forwarding. |
| H5 — validation engineer | Two actual endpoints, two PC hosts, paired USB links, service/data bridge, matching protocol/configuration | Unchanged nine required assertions, both directions/1 MiB hashes, restart with same onion identity, persistent state and sanitized serial/device manifests from both boards. USB drop/reconnect must not expose control or keys. |
| H6 — storage/reliability engineer | Reviewed eMMC partition/boot layout, update/recovery plan, backups kept locally, power-cut equipment and procedure | Repeated cold boots, cache-refresh peak/headroom, long traffic/thermal/resource data, controlled interrupted writes/updates and successful recovery/rollback without identity loss. Include endurance/write amplification and degraded/full-state behavior. |

Minimum ordered bring-up: H0 → UART/boot kernel+DTB → Ethernet → time/entropy
→ public Tor bootstrap → USB gadget/authentication → paired endpoints → state
and power-loss qualification. H5 may initially use removable state; production
eMMC claims must wait for H6 and the exact deployed layout.

## Conditional USB design

USB Ethernet gadget is a **conditional proposal**, not a tested capability.
Confirm USB1/OTG pin routing and device role on the actual SoM/carrier, power
budget/VBUS/back-feed behavior, bootloader role, kernel UDC and DTB `dr_mode`.
Inspect [Linux configfs gadget documentation](https://www.kernel.org/doc/html/latest/usb/gadget_configfs.html)
for supported composition, then select ECM/NCM/RNDIS only after matching Windows
versions, Linux support and driver/signing constraints. No universal Windows
compatibility is claimed. Legitimate VID/PID provisioning and unique stable MACs
are required; do not borrow another vendor's identifiers.

The interface receives a dedicated management subnet with collision detection,
no gateway/DNS advertisement, and a firewall restricted to the paired host.
Agent authenticates both directions; Tor control/cookie stay device-loopback.
See [system design](01-system-architecture.md) for authenticated SOCKS/application
tunnels. Test Windows suspend/resume, cable replug, changing host address,
firewall, driver absence, power-only cable and Ethernet concurrency. UART remains
diagnostic fallback; no silent public-Ethernet management service.

## Yocto strategy — exactly two image profiles

ASSUMPTION: start from the verified-for-this-board vendor BSP and add our own
`yocto/meta-tor-minimal/`; keep Buildroot/QEMU reference and policy unchanged.
There are **exactly TWO new Yocto image profiles: `baseline` and `minimal`**.
The name `baseline` here is a Yocto profile label; it never replaces Git tag
`baseline`, `reports/baseline/` or a historical acceptance reference.

| Composition | `baseline` (`tor-image-baseline`) | `minimal` (`tor-image-minimal`) |
| --- | --- | --- |
| Purpose | Reproducible board integration/control image with retained diagnostics | Remove reviewed nonessential packages after baseline physical evidence |
| Common mandatory runtime | Board-supported bootloader/kernel/DTB, Ethernet, time/RTC/entropy, read-only root, protected writable state, Tor+OpenSSL/libevent/zlib, Linux hardening/seccomp, paired agent, USB only once H4 passes | Same security/features, state/identity and update/recovery behavior; no crypto/TLS/entropy/control reductions |
| Diagnostics | Bounded serial/health/storage/USB diagnostics and manifest export; no desktop UI, dev SDK or production Python test fixture | Retain enough health/rollback diagnostics; remove only measured unused tools, docs and optional features |
| Filesystem | Read-only root (candidate SquashFS with measured compression); tmpfs for `/run`/temporary logs; writable eMMC state | Same mount/security semantics; storage budget measured independently |
| Build/test relationship | Frozen vendor versions/tune/libc and controlled base for same-board comparison | Same toolchain, workload, hardware, network conditions and reference; isolate one reduction per experiment |
| Acceptance | Functional/physical gates; establish new explicitly named board reference without overwriting old one | All mandatory tests and equivalent-condition comparisons; historical zero-growth/RSS/timing rules remain unchanged |

Future tracked layer layout (not created in Phase 0):

```text
yocto/meta-tor-minimal/
  conf/layer.conf
  conf/distro/tor-minimal.conf
  recipes-core/images/tor-image-baseline.bb
  recipes-core/images/tor-image-minimal.bb
  recipes-core/packagegroups/packagegroup-tor-runtime.bb
  recipes-connectivity/tor/tor_%.bbappend
  recipes-connectivity/tor/files/                 # public config/init, no state
  recipes-support/tor-agent/tor-agent.bb
  recipes-bsp/                                  # reviewed boot/layout integration
  wic/                                         # layout after hardware review
```

Freeze manifest XML and all layer commits, machine, BASEBOARD mapping, distro,
kernel/U-Boot/DTB, compiler, tune, libc, recipe versions and license digests
under future `config/` locks. Add machine override only if vendor configuration
is insufficient; do not fork BSP wholesale. Prefer existing compatible Tor
recipe with a narrow append. If its version/dependencies differ from repo pins,
record the conflict and add a reviewed pinned recipe/source hash/license record
in a separate implementation PR. No package upgrades or Windows-only patch
propagation are implied here. `LAYERSERIES_COMPAT` follows the selected release;
unverified compatibility is BLOCKED, not suppressed QA.

Use explicit source checksums, immutable SRCREV and `LIC_FILES_CHKSUM`; preserve
HTTPS/hash verification and add source signatures only with explicit verified
key provenance. Emit package/license/source/config/toolchain manifests. No
`AUTOREV`, unverified mirror binary, inherited host library or unexplained QA
waiver. Baseline records actual configure flags and effective configs; Buildroot
fragment-cache experience shows requested settings alone are not evidence.

[Yocto read-only rootfs](https://docs.yoctoproject.org/6.0/security-manual/read-only-rootfs.html)
requires all writes to be redirected and post-install actions to complete during
image creation. Validate service mounts/order, time seed, DHCP state, machine
ID, agent credentials, Tor cache/identity and log rotation explicitly. Do not
enable delayed first-boot root writes to disguise a failed read-only design.
Init manager/libc choice remains UNVERIFIED until BSP integration review; the
baseline/minimal pair must use the same choices.

## eMMC, footprint and delivery

The approximately **32 MiB read-only footprint is an aspirational measurement
target**, neither an acceptance threshold nor a predicted image size. It refers
to a separately reported immutable system budget; measurement definition must
state whether kernel/DTB are included. No PICO size/resource value is measured.

| Budget to report separately | Required facts / proposed policy |
| --- | --- |
| Boot ROM / SPL / U-Boot / environment | Actual eMMC boot areas vs user area, redundancy, reserved offsets, boot selection, DTB; never overwrite from generic QEMU MBR layout. |
| Kernel / DTB / immutable root | Each compressed/deployed artifact and logical size separately; identify shared blocks/slots and alignment to avoid double counting. |
| Tor persistent state | Dedicated `/var/lib/tor`, private UID/mode; 32 MiB is an existing QEMU block-state reference, not a proven PICO long-duration minimum. Size from transient cache growth, free-space floor and write-error observations. |
| Other writable state | Agent pairing keys/update metadata and necessary OS persistence separate from Tor; bounded tmpfs/logs with RAM cost measured. No shared factory identity or image-seeded secret. |
| Updates and recovery | Proposal: inactive A/B system slots, signature/target/layout checks, atomic boot metadata, trial boot counter and known-good recovery; exact mechanism BLOCKED on bootloader/eMMC review. State format must remain rollback-compatible or use a separately reviewed migration. |
| Usable storage / endurance | Actual eMMC part/capacity/reserves, erase alignment, boot partitions, filesystem overhead, wear and retained failed update space; nominal 4 GB does not establish usable layout. |

Build an update bundle with no private state. Verify signed manifest and hashes,
exact board/carrier/layout compatibility and power conditions; present target,
artifact and recovery plan for explicit flash approval. Verify inactive slot
after writing, trial boot, then commit only after health checks. Never format
state or change identity as an automatic recovery action. Power loss during
download, slot write, metadata commit and first boot must each have a recorded
safe recovery outcome before deployment is qualified.

For comparisons, preserve `config/policy.json` and use the same physical board,
RAM/clock limits, kernel/DTB/toolchain, state allocation, workload/payloads, USB/
Ethernet links, Tor config, cold/warm conditions and sampling. Interleave public
trials; report variability and missing/null values. Buildroot-to-Yocto or
cross-ISA results are descriptive unless equivalent conditions are established;
existing comparison may correctly return NON_COMPARABLE. No new profile
qualifies by meeting only an aspirational size.
