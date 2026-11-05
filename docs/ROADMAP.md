# Engineering roadmap

| Order | Milestone | Exit evidence |
| --- | --- | --- |
| Complete | ARM QEMU qualification | Pinned ARM build, original frozen online suite, and paired 128/256 MiB results. 256 MiB passes the full screen. |
| 1 | DHCOM physical bring-up | Record exact module and carrier, DTB, boot chain and UART log. Boot from removable media, acquire Ethernet DHCP and verify clock, entropy and public Tor bootstrap. |
| 2 | Raw NAND boot and persistent state | Confirm erase geometry, ECC, bad-block reserve and recovery boot. Use read-only SquashFS and a writable UBI-backed state layout. Prove stable identity and headroom through Tor cache refresh and interrupted writes. |
| 3 | USB gadget and Ethernet coexistence | Select the Windows-compatible gadget transport. Test enumeration, reconnect, forwarding and local-only access policy while Ethernet remains operational. |
| 4 | Two physical endpoints | Execute the unchanged nine required checks, including both directions, payload hashes and same-identity reconnection. Verify both Windows hosts reach their local endpoint over USB. |
| 5 | Reliability and endurance | Run repeated cold starts and sustained traffic. Measure thermal, CPU, memory, I/O and state growth. Exercise controlled power cuts and track write amplification. |
| 6 | Minimum-hardware comparison | Repeat on the 128 MiB T113-S3 candidate with equivalent workload and links. Use same-board RAM restriction where useful to isolate RAM from SoC differences. Compare local and public samples separately. |
| 7 | Final target selection | Review performance and RAM gates, availability, flash layout and endurance. Optimize only with full regression evidence and preserve the reference images. |

Keep the raw NAND, boot firmware and update/recovery budget distinct from
the measured QEMU block-image footprint. Preserve the 256 MiB control.
Publish hardware qualification only once the physical evidence exists.
