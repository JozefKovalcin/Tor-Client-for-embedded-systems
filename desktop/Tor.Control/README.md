# Local Tor control driver

BCL-only .NET 11 driver; independent of Avalonia and process startup. It connects
only to explicit IPv4/IPv6 loopback endpoints. Typed commands cover SAFECOOKIE
authentication, bootstrap, ownership, shutdown, a fixed network-enable operation,
online status and NEWNYM. The driver accepts no arbitrary command, shell, or
remote-control input. SAFECOOKIE is the only supported control authentication.

Protocol authority, inspected 2026-11-18.
[Tor commands](https://spec.torproject.org/control-spec/commands.html) and
[Tor replies/events](https://spec.torproject.org/control-spec/replies.html).
SAFECOOKIE uses BCL cryptography with the specification's public domain-separation
strings, random client nonces and constant-time verification of the server proof
before sending a controller proof. Cookie bytes are supplied by the owning host;
the server-advertised cookie pathname is never opened. Temporary byte buffers are
zeroed; raw commands/replies, proofs, cookies and exception messages are not logged.

One receive loop demultiplexes complete asynchronous event frames and serialized
command replies. Parsing supports continuation lines, data blocks and dot escaping;
rejects truncated/malformed/mixed-code frames; and enforces 8 KiB lines, 256 lines
and 1 MiB per reply. These bounds intentionally reject oversized responses.
Unrecognized events/arguments are ignored. Bootstrap exports a bounded percentage
and tag, with a categorical warning where applicable. The Tor warning text,
summary, peer identity and address are discarded.
Events received before successful SAFECOOKIE authentication are ignored.

Commands have a five-second deadline. The connection fails closed if a command
abandons an outstanding reply. NEWNYM drains its bounded reply once admitted,
even if the caller cancels, to preserve authenticated shutdown.
The event queue retains at most 64 records; `LatestBootstrap` gives the latest
authenticated observation if a slow observer misses older notifications.

Online status contains a sanitized version, numeric loopback SOCKS listeners,
and Tor's `status/circuit-established` value when the key is available. This
reports whether Tor can build client circuits. The driver does not export circuit
paths. NEWNYM is limited to one submission per ten seconds on this connection.
The result distinguishes local rate limiting, acknowledgement and receipt of a
`SIGNAL NEWNYM` event. Tor can defer or suppress a request, and even an observed
signal does not guarantee a different IP address or complete identity isolation.

The explicit connectivity probe uses a numeric loopback SOCKS5 connection. The
probe optionally verifies the connected peer via the owner supplied by the host
before sending any SOCKS data. It sends the destination hostname in the SOCKS
request so Tor resolves it. TLS verifies the fixed Tor Project HTTPS destination
inside the tunnel, with system proxies, redirects and certificate network
retrieval disabled. Reachability requires the endpoint's positive `IsTor`
response. Results contain no response body, public IP or credential data.
[Tor SOCKS behavior](https://spec.torproject.org/socks-extensions.html).

Offline harness: `desktop/tests/Tor.Control.Tests`; no NuGet test framework or
Tor process required for the control and SOCKS mock/protocol groups. Native lifecycle smoke
requires explicit `--windows-smoke` arguments and a separately verified existing
runtime. See the Windows adapter handoff. Public Tor connectivity requires the
separately authorized integration workflow.
