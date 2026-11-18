# Windows peer messaging decisions

The current peer implementation uses a loopback listener and a temporary
v3 Onion Service. Tor's existing SAFECOOKIE, owned SOCKS endpoint, remote
name resolution and Windows Job boundaries continue to apply. The local
authenticated transport remains mutual TLS with a full certificate SHA-256
pin. These decisions use the existing standard libraries.

## 1. Out-of-band pairing

The user compares the peer's complete certificate fingerprint over an
authenticated channel and explicitly saves the pair. An onion address may
be saved as an endpoint for that pin. Resolving an onion, observing an
incoming certificate or receiving application data cannot establish trust.
An unpaired peer offers no interactive override inside the TLS handshake.

## 2. Trust store, revocation and rotation

Each explicitly created profile has an owner-private store. The store
records the pin, address and user-visible label in versioned data with
atomic publication. Duplicate pins and automatic replacement are refused.
Revocation is explicit, durable and preserves the revoked pin. A legitimate
new certificate is a new, separately checked out-of-band pairing. Seamless
automatic rotation would change the trust guarantee and is deferred.

## 3. Onion and TLS identity ownership

Keep the v3 Onion Service key in Tor. The current connection publishes a
temporary service and the visible onion address can change on Tor restart.
Persist the application's user-approved TLS credential separately per
private app profile. Protect it for the Windows user with OS cryptographic
protection and owner-private ACLs. Reopen the same credential at restart.
Unreadable or lost state is a visible error. Recovery and key migration
require an explicit operator decision. Fresh directories are used in tests.

## 4. Acknowledgement and duplicate handling

Extend the application protocol with a durable, randomly generated message
ID and explicit authenticated ACK. Receipt means the receiver durably
accepted the message before responding. It conveys no read receipt. A
retried message uses the same ID. The authenticated sender pin scopes the
ID and the receiver remembers it for the valid message lifetime.
An expired message is never delivered again on reconnect. Report pending,
delivered, expired and failed independently from the transport state.
Delivery after an ambiguous failure is retryable within the TTL and bounded
by receiver deduplication. Avoid an exactly-once product guarantee.

## 5. Crash-consistent queue

Bound item count, payload size, total serialized bytes and retention.
Persist the outbox before sending, durable deduplication before ACK and
receipt before the sender claims delivery. Publish a complete private
snapshot with an atomic replacement on the same filesystem. Serialize
writers and reject malformed metadata. Interrupted sends remain pending
and state is keyed by the pinned peer. Recovery never weakens verification
or redirects messages to a changed peer identity.

## 6. History, encryption and retention

Display bounded live message and delivery state by default. Delivery state
required for reconnect is separate from opt-in history and inherits the
profile protections. Persistent message payloads require user-scoped
encrypted storage, a retention limit and a deliberate opt-in for history.
Restrict instrumentation to status and counts. History export, emergency
recovery and re-keying of an existing profile require separate decisions.

## 7. Versions and migration

Carry on accepting version 1 for the existing explicit transient workflow
where both clients choose it. Negotiate reliable messaging as a distinct
authenticated ALPN version. Report incompatible peers visibly instead of
silently dropping guarantees. Older apps have no paired credential to
migrate. Any imported identity or trusted contact requires explicit review.
