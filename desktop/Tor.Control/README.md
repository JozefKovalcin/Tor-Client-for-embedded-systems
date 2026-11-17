# Local Tor control driver

BCL-only .NET 11 driver; independent of Avalonia and process startup. It connects
only to explicit IPv4/IPv6 loopback endpoints. Public commands are typed: SAFECOOKIE
authentication, bootstrap subscription/query, ownership and shutdown. There is no
arbitrary command, shell or remote-control API and no COOKIE/password fallback.

Protocol authority, inspected 2026-11-17:
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
Unrecognized events/arguments are ignored. Bootstrap exports only a bounded
percentage/tag record, excluding raw summaries and sensitive identifiers.
Events received before successful SAFECOOKIE authentication are ignored.

Commands have a five-second deadline. Cancellation/timeout fails the connection
closed so an abandoned response cannot become the next command's result.
The event queue retains at most 64 records; `LatestBootstrap` gives the latest
authenticated observation if a slow observer misses older notifications.

Offline harness: `desktop/tests/Tor.Control.Tests`; no NuGet test framework or
Tor process required for its nine mock/protocol groups. Native lifecycle smoke
requires explicit `--windows-smoke` arguments and a separately verified existing
runtime; see the Windows adapter handoff. This driver does not authenticate a
device, implement pairing or qualify public Tor operation.
