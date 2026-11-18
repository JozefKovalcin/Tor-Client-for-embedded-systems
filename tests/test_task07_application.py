"""Compile and run the package-free application regressions with the pinned SDK."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest


ROOT = Path(__file__).resolve().parents[1]


class WindowsManagerApplicationTests(unittest.TestCase):
    def test_compiled_application_and_presentation_without_a_tor_process(self):
        dotnet = shutil.which("dotnet")
        if dotnet is None and os.name == "nt":
            candidate = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "dotnet/dotnet.exe"
            dotnet = str(candidate) if candidate.is_file() else None
        if dotnet is None:
            self.skipTest("Pinned .NET SDK unavailable; compiled application and presentation checks BLOCKED")
        workspace = ROOT / "build/task07-application-validation"
        feed = workspace / "empty-feed"
        feed.mkdir(parents=True, exist_ok=True)
        env = dict(os.environ, DOTNET_CLI_HOME=str(workspace / "cli-home"),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                   DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="1", DOTNET_GENERATE_ASPNET_CERTIFICATE="false",
                   DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false", DOTNET_CLI_USE_MSBUILD_SERVER="0",
                   NUGET_PACKAGES=str(workspace / "packages"))
        sdk = subprocess.run([dotnet, "--list-sdks"], env=env, text=True, capture_output=True, timeout=15)
        required = json.loads((ROOT / "desktop/global.json").read_text())["sdk"]["version"]
        installed = {row.split()[0] for row in sdk.stdout.splitlines() if row.strip()}
        if sdk.returncode or required not in installed:
            self.skipTest(f"Pinned .NET SDK {required} unavailable; compiled checks BLOCKED")

        desktop = ROOT / "desktop"
        for project, assembly, arguments, exit_code, result_marker in (
            ("Tor.Application.Tests", desktop / "tests/Tor.Application.Tests/bin/Debug/net11.0/Tor.Application.Tests.dll",
             ["--workspace", str(workspace / "cases")], 0, "11 offline application behavior groups PASS"),
            ("Tor.Desktop.Tests", ROOT / "build/desktop/w4-tests/bin/Debug/net11.0/Tor.Desktop.Tests.dll",
             [], 0, "offline desktop integration assertions PASS"),
            ("Tor.Online.Tests", desktop / "tests/Tor.Online.Tests/bin/Debug/net11.0/Tor.Online.Tests.dll",
             ["--public-tor", str(workspace / "unused-runtime"), str(workspace / "unused-state")],
             2, "NOT RUN: public Tor qualification requires"),
            ("Tor.Peer.Tests", desktop / "tests/Tor.Peer.Tests/bin/Debug/net11.0/Tor.Peer.Tests.dll",
             ["--offline"], 0, "7 offline peer groups PASS"),
        ):
            with self.subTest(project=project):
                path = desktop / "tests" / project / (project + ".csproj")
                commands = [
                    [dotnet, "restore", str(path), "--source", str(feed), "-p:NuGetAudit=false", "--disable-parallel"],
                    [dotnet, "build", str(path), "--no-restore", "--disable-build-servers", "-p:UseSharedCompilation=false"],
                ]
                for command in commands:
                    result = subprocess.run(command, cwd=desktop, env=env, text=True, capture_output=True, timeout=120)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                result = subprocess.run([dotnet, str(assembly), *arguments], cwd=desktop, env=env,
                                        text=True, capture_output=True, timeout=120)
                self.assertEqual(result.returncode, exit_code, result.stdout + result.stderr)
                self.assertIn(result_marker, result.stdout + result.stderr)
                if exit_code:
                    self.assertFalse((workspace / "unused-state").exists())
                    policy = subprocess.run([dotnet, str(assembly), "--offline-policy"], cwd=desktop, env=env,
                                            text=True, capture_output=True, timeout=120)
                    self.assertEqual(policy.returncode, 0, policy.stdout + policy.stderr)
                    self.assertIn("PASS: online configuration confines SOCKS to IPv4 loopback", policy.stdout)
                    if os.name == "nt":
                        self.assertIn("PASS: real Windows TCP listener and peer ownership", policy.stdout)


if __name__ == "__main__":
    unittest.main()
