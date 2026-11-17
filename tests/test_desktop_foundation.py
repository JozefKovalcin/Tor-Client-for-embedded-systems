"""Offline desktop fixture/project boundary checks, not GUI compile/render proof."""
import base64
import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

from scripts.contracts import validate_contract


ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "desktop/Tor.Desktop"


class DesktopFoundationTests(unittest.TestCase):
    def test_all_eight_embedded_contract_examples_remain_valid(self):
        names = {path.stem for path in (ROOT / "config/contracts/v1").glob("*.schema.json")}
        names = {name.removesuffix(".schema") for name in names}
        self.assertEqual(len(names), 8)
        for name in names:
            with self.subTest(contract=name):
                value = json.loads((DESKTOP / "SampleData" / (name + ".json")).read_text())
                self.assertEqual(validate_contract(name, value), [])

    def test_simulated_task_and_dependency_coverage_and_validity(self):
        data = json.loads((DESKTOP / "SampleData/Simulation.json").read_text())
        self.assertEqual(data["kind"], "simulation-only")
        self.assertEqual({row["state"] for row in data["tasks"]},
                         {"queued", "running", "cancelling", "cancelled", "succeeded", "failed", "blocked", "interrupted"})
        self.assertEqual({row["status"] for row in data["dependencies"]},
                         {"AVAILABLE", "MISSING", "INCOMPATIBLE", "UNKNOWN", "BLOCKED"})
        for name, rows in (("TaskProgress", data["tasks"]), ("DependencyCheck", data["dependencies"])):
            for row in rows:
                with self.subTest(contract=name, row=row.get("taskId", row.get("id"))):
                    self.assertEqual(validate_contract(name, row), [])
        failed = next(row for row in data["tasks"] if row["state"] == "failed")
        self.assertEqual(failed["rawExitCode"], 17)

    def test_sdk_and_direct_package_versions_are_exact_and_unambiguous(self):
        pin = json.loads((ROOT / "desktop/global.json").read_text())["sdk"]
        self.assertEqual(pin, {"version": "11.0.100", "rollForward": "disable", "allowPrerelease": False})
        project = ET.parse(DESKTOP / "Tor.Desktop.csproj").getroot()
        self.assertEqual(project.findtext("PropertyGroup/TargetFramework"), "net11.0")
        self.assertEqual(project.findtext("PropertyGroup/RestoreLockedMode"), "true")
        packages = {node.attrib["Include"]: node.attrib["Version"] for node in project.findall("ItemGroup/PackageReference")}
        self.assertEqual(packages, {name: "[12.1.6]" for name in ("Avalonia", "Avalonia.Desktop", "Avalonia.Themes.Fluent")})
        sources = ET.parse(ROOT / "desktop/NuGet.Config").getroot().find("packageSources")
        self.assertEqual([node.attrib["value"] for node in sources.findall("add")], ["https://api.nuget.org/v3/index.json"])

    def test_xaml_and_projects_parse_and_ui_has_no_process_execution(self):
        for path in [*DESKTOP.glob("*.axaml"), *DESKTOP.glob("*.csproj"),
                     *DESKTOP.glob("*.props"), *ROOT.glob("desktop/tests/Tor.Desktop.Tests/*.csproj")]:
            ET.parse(path)
        sources = "\n".join(path.read_text() for path in DESKTOP.rglob("*.cs"))
        for prohibited in ("Process.Start", "ProcessStartInfo", "System.Diagnostics.Process", "COMPILE.cmd", "cmd.exe", "powershell.exe", "HttpClient", "TcpClient"):
            self.assertNotIn(prohibited, sources)

    def test_genuine_lock_has_exact_direct_pins_and_resolved_hashes(self):
        lock = json.loads((DESKTOP / "packages.lock.json").read_text())
        self.assertEqual(lock["version"], 1)
        self.assertEqual(set(lock["dependencies"]), {"net11.0"})
        graph = lock["dependencies"]["net11.0"]
        direct = {name: node["resolved"] for name, node in graph.items() if node["type"] == "Direct"}
        self.assertEqual(direct, {name: "12.1.6" for name in ("Avalonia", "Avalonia.Desktop", "Avalonia.Themes.Fluent")})
        packages = {name: node for name, node in graph.items() if node["type"] != "Project"}
        self.assertEqual(len(packages), 25)
        for name, node in packages.items():
            with self.subTest(package=name):
                self.assertEqual(len(base64.b64decode(node["contentHash"], validate=True)), 64)
                self.assertTrue(node["resolved"])
                self.assertNotIn("*", node["resolved"])
                self.assertTrue(set(node.get("dependencies", {})) <= set(packages))
        projects = {name: node for name, node in graph.items() if node["type"] == "Project"}
        expected_projects = {
            "tor.application": {"Tor.Adapters.Windows", "Tor.Core"},
            "tor.adapters.windows": {"Tor.Control", "Tor.Core"},
            "tor.control": set(),
            "tor.core": {"Tor.Contracts"},
            "tor.contracts": set(),
        }
        self.assertEqual({name: set(node.get("dependencies", {})) for name, node in projects.items()},
                         expected_projects)


if __name__ == "__main__":
    unittest.main()
