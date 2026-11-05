# ARMv7 capacity32 platform selection

The ARM port targets little-endian ARMv7-A with a hard-float musl ABI and a
Cortex-A7 guest. It reuses the capacity32 Tor configuration, read-only XZ
SquashFS and separate 32 MiB persistent state. The QEMU boot set consists of
`zImage` and a block-device `disk.img`. The matching ARM Python test disk is
built separately. The existing nine assertions and the production-daemon
and ELF checks are used without changing the test workload or policy.

## Shortlist

| SoC and orderable platform | CPU | RAM and flash | Ethernet and USB device | Porting implication |
| --- | --- | --- | --- | --- |
| NXP i.MX 6ULL, DH electronics DHCOM HI00096 with picoITX carrier | 1× Cortex-A7, 528 MHz | 256 MiB DDR, 128 MiB NAND plus 2 MiB boot SPI | 100 Mb/s Ethernet and high-speed USB OTG on the carrier | Linux 6.18.7 contains the DHCOM/picoITX device tree with SD-card routing. The NAND boot and volume layout need board-specific integration. [Module and SKU](https://www.dh-electronics.com/en/embedded-products/dhsom/detail/dhcom-imx6ull), [carrier](https://www.dh-electronics.com/en/embedded-products/development-carrier-boards/detail/picoitx-carrier-board) |
| Allwinner T113-S3, 100ASK T113s3-PRO V1.3 LTS SPI NAND | 2× Cortex-A7, up to 1.2 GHz | 128 MiB integrated DDR3, 128 MiB SPI NAND in the documented NAND configuration | 100 Mb/s RJ45 on the carrier and USB-C OTG with documented device mode | Lowest RAM in the shortlist. The exact carrier needs a board port and the SPI NAND SKU should be confirmed by revision. Ethernet shares some pins with optional peripherals. [Board and OTG](https://dshanpi.100ask.net/docs/T113s3-Pro/BoardIntroduction/), [NAND capacity](https://allwinner-docs.100ask.org/Special/Buildroot_LTS-T113/01-BoardIntroduction.html), [V1.3 LTS](https://docs.100ask.net/T113s3-Pro/) |
| ST STM32MP135D, MYIR MYD-YF135-256N256D-100-I | 1× Cortex-A7, up to 1 GHz | 256 MiB DDR3L, 256 MiB NAND | 2× Gigabit Ethernet and high-speed USB OTG on the carrier | Higher flash capacity than the preferred budget. A useful commercial alternative where the carrier's interfaces matter. [Exact MYIR SKU and interfaces](https://en.myir.cn/STM32MP135/65.html), [ST SoC documentation](https://www.st.com/resource/en/datasheet/stm32mp135d.pdf) |

These are manufacturer-listed configurations. Stock and lead times vary.
Physical qualification must cover bootloader, DTB, Ethernet PHY, correct USB
device role and enumeration on a PC while Ethernet stays up, cold boot
entropy and wall-clock initialization, plus the same Tor tests and RAM
reservation. The generic ARM VM uses PL011, PL031 and VirtIO. [QEMU virt
documentation](https://www.qemu.org/docs/master/system/arm/virt.html)

## Flash layout

For raw NAND, account for the bootloader, firmware and DTB, page and
eraseblock alignment, ECC and bad-block reserve, UBI overhead and update
slots. Keep the SquashFS root read-only and provision the writable state
with UBI/UBIFS or a managed block device. Size it from measured usable
space and transient Tor cache writes. The 32 MiB ext4 allocation is a
measured block-device budget. Use the NAND geometry and a power-loss test
to qualify the board's state volume. [Linux UBI/UBIFS
documentation](https://www.kernel.org/doc/html/v6.15/filesystems/ubifs.html)

## Build and verify

Run from the native WSL2 checkout with a Linux-only `PATH` and Ubuntu's
`qemu-system-arm` installed.

```sh
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
python3 scripts/check.py
python3 scripts/check_buildroot.py
./scripts/validate.sh --profile qemu-arm-capacity32 --mode online
```

The validator writes the full results, compiler flags, resolved configs,
artifact hashes and serial log under `reports/benchmarks/<run-id>/`. The
Python fixture's kernel ABI, compiler and hardening are independent of the
original x86 fixture. It is mounted read-only and kept outside the target
root. The historical x86 baseline comparison is expected to return
`NON_COMPARABLE` even when all ARM assertions pass. A separate `--mode
offline` run exercises the offline tests with QEMU networking restricted.

The one-endpoint measurement uses the existing native observer. It holds a
32 MiB tmpfs reservation in addition to the actual workload and verifies
the 32 MiB Tor queue configuration via authenticated control. Build a
separate observer disk for each ABI with
`scripts/build_capacity_fixture.py --compiler <target-gcc> --output
<new-path>`. Start the pinned host peer with `scripts/capacity_peer.py` in
a fresh runtime directory. Use
`reports/arm-2026-11-05/reproduce.py --help` for the paired series and
`scripts/analyze_capacity.py --native <series> --output <new-directory>`
for the existing within-image 128/256 MiB screen. Compare ARM and x86
numbers descriptively because their QEMU machines and instruction
translation differ.
