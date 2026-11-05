"""Generate the offline executive report from archived measurements."""
from hashlib import sha256
from html import escape
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
MIB = 2**20
ARM = "reports/arm-2026-11-05"
CAPACITY = "reports/capacity-2026-11-05"
HISTORY = "reports/performance-2026-11-05"
EARLY = "reports/measured/2026-11-04"
sources = {}


def read(path):
    content = (ROOT / path).read_bytes()
    sources[path] = sha256(content).hexdigest()
    return json.loads(content)


def build(path, layout=None):
    document = read(path)
    result = document.get("result", document)
    metrics = result["metrics"]
    disk = metrics["rootfs_bytes"]
    if layout:
        assert layout["disk"] == disk
        assert sum(layout[k] for k in ("squashfs_root", "mbr_and_alignment", "state")) == disk
    assert metrics["kernel_bytes"] + disk == metrics["complete_image_bytes"]
    return {
        "source": path, "run": result["run_id"],
        "profile": result["metadata"]["configuration_id"],
        "environment": result["environment"], "metadata": result["metadata"],
        "build": result["build"], "tests": result["tests"], "metrics": metrics,
        "comparison": document.get("comparison"),
        "storage_bytes": {
            "kernel": metrics["kernel_bytes"], "disk": disk,
            "root": layout["squashfs_root"] if layout else disk,
            "partition_overhead": layout["mbr_and_alignment"] if layout else None,
            "dedicated_state": layout["state"] if layout else None,
            "non_state": layout["non_state"] if layout else None,
            "complete": metrics["complete_image_bytes"],
            "tor": metrics["tor_executable_bytes"], "libraries": metrics["library_bytes"],
        },
        "layout": "dedicated ext4 state and SquashFS root" if layout else "shared writable ext4",
    }


