# Executive report package

The current Slovak report is [Tor_Executive_Report_SK.pdf](Tor_Executive_Report_SK.pdf),
supplied on 2026-11-09. Its measurement pages match the archived data. Only the
launcher note on page 6 changed to describe Ubuntu discovery and the opt-in
upgrade offer. The [original supplied PDF](source/Tor_Executive_Report_SK_2026-11-06.pdf)
is preserved. See [the synchronization audit](../sync-2026-11-09/README.md) for
checksums, package provenance, validation and current project limits.

The following English HTML/PDF is the earlier 2026-11-05 report package.

Open [index.html](index.html) locally in a browser. The report uses inline
CSS and SVG. No network, CDN or JavaScript is required. Manufacturer links
are optional references. [Executive_Report.pdf](Executive_Report.pdf) is
the six-page, 16:9 print export.

[data.json](data.json) contains archived boot measurements and test
outcomes, the paired native cold boots, per-group gates, workload context,
source paths and SHA-256 values. Null or absent data means N/A. Shared
writable ext4 has no separately measured state partition or non-state
footprint. Byte counts use logical image size. Physical flash requires
board-specific allowances.

The early measurements use the original two-Tor workload with the harness
either embedded or on an external disk. The paired performance pages use
the one-endpoint observer with a host peer, QEMU 10.2.1 TCG, one vCPU, NAT
and three cold boots per RAM/ISA group. Performance gates use the 256 MiB
group of the same architecture. Cross-ISA observations are descriptive.

## Regenerate and verify

From the repository root, run `python3 reports/executive/generate.py`.
This reads the existing archives and generates JSON plus offline HTML.
Export the HTML from Chromium with background graphics enabled, CSS
page size, and browser headers and footers disabled. Render all six
PDF pages with Poppler to check chart and table layout. The evidence date
and values remain independent of the export timestamp.

See [technical handover](../../docs/HANDOFF.md) and
[roadmap](../../docs/ROADMAP.md) for build commands and physical gates.

## Two-PC launcher and Slovak PDF

The launcher sources are in `scripts/two_pc_demo/`. To rebuild the ZIP using
the original local package as the source of verified firmware (output must not
already exist):

```sh
python3 scripts/build_two_pc_demo.py --base-zip /path/to/Tor_Capacity32_Test_Dva_PC.zip \
  --output build/packages/Tor_Capacity32_Test_Dva_PC.zip
```

The packager verifies both image hashes against the archived ARM manifest and
includes only the versioned launchers plus those images. It excludes runtime
state. `bash run-wsl.sh --check` checks dependencies without booting QEMU.
The demo's 59-byte echo is separate from the frozen nine-test qualification.

To reproduce the small Slovak PDF edit with authoring-only dependency PyMuPDF,
run `update_sk_report.py --input source/Tor_Executive_Report_SK_2026-11-06.pdf
--output /fresh/path/Tor_Executive_Report_SK.pdf --font /path/to/segoeui.ttf`
from this directory. The earlier HTML generator does not regenerate this PDF.
