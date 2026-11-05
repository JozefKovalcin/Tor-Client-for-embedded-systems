# Executive report package

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
