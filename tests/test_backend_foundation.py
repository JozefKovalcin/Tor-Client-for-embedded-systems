"""W1 compatibility and actual BCL-only backend behavior; no network/package feed."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest

from scripts.contracts import validate_contract

ROOT = Path(__file__).resolve().parents[1]


class BackendFoundationTests(unittest.TestCase):
    def test_emitted_synthetic_snapshots_preserve_w1_states_and_bounds(self):
        data = json.loads((ROOT / "tests/fixtures/backend/task-progress.json").read_text(encoding="utf-8"))
        self.assertEqual(data["kind"], "synthetic-core-harness-snapshots")
        states = set()
        for row in data["snapshots"]:
            with self.subTest(task=row["taskId"], sequence=row["sequence"]):
                self.assertEqual(validate_contract("TaskProgress", row), [])
                states.add(row["state"])
        self.assertEqual(states, {"queued", "running", "cancelling", "cancelled", "succeeded", "failed", "blocked", "interrupted"})
        self.assertTrue(any(row["completed"] is not None for row in data["snapshots"]))

    def test_compiled_backend_behavior_when_net11_sdk_is_available(self):
        executable = shutil.which("dotnet")
        if not executable and os.name == "nt":
            candidate = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "dotnet/dotnet.exe"
            executable = str(candidate) if candidate.is_file() else None
        if executable is None:
            self.skipTest(".NET 11 SDK unavailable; compiled backend gate BLOCKED")
        workspace = ROOT / "build/task04-backend-validation"
        workspace.mkdir(parents=True, exist_ok=True)
        feed = workspace / "empty-feed"
        feed.mkdir(exist_ok=True)
        env = dict(os.environ, DOTNET_CLI_HOME=str(workspace / "cli-home"),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                   DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="1", DOTNET_GENERATE_ASPNET_CERTIFICATE="false",
                   DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false", DOTNET_CLI_USE_MSBUILD_SERVER="0",
                   NUGET_PACKAGES=str(workspace / "packages"))
        sdk = subprocess.run([executable, "--list-sdks"], text=True, capture_output=True, timeout=15, env=env)
        if sdk.returncode or not any(line.startswith("11.") for line in sdk.stdout.splitlines()):
            self.skipTest(".NET 11 SDK unavailable; compiled backend gate BLOCKED")
        project = ROOT / "desktop/tests/Tor.Core.Tests/Tor.Core.Tests.csproj"
        for command in ([executable, "restore", str(project), "--source", str(feed), "-p:NuGetAudit=false", "--disable-parallel"],
                        [executable, "build", str(project), "--no-restore", "--disable-build-servers", "-p:UseSharedCompilation=false", "--nologo"],
                        [executable, str(project.parent / "bin/Debug/net11.0/Tor.Core.Tests.dll")]):
            process = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=90)
            self.assertEqual(process.returncode, 0, process.stdout + process.stderr)


if __name__ == "__main__":
    unittest.main()
