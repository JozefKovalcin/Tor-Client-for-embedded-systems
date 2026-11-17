"""Execute bounded SAFECOOKIE mock behavior when the installed .NET SDK permits it."""
import os
from pathlib import Path
import shutil
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[1]


class TorControlFoundationTests(unittest.TestCase):
    def test_compiled_loopback_control_behavior_without_tor_or_downloads(self):
        dotnet = shutil.which("dotnet")
        if dotnet is None and os.name == "nt":
            candidate = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "dotnet/dotnet.exe"
            dotnet = str(candidate) if candidate.is_file() else None
        if dotnet is None:
            self.skipTest(".NET 11 SDK absent; compiled Tor-control gate BLOCKED")
        workspace = ROOT / "build/task04-control-validation"
        workspace.mkdir(parents=True, exist_ok=True)
        empty = workspace / "empty-feed"
        empty.mkdir(exist_ok=True)
        env = dict(os.environ, DOTNET_CLI_HOME=str(workspace / "cli-home"),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                   DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="1", DOTNET_GENERATE_ASPNET_CERTIFICATE="false",
                   DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false", DOTNET_CLI_USE_MSBUILD_SERVER="0",
                   NUGET_PACKAGES=str(workspace / "packages"))
        sdk = subprocess.run([dotnet, "--list-sdks"], env=env, capture_output=True, text=True, timeout=15)
        if sdk.returncode or not any(row.startswith("11.") for row in sdk.stdout.splitlines()):
            self.skipTest(".NET 11 SDK absent; compiled Tor-control gate BLOCKED")
        project = ROOT / "desktop/tests/Tor.Control.Tests/Tor.Control.Tests.csproj"
        commands = [
            [dotnet, "restore", str(project), "--source", str(empty), "-p:NuGetAudit=false", "--disable-parallel"],
            [dotnet, "build", str(project), "--no-restore", "--disable-build-servers", "-p:UseSharedCompilation=false", "--nologo"],
            [dotnet, str(project.parent / "bin/Debug/net11.0/Tor.Control.Tests.dll")],
        ]
        for command in commands:
            result = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=90)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("9 control behavior groups PASS", result.stdout)
        self.assertIn("NOT RUN native Windows Tor lifecycle smoke", result.stdout)


if __name__ == "__main__":
    unittest.main()
