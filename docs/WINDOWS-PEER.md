# Windows Tor peer messaging

## Connect two clients

1. Start each Manager with its verified native Tor runtime and select
   **Real Online**.
2. In **Peers**, explicitly create the first local peer identity.
   Exchange **Your certificate SHA-256** over a trusted authenticated channel.
   On each side open **Pair a contact or replace a certificate**, enter a
   contact name and the other party's 64-character fingerprint, confirm the
   comparison, and select **Pair contact**. Select that contact before hosting
   or connecting. The same page supports pairing and queueing in **Real Offline**.
3. On both PCs choose the SOCKS port and select **Start Tor**. Wait for
   authenticated control, an owned SOCKS listener and bootstrap at 100%.
   On A, select **Create and host saved address** on first use, or **Host saved
   address** on a later launch. Share the displayed v3 address with
   B. Registration may precede Tor's descriptor publication. On B, enter the
   full address, select **Save contact address** to retain it, and select
   **Connect to Peer**. A transient connection
   failure triggers up to four retry attempts. While the UI remains open,
   a replacement Tor session can resume the same pinned destination. Editing
   the onion or contact or choosing **Disconnect** cancels that intent.
4. Wait for the paired mutual TLS status. **Send or queue** persists a
   bounded message as pending even if Tor is currently stopped. The entry
   shows delivered after the receiver's ACK, or expired if its deadline
   passes first. **Disconnect** closes the peer session. On A it also
   removes the service.

Use **Recent connection events** on the selected contact for the last
12 transitions in this app session. To replace a remote certificate, select
the original contact, verify the new full fingerprint out of band, and
choose **Revoke old pin and pair replacement**. The replacement starts a
new conversation. The revoked pin retains its historical association with
the old queue.

## Architecture and trust

A binds an exclusive IPv4 `127.0.0.1` listener on a free OS-selected port.
The authenticated management connection owns the virtual port 80 registration.
**Host temporarily** uses `NEW:ED25519-V3 Flags=DiscardPK`. Creating a
saved address requests the key from Tor, then stores it in an owner-private
DPAPI record under the current profile's `onion-hosting` directory. The
record is separate from the application TLS certificate and contact pins.
On restore, the app supplies the key through authenticated Control and checks
the returned service ID against the saved one. A corrupt or inaccessible
record blocks hosting. Rotation requires confirmation of the current saved
address and changes it only after the new registration and protected write
succeed. Share the new address following rotation.
B connects to the owned local Tor SOCKS5
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

An active saved host registers the same address when the replacement Tor
session becomes ready. After a Manager restart, select the paired contact and
choose **Host saved address**. The same intact private profile also retains its
TLS fingerprint and active contact pins. A temporary host gets a new address
for each new registration.

## Reliable protocol version 2

Version 2 uses only ALPN `tor-peer/2`. The TLS setup and protocol-ready
exchange retain both certificate pin checks and use version byte 2. The
outer header remains bounded by the same 16 KiB payload and independent
sequence rules. A nested application record is either
`M <32 hex ID> <Unix UTC expiry> <canonical Base64 UTF-8>` or
`A <32 hex ID>`. Text is limited to 8,192 UTF-8 bytes. An idle connection
waits until the first byte of a record with cancellation still active. The
rest of the record has the existing I/O deadline.

The same random message ID and original 15-minute expiry follow the
message after any reconnect. The recipient durably commits the contents
under the authenticated pin before ACK. A duplicate with the same content
and expiry returns the ACK again. Reuse with different content is an error.
The sender records delivered only after a matching ACK on an authenticated
connection where the ID was sent. TTL expiry, an invalid queue or exhaustion
is observable and cannot imply delivery. This is bounded retry with durable
deduplication within the expiry window. It provides no human read receipt.