def dataset():
    storage = read(f"{ARM}/storage.json")["images"]
    inventory = read(f"{CAPACITY}/inputs/inventory.json")
    builds = {}
    for file in sorted((ROOT / EARLY).glob("*.json")):
        name = file.relative_to(ROOT).as_posix()
        layout = None
        if "173443" in file.name:
            # The archived compressed disk follows genimage's 1 MiB prefix and 16 MiB state.
            result = read(name)["result"]["metrics"]
            disk = result["rootfs_bytes"]
            layout = {"disk": disk, "squashfs_root": disk - 17*MIB, "state": 16*MIB,
                      "mbr_and_alignment": MIB, "non_state": result["complete_image_bytes"] - 16*MIB}
        row = build(name, layout)
        builds[row["run"]] = row
    for name, key in (("ram", "ram"), ("fulltor-slim", "slim")):
        item = inventory[key]
        root, state = item["partitions"]
        metrics = item["metrics"]
        builds[name] = build(f"{HISTORY}/{name}-build.json", {
            "disk": metrics["rootfs_bytes"], "squashfs_root": root["bytes"],
            "state": state["bytes"], "mbr_and_alignment": root["offset_bytes"],
            "non_state": metrics["complete_image_bytes"] - state["bytes"],
        })
    builds["x86-capacity32"] = build(f"{CAPACITY}/capacity32-build/results.json", storage["x86"]["storage_bytes"])
    builds["arm-capacity32"] = build(f"{ARM}/build-results.json", storage["arm"]["storage_bytes"])
    builds["x86-capacity32"]["comparison"] = read(f"{CAPACITY}/capacity32-build/comparison.json")
    builds["arm-capacity32"]["comparison"] = read(f"{ARM}/build-comparison.json")
    item = inventory["selective"]
    root, state = item["partitions"]
    builds["selective"] = {
        "source": f"{CAPACITY}/inputs/inventory.json", "run": item["source_build"],
        "profile": item["profile"], "metadata": item["metadata"], "build": item["build"],
        "storage_bytes": {"kernel": item["metrics"]["kernel_bytes"], "root": root["bytes"],
                          "partition_overhead": root["offset_bytes"], "dedicated_state": state["bytes"],
                          "non_state": item["metrics"]["complete_image_bytes"] - state["bytes"],
                          "complete": item["metrics"]["complete_image_bytes"],
                          "tor": item["metrics"]["tor_executable_bytes"], "libraries": item["metrics"]["library_bytes"]},
        "functional_measurements_source": f"{CAPACITY}/public-nine/summary.json",
        "layout": "dedicated ext4 state and SquashFS root",
    }
    native = read(f"{ARM}/native-analysis.json")
    summary = read(f"{ARM}/native-summary.json")
    assert native["completed"] == native["planned"] == summary["completed"] == 12
    for group in summary["groups"]:
        key = f"{group['architecture']}-{group['ram_mib']}"
        group["gates"] = native["groups"][key]
        assert group["capacity_screen"] == group["gates"]["capacity_screen"]
    return {
        "schema_version": 1, "evidence_date": "2026-11-05",
        "units": "Bytes, seconds, percent, and bytes/second unless explicitly labeled.",
        "missing_values": "JSON null or absent fields denote N/A. Shared ext4 has no measured non-state boot set.",
        "physical_qualification": "Pending. All Tor measurements are from Ubuntu WSL2/QEMU.",
        "builds": builds, "native_summary": summary, "native_plan": native["plan"],
        "native_environments": {
            "arm": {**read("config/profiles/qemu-arm-capacity32.json")["environment"],
                    "qemu_version": builds["arm-capacity32"]["environment"]["qemu_version"]},
            "x86": {**read("config/profiles/qemu-x86_64-capacity32.json")["environment"],
                    "qemu_version": builds["arm-capacity32"]["environment"]["qemu_version"]},
        },
        "native_protocol": "One target Tor with a shared host peer, three cold boots/group, one vCPU, TCG, NAT, 32 MiB resident reservation.",
        "native_cases": [{k: case[k] for k in ("image", "memory_mib", "run", "metrics", "tests", "qualification", "summary", "local")}
                         for case in native["cases"]],
        "arm_frozen_128_online": read(f"{ARM}/online-128.json"),
        "arm_frozen_128_offline": read(f"{ARM}/offline-128.json"),
        "historical_selection": read(f"{HISTORY}/final-selection.json"),
        "historical_public_repeats": read(f"{CAPACITY}/public-nine/summary.json"),
        "capacity_sources": {"layout": f"{ARM}/storage.json",
                             "compressed_layout": "br2-external/board/qemu-compressed/genimage.cfg",
                             "state_exhaustion": f"{CAPACITY}/state-exhaustion.json"},
        "source_sha256": sources,
    }


def bars(rows, maximum, unit):
    """Zero-based horizontal vector bars; rows contain labeled color segments."""
    out = [f'<svg class="chart" viewBox="0 0 1020 {len(rows)*40+12}" role="img" aria-label="Measured values in {unit}">']
    for i, (name, segments) in enumerate(rows):
        y = i*40+3
        out.append(f'<text x="0" y="{y+19}" class="label">{escape(name)}</text>')
        x = 204
        out.append(f'<rect x="{x}" y="{y}" width="695" height="26" rx="4" fill="#edf3f6"/>')
        for value, color in segments:
            width = value/maximum*695
            out.append(f'<rect x="{x:.2f}" y="{y}" width="{width:.2f}" height="26" fill="{color}"/>')
            x += width
        out.append(f'<text x="1018" y="{y+19}" text-anchor="end" class="value">{sum(v for v, _ in segments):.2f} {unit}</text>')
    return "".join(out) + "</svg>"


def table(headers, rows):
    head = "".join(f"<th>{escape(label)}</th>" for label in headers)
    body = "".join("<tr>" + "".join(f"<td>{escape(str(item))}</td>" for item in row) + "</tr>" for row in rows)
    return f"<table><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table>"


