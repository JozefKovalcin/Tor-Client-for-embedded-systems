# W5 Part B — native Windows Tor control

Branch: `codex/task04-w5-windows-control`. Parent/core boundary:
`19670692c0b9a29c1e5082f4a442a34fc8237fb5` on
`codex/task04-w5-task-core` (PR #5). Corrected core commits were integrated by
ordinary non-destructive fast-forward merges; no history was rewritten.
This PR must target the core branch until its parent is merged, then retarget
to main and recheck the remaining diff/CI. No automatic PR merge is authorized.

## Implemented boundary

- `desktop/Tor.Control/`: bounded multiline/continuation/data-block/dot framing,
  one reply reader, serialized typed commands, asynchronous bootstrap events,
  SAFECOOKIE server-proof verification before controller authentication,
  no COOKIE/password fallback, loopback-only connections and no arbitrary
  command API. Unauthenticated events never become trusted progress.
- `desktop/Tor.Adapters/Windows/`: existing static-package hash verification
  with read locks; fresh owner-only configuration/state; argument-array startup
  with minimal environment, fixed `DisableNetwork 1` and `SocksPort 0`;
  ephemeral loopback control; controller ownership and Windows kill-on-close
  Job Object; bounded graceful/forced teardown and preserved observed exits.
- Explicit `WindowsTorTaskHandler` connects the managed offline session to the
  typed core. Cancellation awaits process cleanup and preserves raw exit0;
  unexpected exit0 is FAILED through the separate FailureReason axis.
- stdout/stderr are drained through bounded buffers and discarded. Only
  sanitized lifecycle/progress/exit information is exported. Private config,
  cookies, state and raw control messages remain outside Git/evidence/logs.
  There is no state deletion, installation, source build or download behavior.

Runtime provenance: existing `windows-native/evidence/package-static.json`,
Tor0.4.9.14 static package. `tor.exe` 8,642,974 B / SHA-256
`3a65024937320fb53a76615273d9cdf7718d9cf90855081dab4b73ae52d2c7f9`;
`libwinpthread-1.dll` 73,308 B / SHA-256
`94334eab25e1a4043a3efdcf88636e722f387fe62fcc57c2533876a019baa04c`.
These pins match preserved historical metadata. No source/policy/license lock
or upstream patch was changed. No new signature verification is claimed.

Protocol references, inspected 2026-11-17:
[Tor commands](https://spec.torproject.org/control-spec/commands.html),
[replies/events](https://spec.torproject.org/control-spec/replies.html),
[Microsoft job objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

## Executed validation

Windows x64, user-installed .NET SDK11.0.100 / runtime11.0.0, `net11.0`.
BCL-only restore used an empty local feed and disabled NuGet audit; no external
packages or SDK were installed/downloaded by this agent. Build: PASS, zero
warnings/errors. Generated bin/obj/CLI caches are ignored and preserved.

`desktop/tests/Tor.Control.Tests` executed **9 mock/protocol groups PASS**:
bounded framing/dot escaping and malformed/oversized/truncated input rejection;
SAFECOOKIE plus interleaved event/reply/concurrent-command routing;
bad server proof and duplicate challenge rejection without sending a proof;
no insecure authentication fallback; sanitized authentication rejection;
unauthenticated/local-API and nonloopback denial; timeout/cancellation failure
closure; unexpected EOF. Pre-auth fake bootstrap100 never becomes trusted.

Explicit actual-Tor native smoke used the already-present verified package
`C:/Users/jozef/Downloads/Tor Klient/windows-native/dist` with fresh private
state under this worktree's ignored `build/task04-checks/private-native-state`.
No public-network traffic or SOCKS listener was enabled:

- Actual raw unauthenticated GETINFO rejected by Tor with514: PASS.
- SAFECOOKIE start, observed offline bootstrap0, authenticated clean shutdown,
  actual raw exit0 and no surviving Tor PID: PASS.
- Unexpected Tor termination detected, distinct from managed stop: PASS.
- Core command cancellation waits for Tor teardown and retains raw exit0: PASS.
- Killing only the owning test application leaves no Tor orphan: PASS.
- Unverified executable hash rejected before process/state creation: PASS.
- Unlisted root DLL, external manifest and `.local` redirection fixtures are
  excluded from the fresh verified execution directory and loaded modules:
  PASS. Only the two pinned open file streams are copied; staged copies are
  rehashed/read-locked. Source package neighbors remain unchanged.

Commands/results retained under ignored `build/task04-checks/`:

```powershell
dotnet restore desktop/tests/Tor.Control.Tests/Tor.Control.Tests.csproj --source build/task04-checks/empty-feed -p:NuGetAudit=false --disable-parallel
dotnet build desktop/tests/Tor.Control.Tests/Tor.Control.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
dotnet desktop/tests/Tor.Control.Tests/bin/Debug/net11.0/Tor.Control.Tests.dll
dotnet desktop/tests/Tor.Control.Tests/bin/Debug/net11.0/Tor.Control.Tests.dll --windows-smoke <verified-existing-static-package> <private-parent>
python -B scripts/check.py
python -B -m unittest discover -s windows-native/tests -v
git diff --check
```

Final complete portable suite: **119 discovered / 116 PASS / 3 platform
SKIPPED**, exit0; it executes corrected core **15 compiled behavior groups**
and the **9 compiled control groups** on this host. Core branch separately:
**118 discovered / 115 PASS / 3 SKIPPED**, exit0. Existing Windows pipeline:
**6 PASS**. Whitespace/scope/preserved-input review: PASS.

Ordinary repository tests run only synthetic loopback mocks; actual native Tor
requires the explicit smoke arguments and does not run automatically in CI.
SDK-less hosts explicitly skip the compiled gate as BLOCKED, rather than
claiming source/fixture checks proved executable behavior. Initial tool
execution was once blocked by automatic approval review's account usage-limit
service error, not an unsafe-action determination; it was not executed. After
user continuation, normal approval succeeded and final tests above executed.
A nullable test-helper compile error was corrected before final native PASS.

## Changed files and integration

- `desktop/Tor.Control/{.gitignore,Tor.Control.csproj,ControlProtocol.cs,TorControlClient.cs,README.md}`
- `desktop/Tor.Adapters/Windows/{.gitignore,Tor.Adapters.Windows.csproj,PinnedWindowsRuntime.cs,WindowsJob.cs,ManagedTorSession.cs,WindowsTorTaskHandler.cs,README.md}`
- `desktop/tests/Tor.Control.Tests/{.gitignore,Tor.Control.Tests.csproj,Program.cs}`
- `tests/test_tor_control_foundation.py`
- `docs/task04/W5-WINDOWS-HANDOFF.md`

W4 remains simulated and does not yet register this adapter. Next integration
must explicitly register the typed handler, map W1 progress onto the UI feed,
select private runtime retention and supply verified package ownership. W6
remains an independent Linux executor interface; no device/Yocto adapter exists.
W1 schemas, all existing functional tests/thresholds, source locks, Tor patches,
historical reports, baseline and stashes remain unchanged.

Open limits: only the existing reviewed static Windows package is accepted;
other runtime versions need provenance review. Actual smoke is Windows11 x64
offline evidence only, not Windows10/Linux or public Tor qualification. Startup
exits not observed remain null, and cleanup timeout is a real failure. Application
budgets are not OS quotas. The control reader has intentional response limits;
future larger queries require separate review. Raw Tor output is intentionally
unavailable in shareable logs. Private session data requires a future explicit
retention policy; it is never erased automatically here. Networking enablement,
run-only distribution and real GUI wiring remain NOT RUN/unimplemented in this
foundation. No W7–W15, firmware build, public test or hardware action was started.

## Final runtime-integrity review correction

Review confirmed that launching the original verified executable in a package
directory containing unverified neighbors would still expose that directory to
Windows DLL search/redirection. The adapter now stages only the two pinned
runtime file streams into a fresh owner-only execution directory, verifies and
locks those copies, and excludes the original package from executable location,
working directory and PATH. This is a runtime-integrity fix; no core/schema or
source-lock change. A harmless non-PE `version.dll`, external manifest and
`.local` folder fixture are retained in an otherwise valid source package;
actual offline launch verifies that none is copied/searched/loaded and the
source package remains intact. No malicious code is created or executed.
Temporary staging adds approximately8.7MB per retained session; staged binaries
and private state remain ignored and are not automatically deleted. The OS and
current user identity remain trusted. Official search/redirection documentation
is linked in the adapter README.