Each contact uses a profile-local DPAPI-encrypted file with up to 64 outbox
and 64 inbox entries and a 1 MiB clear serialization limit. A completed
outbox retains descriptive metadata while the payload is removed. The
inbox keeps the delivered text and its deduplication record until expiry,
which is processed on access and during a connected session. It can be
viewed again after a restart within this window. Delivery storage is
separate from optional conversation history. **Archive new delivered
messages** opts in per contact, records subsequently received or acknowledged
text and retains it for up to seven days or 64 entries. It uses the same
encrypted storage size budget. If more space is needed for delivery, the
oldest archived entries are evicted. Turning off archival preserves previous
entries until retention removes them. Expiration is applied on profile access
and while connected.

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

### Persistent address on one Windows host

The persistent-host variant uses two independent native Tor sessions and a
verified Developer `optimized-static` build. First create a candidate JSON
using `scripts.windows_developer candidate` from the approved build workspace.
Keep the candidate file and fresh state path in the ignored test workspace.
Compare the returned `tor.exe` digest with the recorded build evidence.

```powershell
python -B -m scripts.windows_developer candidate --workspace $buildWorkspace --sources $sourceCache --profile optimized-static --build-id $buildId |
    Set-Content -LiteralPath $candidateJson -Encoding utf8NoBOM
if ($LASTEXITCODE) { throw 'Verified runtime candidate generation failed.' }
pwsh -NoProfile -File scripts/test_windows_online.ps1 -ApprovePublicTor `
    -RuntimeCandidate $candidateJson -NewStatePath $freshStatePath
```

The runner verifies the selected runtime in an isolated approval store. It
starts both clients, creates A's DPAPI-protected v3 identity and waits for
authenticated descriptor publication. B exchanges exact test messages in both
directions with A through its owned SOCKS listener and mutual TLS. The runner
stops A, reopens its service and profile and imports the saved key. It checks
the same v3 address and TLS fingerprint, observes publication again, repeats
the message exchange, and checks all owned Tor exits. A recent descriptor can
still be cached by B, so the restored connection uses bounded retries.
Connection failures report the stage and the SOCKS reply code when Tor
returns one. Console diagnostics omit the address, pins and key material.
The console runner exercises the application backend on one Windows host.
The physical test below covers graphical and independent-host acceptance.

## Two physical PCs with the graphical workflow

With separate authorization, stage the same reviewed Windows x64 artifact
on each physical PC. Pass its independently recorded apphost and assembly
SHA-256, the pinned Tor package directory, a fresh run ID and each distinct
role to `scripts/manual/prepare_peer_gui_qualification.ps1`. The switch
`-ApprovePublicTor` records approval for this manual workflow. The script
verifies the named input files, opens the GUI with a new state directory and
records the launch as NOT RUN until an operator records the actual steps.

1. Complete the pairing steps above with independent profiles and confirm
   the displayed fingerprints over the approved trusted channel. First use
   a different fixture fingerprint and record that the expected peer cannot
   authenticate. Pair the intended test identity with a fresh explicit action.
2. Start Tor and record bootstrap 100% on each PC. Host A and enter the
   actual address on B. Verify both authenticated GUI statuses.
3. Exchange two known harmless messages in both directions. Record the exact
   content match and sequence. Confirm delivered appears after receiving
   the corresponding message. Record opt-in status and encrypted history,
   using the isolated test profile.
4. While A remains active, stop B's Tor, queue one message on B, and start
   Tor again. Verify same-pin reconnect, one reception and a transition
   from pending to delivered. Repeat with B's app restarted using the same
   test state. Restart A's Tor and verify that it rehosts the identical saved
   address. Reopen A's Manager in its same test profile and select **Host
   saved address**. B must reconnect to the original address and fingerprint.
5. Exercise explicit disconnect and close both apps. Confirm owned Tor
   PIDs exit. Preserve independent private state locally.

Record screenshots with fixture content. Redact onion addresses, fingerprints,
private paths and account details in shared evidence. Save sanitized
observations and measurements in the ignored
`artifacts/windows-peer/<run>-<role>/` directory. Keep private state and
authentication material on their own PCs.
