# Windows Tor peer messaging

## Connect two clients

1. Start each Manager with its verified native Tor runtime. Select **Real
   Online**, pick separate SOCKS ports if sharing a Windows host, then select
   **Start Tor**. Wait for authenticated control, an owned SOCKS listener and
   bootstrap at 100%.
2. In **Peer messaging**, explicitly create the first local peer identity.
   Exchange **Your certificate SHA-256** over a trusted authenticated channel.
   On each side enter a contact name and the other party's 64-character
   fingerprint, confirm the comparison, and select **Pair contact**. Select
   that contact before hosting or connecting.
3. On A, select **Host Onion Service**. Share the displayed v3 address with
   B. Registration may precede Tor's descriptor publication. On B, enter the
   full address and select **Connect to Peer**. Retry explicitly if Tor is
   still publishing the service or after an interruption.
4. Wait for **Connected · peer authenticated with mutual TLS**. Send text
   from either side. The receiving panel displays the latest message only.
   **Disconnect** closes the peer session. On A it also removes the service.

## Architecture and trust

A binds an exclusive IPv4 `127.0.0.1` listener on a free OS-selected port.
Authenticated Tor Control registers `NEW:ED25519-V3` for virtual port 80 with
`DiscardPK`. The management connection owns the registration. The application
never gets the onion private key. B connects to the owned local Tor SOCKS5
socket and sends the onion hostname as a SOCKS domain field. The application
performs no local DNS lookup and has no direct fallback or rendezvous server.

The onion address identifies the Tor service. Application identity additionally
requires a peer-controlled P-256 key proven by mutual TLS over that connection.
Each private profile explicitly creates one certificate and requires the full
SHA-256 hash of the selected trusted remote certificate to match. There is no trust on first use,
public CA dependency or online certificate lookup. An attacker who controls
fingerprint exchange can substitute an identity. A party controlling a peer's
process or TLS key can impersonate that peer. A Tor relay or a local outsider
with only the onion address cannot authenticate as the peer. Denial of service,
traffic correlation, endpoint compromise and local privileged users are outside
this identity guarantee. The selected Windows account protects the saved
certificate with DPAPI in the owner-private `peer-security` subdirectory
of the configured app state. The trust store ties the saved labels, pins and
revocations to that local certificate. An invalid or missing file in an
existing profile blocks pairing and reuse. Revocation protects future
sessions and cannot be undone by entering the same pin. A legitimate new
certificate requires a fresh out-of-band comparison and an explicit pairing.
Windows Schannel temporarily uses an OS user key container until disposal.
[Tor Control commands](https://spec.torproject.org/control-spec/commands.html) ·
[Tor SOCKS extensions](https://spec.torproject.org/socks-extensions.html) ·
[.NET TLS server authentication](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.authenticateasserverasync)

## Protocol version 1

Mutual TLS 1.2 or 1.3 must negotiate ALPN `tor-peer/1`. Resumption and
renegotiation are disabled. The client writes `54 50 01 43` after validating
the server pin. The server waits for it after validating the client pin, then
replies `54 50 01 53`. Only then may the client mark the peer connected.

| Field | Width | Value |
| --- | ---: | --- |
| Magic | 2 bytes | ASCII `TP` |
| Version | 1 byte | `1` |
| Type | 1 byte | `1` text or `2` close |
| Sequence | 4 bytes | Unsigned big-endian, starts at one in each direction |
| Length | 4 bytes | Unsigned big-endian payload byte count |
| Payload | 0–16,384 bytes | Nonempty strict UTF-8 text, or empty for close |

The receiver validates type, length and the next sequence before allocating a
payload. Invalid or duplicate sequences, malformed frames, failed reads and
damaged TLS records end the session. Independent read and write allow two-way
traffic without an unbounded queue. Per-message I/O is bounded to two minutes.
The TLS establishment deadline is 12 seconds after the SOCKS stream opens.
The SOCKS operation is bounded to 45 seconds. A controlled close sends the
close frame before disposing the stream. An interrupted connection is reopened
with fresh mutual TLS and reset directional sequences. TLS protects against
captured-record replay across sessions. Messages are never retransmitted
automatically and this version does not provide delivery acknowledgements or
durable exactly-once processing. Status events are typed and do not include
raw diagnostics, credentials or message payloads. Message text is shown only
in the live messaging panel.

The onion is temporary. It remains available during peer reconnect while A's
host operation and Tor stay alive. Restarting A or its Tor requires a new
onion registration. Restarting the app with the same intact private profile
retains its TLS fingerprint and active contact pins.

## Authorized qualification

The offline suite uses loopback TLS and SOCKS fixtures. A separate opt-in
workflow runs the pinned native Tor client on Windows. Obtain explicit approval
before passing the flag. Use a new private path and an existing verified runtime.

```powershell
pwsh -NoProfile -File scripts/test_windows_online.ps1 -PeerToPeer -ApprovePublicTor `
  -TorPackage 'C:\path\to\verified-tor-runtime' `
  -NewStatePath 'C:\path\to\test-workspace\new-peer-state'
```

The workflow checks both bootstraps, authenticated `HS_DESC UPLOADED` for A's
service, mutual TLS and two-way integrity. It stops B's Tor to interrupt the
connection, restarts B and verifies a second exchange through the same onion.
It then checks protocol close, onion removal and both process exits.