def render(data):
    b = data["builds"]
    baseline = b["20261104T162447Z-cd6efe9f"]["storage_bytes"]
    arm = b["arm-capacity32"]["storage_bytes"]
    x86 = b["x86-capacity32"]["storage_bytes"]
    groups = data["native_summary"]["groups"]
    labels = [f"{g['architecture'].upper()} / {g['ram_mib']} MiB" for g in groups]
    blue, teal, gold, slate = "#2671d4", "#078478", "#e5ae42", "#8297ad"
    journey = [("Original x86", baseline),
               ("x86 data-only", b["20261104T163515Z-f2a9da00"]["storage_bytes"]),
               ("x86 tiny", b["20261104T165728Z-643dbbe1"]["storage_bytes"]),
               ("x86 compressed", b["20261104T173443Z-2cd3ee20"]["storage_bytes"]),
               ("x86 fulltor-slim", b["fulltor-slim"]["storage_bytes"]),
               ("x86 capacity32", x86), ("ARM capacity32", arm)]
    journey_bars = bars([(name, [(s["non_state"]/MIB, blue), (s["dedicated_state"]/MIB, gold)]
                                if s["dedicated_state"] else [(s["complete"]/MIB, slate)])
                         for name, s in journey], baseline["complete"]/MIB, "MiB")
    storage_rows = [(name, *(f"{s[k]/MIB:.2f}" for k in
                    ("kernel", "root", "partition_overhead", "dedicated_state", "complete")))
                    for name, s in (("x86 capacity32", x86), ("ARM capacity32", arm))]
    memory_bars = bars([(label, [(g["ranges"]["minimum_available_bytes"]["min"]/MIB, teal)])
                        for label, g in zip(labels, groups)], 200, "MiB")
    ram_rows = [(label, *(f"{g['medians'][k]/MIB:.2f}" for k in
                 ("idle_pss_bytes", "peak_tor_rss_bytes", "peak_system_used_bytes")),
                 f"{g['medians']['tor_cpu_seconds']:.2f}") for label, g in zip(labels, groups)]
    perf = []
    for label, g in zip(labels, groups):
        m = g["medians"]
        perf.append((label, f"{m['bootstrap_seconds']:.2f}", f"{m['onion_connection_seconds']:.2f}",
                     f"{m['throughput_bytes_per_second']/1024:.2f}", f"{m['local_tcp_bytes_per_second']/MIB:.2f}",
                     f"{g['functions_pass']}/{g['planned']}", "PASS" if g["capacity_screen"] == "PASS" else "REJECTED"))
    perf_bars = []
    for label, key, divisor, unit in (("Local TCP", "local_tcp_bytes_per_second", MIB, "MiB/s"),
                                     ("Public Tor payload", "throughput_bytes_per_second", 1024, "KiB/s"),
                                     ("Onion connection", "onion_connection_seconds", 1, "s"),
                                     ("Public bootstrap", "bootstrap_seconds", 1, "s")):
        rows = [(name, [(g["medians"][key]/divisor, teal if g["architecture"] == "arm" else blue)])
                for name, g in zip(labels, groups)]
        perf_bars.append(f'<div><h3>{label}</h3>{bars(rows, max(v for _, [(v, _)] in rows)*1.02, unit)}</div>')
    return {
        "TOTAL": f"{arm['complete']/MIB:.2f}", "NONSTATE": f"{arm['non_state']/MIB:.2f}",
        "BASELINE_DROP": f"{100*(1-arm['complete']/baseline['complete']):.1f}%",
        "JOURNEY": journey_bars,
        "STORAGE": table(("Boot set (MiB)", "Kernel", "Read-only root", "MBR + alignment", "State", "Total"), storage_rows),
        "MEMORY": memory_bars, "RAM": table(("Guest", "Idle PSS MiB", "Tor peak RSS MiB", "System peak MiB", "Tor CPU s"), ram_rows),
        "PERF": table(("Guest", "Bootstrap s", "Onion s", "Public KiB/s", "Local MiB/s", "Functional runs", "Screen"), perf),
        "PERF_BARS": "".join(perf_bars),
    }


def main():
    data = dataset()
    html = (HERE / "report.html.in").read_text(encoding="utf-8")
    for key, value in render(data).items():
        assert html.count(f"@@{key}@@") == 1, key
        html = html.replace(f"@@{key}@@", value)
    assert "@@" not in html
    (HERE / "data.json").write_text(json.dumps(data, indent=2, sort_keys=True, allow_nan=False)+"\n", encoding="utf-8", newline="\n")
    (HERE / "index.html").write_text(html, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
