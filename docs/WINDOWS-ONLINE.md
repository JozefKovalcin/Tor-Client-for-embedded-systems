# Windows online Tor Manager

## Running a session

1. Start the manager with the reviewed Windows runtime directory using
   `Tor.Desktop.exe --tor-package "C:\path\to\verified-tor-runtime"`.
2. Select **Real Online** explicitly. Choose a local SOCKS5 port, then select
   **Start Tor**. The default port is `9150`. If occupied, select another port
   and retry after the failed task completes.
3. Use `127.0.0.1:<selected-port>` as the SOCKS5 proxy in each client that
   should use Tor. Enable proxy-side name resolution in those clients.
   Tools that accept a proxy URL may call this `socks5h://`. Check that your
   client sends hostnames to the proxy and has direct fallback disabled.
4. Use **Test through Tor** to make a separate proxy-only destination check.
   The dashboard distinguishes the owned local listener, Tor bootstrap and
   destination reachability. Stop closes the owned process and its listeners.

The managed ControlPort is restricted to IPv4 loopback and authenticated with
SAFECOOKIE. The configured SOCKS5 endpoint also binds only to IPv4 loopback.
SOCKS access is for local desktop clients. Other processes on the same Windows
host can access a localhost SOCKS port. SOCKS usernames and passwords, when
supported by the client, provide Tor stream isolation and do not authenticate
local Windows users. Use a dedicated host or OS isolation when other local
accounts or software are untrusted. The manager makes no system proxy change.

Online uses a Tor client configuration with `SafeSocks` enabled. The manager
keeps private state under the user-owned scope used by the offline build and
does not export identities, cookies or raw authentication and circuit frames.
New Identity requests fresh circuits for new streams, subject to Tor rate limits.
Existing streams can stay open. A new exit address or complete identity
isolation cannot be inferred from a successful request.
[Tor Control specification](https://spec.torproject.org/control-spec/commands.html) ·
[Tor SOCKS extensions](https://spec.torproject.org/socks-extensions.html).

## Explicit public-network qualification

The ordinary suite and GitHub CI do not execute public Tor tests. The native
port-conflict check can run with a local occupied socket and the reviewed
package. The public workflow must have separate approval for real bootstrap and
the proxy-only destination probe. Run it on the supported Windows x64 host
using an existing verified runtime and a new private path with a safe parent.

```powershell
pwsh -NoProfile -File scripts/test_windows_online.ps1 -ApprovePublicTor `
  -TorPackage 'C:\path\to\verified-tor-runtime' `
  -NewStatePath 'C:\path\to\test-workspace\new-private-state'
```

The qualification console verifies collision refusal, SAFECOOKIE, observed
bootstrap at 100%, the owned IPv4 loopback SOCKS endpoint, a proxy-only HTTPS
check, graceful Stop and owned-process cleanup when the application service
closes. The focused Tor.Control loopback tests verify domain-form SOCKS requests
and refusal of direct fallback. For release acceptance, manually repeat Start,
bootstrap, the connectivity check and close in the real GUI, then separately
repeat the existing native offline tests. Preserve only sanitized observations.
