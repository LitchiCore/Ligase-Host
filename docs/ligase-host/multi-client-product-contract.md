# Ligase multi-client product contract

This document freezes the product ownership, visibility, pairing, and visual
semantics shared by Ligase Host and Ligase Android. The Host implementation is
the source of truth for this contract.

## Data ownership

| Data | Authority | Android rights |
| --- | --- | --- |
| Application collection and launch definition | Host | Read and launch only |
| Steam discovery/import and non-Steam add/remove | Host | None |
| `publishedToClients` for each UUID | Host | Read only |
| Library sort mode | Host-persisted shared preference | Read and update with `baseRevision` |
| Global and per-application streaming settings | Host-persisted shared preference | Read and update with `baseRevision` |
| Hidden only on one Android device | That Android installation, partitioned by Host | Local read/write only; never uploaded |

The Host never interprets an Android omission as a delete. Android never sends
Windows paths, commands, working directories, collection replacements, add,
remove, or global visibility mutations.

## Global publication and local hiding

Every Sync v1 library item has the additive field:

```json
{
  "id": "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91",
  "publishedToClients": true
}
```

`publishedToClients` is keyed by the stable item UUID. A missing field from an
older Sync v1 Host means `true`; Android must therefore model it as nullable
during migration and calculate `hostPublished = value != false`. This default
prevents an older Host from accidentally hiding its entire library.

The effective Android visibility rule is:

```text
visible = item.publishedToClients != false
          && (hostUniqueId, item.id) is not in this device's locallyHiddenUuids
```

Host-global hiding wins. The Android “Hidden games” screen may only remove UUIDs
from its own local set; it cannot restore an item whose Host publication is
false. Renaming, removing an item from a partial response, or matching by name
must never represent visibility.

`locallyHiddenUuids` is partitioned by the stable GameStream
`serverinfo.uniqueid`. Its logical key is `(hostUniqueId, appUuid)`, with both
UUID strings normalized to lowercase only for storage and lookup. It is not one
installation-wide bare app UUID set. This prevents two Hosts that contain the
same application UUID from sharing a local-hide decision. Changing a Host
address or display name does not change the partition.

Both system entries (`desktop` and `virtualDesktop`) are non-hideable in the
first implementation. They remain published and the Host UI explains that they
are recovery entry points. This avoids leaving a new client with no desktop
entry.

## Host library interaction

- Add remains a dedicated Host navigation area.
- Every non-system library card exposes details, global client visibility, and
  delete.
- Delete requires confirmation using the application name and the consequence:
  “This removes it from Ligase Host and it will disappear from every client.”
- System entries cannot be deleted.
- Hiding is reversible and does not delete launch configuration or history.
- A successful add, remove, or visibility change increments the library
  revision, regenerates the Apollo launch projection, and atomically refreshes
  the Sync snapshot.

`ApplicationLibrary` remains the single mutation boundary. Desktop view models
invoke intent-sized methods and do not write JSON, Apollo manifests, or Sync
documents directly. `LigaseSyncDocumentWriter` remains a projection writer and
must not become a second library state source.

## One-click attended pairing

### User experience

1. Android selects a discovered Ligase Host and requests pairing.
2. Host shows one pending device with its device name, platform, request age,
   and a short safety fingerprint.
3. The user clicks **Allow** or **Reject** on the Host.
4. Both sides show the final paired or rejected state. No visible PIN exists.

Pairing is never automatic merely because the client is on the LAN. A request
expires after 120 seconds. Repeating the same request refreshes the existing
pending card rather than creating duplicates. Reject immediately invalidates
the request. Allow is one-shot.

### Capability and transport

The public HTTP `serverinfo` response advertises both fields:

```xml
<LigaseAttendedPairingVersion>1</LigaseAttendedPairingVersion>
<LigaseAttendedPairingPath>/ligase/v1/pairing/requests</LigaseAttendedPairingPath>
```

Android uses this flow only when the version is exactly `1` and the path is
present. Enrollment uses the GameStream HTTP port because the existing HTTPS
listener correctly requires an already-paired client certificate. No secret is
sent in plaintext over HTTP. Paired Sync and launch traffic remain on HTTPS.

### Cryptographic suite

Version 1 has exactly one suite:

- key agreement: X25519, 32-byte public keys and shared secret (RFC 7748);
- derivation: HKDF-SHA-256 (RFC 5869);
- authenticated encryption: ChaCha20-Poly1305 with a 32-byte key, 12-byte
  nonce, and 16-byte tag (RFC 8439);
- canonical JSON: RFC 8785 JCS encoded as UTF-8;
- binary JSON fields: unpadded base64url from RFC 4648 section 5;
- hashes and certificate fingerprints: SHA-256.

Both sides reject an all-zero X25519 shared secret, duplicate JSON property,
invalid UTF-8, padded/non-canonical base64url, unknown property, or value with
the wrong exact length. There is no cipher-suite negotiation in version 1.
An X25519 public key is the RFC 7748 raw 32-byte little-endian u-coordinate. It
is not an X.509 SubjectPublicKeyInfo structure, a PKCS#8 structure, or a
provider-specific encoded key object.

### Public request schema

Android creates a fresh X25519 key pair, 32-byte random `clientNonce`, UUID v4
`requestId`, and its normal GameStream client certificate before calling:

```http
POST http://host:httpPort/ligase/v1/pairing/requests
Content-Type: application/json
```

```json
{
  "version": 1,
  "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
  "device": {
    "name": "Phone",
    "platform": "android"
  },
  "clientEphemeralKey": "<32-byte base64url>",
  "clientNonce": "<32-byte base64url>",
  "clientCertificateSha256": "<32-byte base64url>"
}
```

`device.name` is 1–80 Unicode scalar values after trimming; `platform` is
exactly `android`. The Host returns `201`:

`requestId` must already be a lowercase canonical UUID in D form. The
`device.name` wire value must already be trimmed; if trimming changes it, the
request is invalid rather than silently normalized. The socket source identity
contains the binary IP address and, for link-local IPv6 only, its positive
scope ID. It never contains the source port and ignores `Forwarded` and
`X-Forwarded-For`. IPv4-mapped IPv6 is normalized to the same IPv4 identity.

```json
{
  "version": 1,
  "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
  "requestToken": "<32 random bytes, base64url>",
  "hostUniqueId": "<serverinfo uniqueid>",
  "hostCertificateSha256": "<32-byte base64url>",
  "hostEphemeralKey": "<32-byte base64url>",
  "hostNonce": "<32-byte base64url>",
  "expiresAt": "2026-07-23T07:30:00Z"
}
```

`clientCertificateSha256` is exactly
`SHA-256(client X.509 certificate DER bytes)`. It is not a hash of PEM text,
SPKI, PKCS#8, hexadecimal text, or a pair-query representation.

`hostCertificateSha256` is exactly
`SHA-256(current Host X.509 certificate DER bytes)` under the same rule. The
Host computes and displays the safety code from the certificate actually loaded
by its HTTPS listener; it must not trust or round-trip the fingerprint contained
in its own HTTP response as an independent source.

`requestToken` is an opaque bearer credential used only for this pending
request. It has at least 256 bits of entropy, is stored hashed by Host, is never
included in a URL, and expires with the request.

While a request is live, the coordinator owns mutable token bytes so an exact
idempotent create can return the original response; the authentication index
stores only the token hash. Terminal transition overwrites and releases the
token bytes. The bounded terminal cache retains the token hash and exact source
identity so status and DELETE remain authenticated, plus status and envelope
replay metadata. These are sensitive authentication metadata: they are never
logged or persisted and are cleared on cache eviction. It never retains the
original token bytes.

After strict parsing, an exact create retry is identified by the same canonical
request JCS bytes and the same exact source identity. JSON whitespace and
property order therefore do not affect equality; no field value is ignored.
An exact retry returns `200` with the original response and does not consume
rate or live-certificate quota. The same request ID from another source, or
with different canonical request bytes, returns `409 requestIdConflict` and
never returns the original token. Only a live `pending` or `approved` exact
retry can return `200`.
A create using a cached expired request ID returns `410 requestExpired`; a
create using a cached `paired`, `rejected`, `cancelled`, or `failed` request ID
returns `409 requestIdConflict`. After terminal-cache eviction the request ID
is treated as new and remains subject to current rate and live-certificate
quota.

Before deriving any key or uploading an envelope, Android must validate the
create response:

- `version` is exactly `1`;
- `requestId` is byte-for-byte equal to the UUID string sent in the request;
- `hostUniqueId`, compared after the same lowercase UUID normalization used by
  the Host repository, equals `uniqueid` from the public `serverinfo` response
  that initiated this exact pairing attempt.

Any mismatch immediately fails enrollment and triggers Android cleanup. Android
must not canonicalize a mismatched response, derive a key, upload an envelope,
or start legacy pairing.

### Transcript, key derivation, envelope, and safety code

The transcript object has exactly two properties. `request` is the complete
create request. `response` is the complete create response with
`requestToken` removed. The unique complete structure is:

```json
{
  "request": {
    "version": 1,
    "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
    "device": {
      "name": "Phone",
      "platform": "android"
    },
    "clientEphemeralKey": "<32-byte base64url>",
    "clientNonce": "<32-byte base64url>",
    "clientCertificateSha256": "<32-byte base64url>"
  },
  "response": {
    "version": 1,
    "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
    "hostUniqueId": "<serverinfo uniqueid>",
    "hostCertificateSha256": "<32-byte base64url>",
    "hostEphemeralKey": "<32-byte base64url>",
    "hostNonce": "<32-byte base64url>",
    "expiresAt": "2026-07-23T07:30:00Z"
  }
}
```

There are no flattened duplicate `version` or `requestId` properties. Both
sides compute:

```text
transcriptBytes = JCS(transcript)
transcriptHash  = SHA-256(transcriptBytes)
salt            = SHA-256(clientNonce || hostNonce)
pairingKey      = HKDF-Extract(salt, x25519SharedSecret)
                  then HKDF-Expand(
                    info = UTF8("Ligase attended pairing v1") || 0x00
                           || transcriptHash,
                    L = 32)
safetyDigest    = HMAC-SHA-256(
                    pairingKey,
                    UTF8("Ligase attended pairing SAS v1") || 0x00
                    || transcriptHash)
safetyCode      = RFC 4648 base32 of first 5 bytes of safetyDigest, using
                  alphabet `ABCDEFGHIJKLMNOPQRSTUVWXYZ234567`, uppercase,
                  no padding; display the resulting 8 characters as
                  `XXXX-XXXX`
```

The Host and Android display the same `safetyCode`. Android says “Confirm this
code appears on the computer”; Host says “Only allow if this code matches the
device.” The code is not entered anywhere and is not the legacy PIN. A mismatch
requires Reject.

Android generates the four-digit legacy PIN with a cryptographic RNG and
uploads:

```http
PUT http://host:httpPort/ligase/v1/pairing/requests/{requestId}/envelope
Authorization: Bearer <requestToken>
Content-Type: application/json
```

```json
{
  "nonce": "<12 random bytes, base64url>",
  "ciphertext": "<ciphertext followed by 16-byte tag, base64url>"
}
```

The plaintext is the JCS UTF-8 encoding of:

```json
{"legacyPin":"1234"}
```

This plaintext is exactly 20 bytes and must match the byte template
`{"legacyPin":"dddd"}`, where every `d` is ASCII `0`–`9`. The nonce is exactly
12 bytes and decoded `ciphertext` is exactly 36 bytes: 20 ciphertext bytes
followed by the 16-byte tag. After AEAD verification the Host validates the
plaintext byte-for-byte and immediately clears it.

ChaCha20-Poly1305 uses `pairingKey`, the supplied nonce, and
`transcriptHash` as AAD. A nonce may be used only once with this key. Host
returns `204` and retains the encrypted envelope only while it is required for
local approval. After consumption or terminal cleanup it retains only nonce
and `SHA-256(JCS(envelope))` replay metadata. The hash covers the complete,
strictly parsed envelope object, comparisons are constant-time, and metadata is
cleared on ten-minute cache eviction. For a non-expired request with existing
metadata, the same nonce and envelope is an idempotent `204` even after
`paired`, `rejected`, `cancelled`, or `failed`; the same nonce with different
ciphertext is `409 nonceReuse`; any other envelope is `409 envelopeConflict`.
An expired request always returns `410`. A terminal request without prior
envelope metadata returns `409 invalidState`.

Android then starts the existing GameStream `getservercert` phase with its
internally generated PIN and adds
`ligasepairingrequestid={requestId}` to that first `/pair` request. Host matches
the pending request ID, client-certificate SHA-256, and source request before
holding the existing asynchronous response. It never accepts a request ID on
later pair phases.

When `getservercert` returns the actual Host certificate, Android must parse the
X.509 certificate, hash its complete DER bytes with SHA-256, and compare the
result in constant time with transcript `hostCertificateSha256` before sending
any later GameStream challenge phase. A mismatch immediately cancels the held
pair operation, terminates or rejects enrollment, and runs cleanup. Human SAS
comparison remains the primary attended check; this certificate comparison
also binds the machine-consumed legacy pairing channel to the reviewed
transcript.

### Status polling, cancellation, and local approval

Android polls at no more than once per second:

```http
GET http://host:httpPort/ligase/v1/pairing/requests/{requestId}
Authorization: Bearer <requestToken>
```

```json
{
  "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
  "state": "pending",
  "expiresAt": "2026-07-23T07:30:00Z",
  "failure": null
}
```

Allowed states are `pending`, `approved`, `paired`, `rejected`, `cancelled`,
`expired`, and `failed`. The status response always has exactly
`requestId`, `state`, `expiresAt`, and `failure`. `failure` is non-null only for
`failed`, where it is one of `certificateMismatch`, `pairSessionMissing`,
`cryptoFailure`, or `legacyPairingFailed`; it is `null` for every other state,
including `cancelled`. The original `expiresAt` remains in every terminal
response.

Android cancels with an empty-body request:

```http
DELETE http://host:httpPort/ligase/v1/pairing/requests/{requestId}
Authorization: Bearer <requestToken>
```

With a valid token, exact source, and an entry still in the live or ten-minute
terminal cache, DELETE always returns `204`. `pending` or `approved` transitions
to `cancelled` and destroys the held operation and secrets. An existing
terminal state remains unchanged; in particular, `paired` never unpairs a
device. After cache eviction it returns `404`. DELETE is not a late mutation
and never returns `410`.

The pending-list and approval endpoints are loopback-only:

```text
GET  /ligase/v1/pairing/requests
POST /ligase/v1/pairing/requests/{requestId}/allow
POST /ligase/v1/pairing/requests/{requestId}/reject
```

The loopback list has the exact shape:

```json
{
  "version": 1,
  "requests": [
    {
      "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
      "device": {"name": "Phone", "platform": "android"},
      "state": "pending",
      "readyForApproval": true,
      "safetyCode": "ABCD-EFGH",
      "createdAt": "2026-07-23T07:28:00Z",
      "expiresAt": "2026-07-23T07:30:00Z",
      "sourceAddress": {"address": "fe80::1", "scopeId": 7}
    }
  ]
}
```

Only live `pending` and `approved` requests are listed, ordered by `createdAt`
then `requestId`. `sourceAddress.address` is canonical dotted IPv4 or lowercase
compressed IPv6 without brackets or zone. Link-local IPv6 requires a positive
`scopeId`; all other addresses forbid it.

`readyForApproval` becomes true only after a valid envelope and the first
`getservercert` request have been bound to the request ID, certificate
fingerprint, exact source identity, and a safely held nvhttp response. Pending
events and Windows notifications are emitted only on the false-to-true edge.
Allow is disabled while false and returns `409 invalidState` with
`detail=notReadyForApproval` and `currentState=pending` if invoked anyway.
Reject and public cancel remain available before readiness.

The only readiness transition is `pending false` to `pending true`, under the
request lock, and it emits exactly one event. `approved` performs no readiness
transition: it can be entered only from `pending` with readiness true, so every
approved live-list projection reports `readyForApproval=true`; the public
status response remains the frozen four-field shape and has no readiness field. Terminal
state is absent from the live list; its last internal readiness value never
changes or emits another event. Envelope and
`getservercert` may arrive in either order; the second condition to arrive
performs the single false-to-true transition while holding the request lock.
If expiry or another terminal transition wins, no readiness event is emitted.

Allow returns `202` when it changes `pending` to `approved`, decrypts the PIN
only in memory, injects it into the matching held `nvhttp` session, and erases
PIN, shared secret, ephemeral private key, and envelope immediately after the
legacy certificate step consumes them. Repeated allow for `approved` or
`paired` returns `200` with current status. Allow for `rejected`, `cancelled`,
`expired`, or `failed` returns `409 invalidState`.

Reject changes `pending` or `approved` to `rejected`, returns `200` with current
status, and destroys the held session and all secrets. Repeated reject for
`rejected` returns `200`; reject for `paired`, `cancelled`, `expired`, or
`failed` returns `409 invalidState`. Reject may revoke an approved request
before pairing commits.

Allow, reject, cancel, envelope mutation, and the asynchronous
`approved`-to-`paired` commit are serialized by the same per-request lock or
CAS generation. Pairing commits only from `approved`. Every continuation checks
both state and generation. If reject or cancel wins, no asynchronous certificate
commit may occur; if paired wins, later DELETE remains a no-op `204`.

### Routes, validation order, and machine errors

All JSON requests reject unknown and duplicate properties. Validation order is:
invalid UTF-8/JSON, duplicate property, unknown property, missing required
property, wrong type, then invalid value. Within one class, the first path is
the RFC 6901 pointer whose escaped (`~0`, `~1`) UTF-8 bytes compare first as
unsigned bytes. Error responses have exactly
`{"code":"...","detail"?:...,"path"?:...,"currentState"?:...}`. Optional fields
appear only where frozen below; exception text is never returned.

`duplicateField`, `unknownField`, `missingField`, `invalidType`, and
`invalidValue` always include `path`. `invalidJson` and `unexpectedBody` never
include it. No other application code includes `path`;
`invalidEnvelope.authenticationFailed` and
`invalidEnvelope.invalidPlaintext` do not include it.
Whole-document `invalidValue`, including an empty or oversized body, uses the
RFC 6901 root pointer `path=""`.

For every public route containing a request ID, a malformed or non-canonical
lowercase D UUID path segment is `404 requestNotFound`; a canonical ID not in
the live or terminal cache is also `404`, both before Authorization handling.
For a known ID, a missing bearer header, wrong scheme, invalid whitespace,
non-canonical base64url, wrong token length/hash, or exact-source mismatch is
`401 invalidRequestToken` with no optional fields. Hash comparison is
constant-time. Create has no bearer authentication and follows the sole ordered
create pipeline defined below.

Public route processing is:

- envelope: canonical path and lookup, bearer/token/source authentication,
  lock and materialize monotonic expiry (an expired request returns `410`
  without parsing the body), media/body/strict JSON and decode, nonce plus
  canonical-envelope hash, existing replay-metadata decision, then (only for
  the first envelope) pending-state check, AEAD and strict plaintext;
- status: request lookup, token plus exact-source authentication, then return
  current state, including cached terminal state;
- cancel: request lookup, token plus exact-source authentication, then the
  idempotent cancel transition.

Every request route locks the request and materializes monotonic expiry before
it observes or changes state. A deadline-expired pending/approved request is
therefore first changed to `expired`; status cannot report it pending and
DELETE cannot change it to cancelled. Materialization changes only live
`pending` or `approved` to `expired` and performs terminal cleanup. Existing
`paired`, `rejected`, `cancelled`, and `failed` states are never rewritten by
the old deadline.

The loopback list and actions reject non-loopback callers as `404`. Allow and
reject accept an empty body only; a non-empty body is `400 invalidRequest` with
`detail=unexpectedBody`. They perform request lookup, expiry/state/readiness,
then the serialized action. A missing request is `404`; a state conflict is
`409 invalidState` with `currentState`, and readiness conflict additionally has
`detail=notReadyForApproval`.

Create processing has one order: route/method match, Content-Type,
Content-Encoding, actual bounded body read, UTF-8/JSON, duplicate, unknown,
missing, type, value, request-ID lookup/source/canonical equality, exact retry,
then rate/live-fingerprint quota and new creation. Create requires
`application/json`; type and subtype compare
ASCII-case-insensitively. It may have no parameters or one
`charset=utf-8` parameter whose name and value compare case-insensitively and
whose value may use standard HTTP quoted-string syntax. Missing content type,
another type, another parameter, or duplicate parameters is
`415 unsupportedMediaType`. Create has a 4096-byte body limit and envelope has
a 1024-byte limit. Their body must be non-empty; an empty or oversized body is
`400 invalidRequest` with `detail=invalidValue` and `path=""`. Actual decoded
body bytes determine the limit; a declared Content-Length over the limit may
be rejected early, and chunked input must still remain within the limit.

Content-Encoding must be completely absent for create and envelope. Any value,
including `identity`, `gzip`, or duplicate headers, is
`415 unsupportedMediaType` with `detail=contentEncodingNotSupported` and no
path. Envelope applies Content-Type, Content-Encoding, actual body length, and
strict JSON/decode after lookup/authentication and locked expiry, before replay,
state, and AEAD.

Status, cancel, loopback list, allow, and reject require an actual zero-byte
body. Content-Type is ignored whether present or absent. Any non-zero body,
including decoded chunked content, is `400 invalidRequest` with
`detail=unexpectedBody` and no path.

Public routes check canonical path and lookup, then authentication/source,
lock and expiry, then body/media/schema and route state/action. Create follows
its separate strict-body, request-ID/idempotency, quota order. Loopback actions
check loopback first (`404` otherwise), then canonical path/lookup, lock and
expiry, body, and action. The loopback list has no request lock: it checks
loopback, body, then snapshots and sorts.

| Application code | HTTP | Frozen optional fields / use |
| --- | --- | --- |
| `invalidRequest` | 400 | `detail` is `invalidJson`, `duplicateField`, `unknownField`, `missingField`, `invalidType`, `invalidValue`, or `unexpectedBody`; schema errors include `path` |
| `unsupportedMediaType` | 415 | `detail=contentEncodingNotSupported` only when Content-Encoding is present; otherwise no optional fields |
| `invalidRequestToken` | 401 | no optional fields; also covers source mismatch after lookup |
| `requestNotFound` | 404 | no optional fields |
| `requestIdConflict` | 409 | no optional fields |
| `clientCertificateBusy` | 409 | no optional fields |
| `invalidState` | 409 | `currentState`; loopback not-ready also has `detail=notReadyForApproval` |
| `invalidEnvelope` | 400 | `detail` is `authenticationFailed` or `invalidPlaintext` |
| `nonceReuse` | 409 | no optional fields |
| `envelopeConflict` | 409 | no optional fields |
| `requestExpired` | 410 | no optional fields; expired non-cancel mutation only |
| `rateLimited` | 429 | no optional fields |
| `pairingUnavailable` | 503 | no optional fields |

Public endpoint results:

| Condition | Status |
| --- | --- |
| Created | `201` |
| Exact idempotent create | `200` with the original response |
| Envelope accepted | `204` |
| Poll accepted | `200` |
| Cancel accepted or terminal idempotent cancel | `204` |
| Invalid schema/encoding | `400 invalidRequest` |
| Unsupported content type | `415 unsupportedMediaType` |
| Invalid/absent bearer token | `401 invalidRequestToken` |
| Unknown request | `404 requestNotFound` |
| Same request ID with different transcript | `409 requestIdConflict` |
| Expired non-cancel mutation | `410 requestExpired` |
| Rate limit exceeded | `429 rateLimited` |

On `rejected`, `cancelled`, `expired`, or `failed` observed by ordinary polling,
Android cancels the held legacy pair operation, stops polling, best-effort
clears PIN, bearer token, private/shared/pairing key, nonce and owned secret
arrays, releases references, and shows that terminal result. It does not replay
create, envelope, or legacy pair.

Outside the user-cancel flow below, `rejected`, `cancelled`, `expired`, or
`failed` from create, envelope, status, or held `getservercert`; HTTP `401`,
`404`, or `410`; a transport failure; or the local monotonic timeout all have
the same fail-closed cleanup behavior. Android stops periodic polling, cancels
the held operation, best-effort clears PIN, bearer, private/shared/pairing key,
nonce, every owned secret and reference, presents a natural-language retry
action, and never automatically replays create, envelope, or legacy pair.
`paired` performs successful-pair cleanup and continues. There is no
route-specific exception in v1. This ordinary enrollment branch is distinct
from the DELETE-`204` one-shot authenticated status probe below.

User cancellation is separate because DELETE returns an empty `204` for both a
newly cancelled request and an already-terminal request. Android first stops
creating pair steps and ordinary polling, then sends DELETE. On `204`, it
cancels the local held call and immediately clears PIN, private/shared/pairing
key, nonce, held-call references, and every secret except bearer bytes/verifier
plus the request ID and exact source required for one final query. It performs
exactly one authenticated status GET against the same endpoint and source, with
an independent short timeout and without resuming periodic polling:

- `cancelled`, `rejected`, `expired`, or `failed` is displayed as that terminal
  result and completes cleanup;
- `paired` means pairing won the race; Android accepts the paired device,
  performs no unpair, and enters paired state;
- `pending` or `approved` is a protocol error and becomes local `failed`
  without replay;
- `401`, `404`, `410`, or transport failure clears the remaining bearer,
  marks the result unknown, and offers discovery/recheck. Later serverinfo or
  device discovery may independently establish whether pairing completed.

After the one GET completes or fails, Android clears the bearer and releases
the coordinator. A non-`204` DELETE response also causes local best-effort
cleanup and no replay. DELETE never unpairs an already-paired device.

Android implements this flow in an independent
`AttendedPairingRepository`/`AttendedPairingCoordinator`, not in
`LigaseActivity`. The coordinator owns create, envelope upload, the concurrent
held `getservercert` operation, polling at no more than 1 Hz, terminal
cancellation, and memory cleanup. The existing `PairingManager.pair()` receives
only the smallest transport extension needed to add
`ligasepairingrequestid` to the first `getservercert` request. Later pair phases
must reject or omit that parameter.

Android API 23 implementations use the APK-pinned Bouncy Castle lightweight
APIs rather than relying on platform-provider availability. Gson serialization
is not JCS. Android must use an RFC 8785 canonicalizer verified against published
vectors and must perform strict duplicate-property, unknown-property, and
canonical-base64url validation around DTO parsing.

The Android test gate includes fixed vectors for RFC 7748, RFC 5869, RFC 8439,
RFC 8785, and the Ligase SAS derivation, plus negative tests for duplicate and
unknown properties, non-canonical base64url, an all-zero shared secret,
certificate mismatch, local monotonic timeout, and replay. Gson may deserialize
only data that has already passed strict validation; it is not the JCS
implementation and does not enforce duplicate-property rejection.

Bearer token, legacy PIN, private key, shared secret, and ciphertext are never
persisted or logged. Rejection, expiry, cancellation, transport failure, and
process teardown cancel the held HTTP operation and erase those values from
reachable memory as far as the managed/runtime APIs permit.

For Android, cleanup means overwriting every mutable byte or character array it
owns for the legacy PIN, bearer token, nonce, shared secret, pairing key, and
raw private key, then releasing all coordinator/repository references. Cleanup
also cancels the held HTTP call and scheduled poll. Immutable strings must not
be used for these secrets where a byte or character array is possible. This is
a best-effort managed-runtime guarantee: JVM, garbage collector, HTTP stack,
and cryptographic-provider internal copies cannot be claimed to be physically
erased with certainty.

Requests expire 120 seconds after creation using Host monotonic time. Exact
retries do not extend expiry. Host restart invalidates all pending requests.
Completed request IDs remain in a bounded replay cache for ten minutes. Create
is limited to five requests per rate key per minute and at most one live
request per client-certificate fingerprint. The rate key is IPv4 `/32`, native
IPv6 `/64`, or link-local IPv6 `/64` plus scope ID. Exact source binding remains
the full normalized address (plus link-local scope), not the rate prefix.

`expiresAt` is a display and diagnostic wall-clock value, not Android's sole
security clock. When Android receives the `201` response, it records a local
deadline using `SystemClock.elapsedRealtime()` with a maximum lifetime of 120
seconds from receipt. Android expires the operation at the earlier of that
local deadline or a terminal Host response. Wall-clock changes never extend the
local deadline. Recreated UI observes the coordinator deadline; it does not
create a new one.

The public endpoint reveals no paired-client list. Logs must not contain the
PIN, bearer token, shared secret, ephemeral private key, envelope plaintext, or
complete ciphertext.

### Conformance vector file schema

Before public routes are registered, the Host produces one UTF-8 JSON vector
file which Android copies byte-for-byte. Until Android accepts the file format
and content, it is a draft and has no frozen SHA-256. The inventory below names
required material but is not the fixture JSON shape. The mechanically
authoritative draft shape is
[`attended-pairing-v1-vectors.schema.json`](attended-pairing-v1-vectors.schema.json):
its root is `{schemaVersion,draft,cases}`, and every case is an
`operation`-discriminated closed object. The JSON block below is an inventory
of required cryptographic material only; it is not a second case schema.

```json
{
  "schemaVersion": 1,
  "primitiveVectors": {
    "rfc7748": [{
      "id": "",
      "source": "",
      "privateKey": "",
      "peerPublicKey": "",
      "expectedPublicKey": "",
      "expectedSharedSecret": ""
    }],
    "rfc5869": [{
      "id": "",
      "source": "",
      "ikm": "",
      "salt": "",
      "info": "",
      "length": 32,
      "expectedOkm": ""
    }],
    "rfc8439": [{
      "id": "",
      "source": "",
      "key": "",
      "nonce": "",
      "aad": "",
      "plaintext": "",
      "expectedCiphertextAndTag": ""
    }],
    "rfc8785": [{
      "id": "",
      "source": "",
      "inputUtf8Base64url": "",
      "expectedJcsUtf8Base64url": ""
    }]
  },
  "ligaseVector": {
    "clientPrivateKey": "",
    "clientPublicKey": "",
    "hostPrivateKey": "",
    "hostPublicKey": "",
    "clientNonce": "",
    "hostNonce": "",
    "requestToken": "",
    "requestTokenSha256": "",
    "createRequest": {},
    "createRequestJcsUtf8Base64url": "",
    "createResponse": {},
    "createResponseWithoutTokenJcsUtf8Base64url": "",
    "transcriptJcsUtf8Base64url": "",
    "transcriptHash": "",
    "salt": "",
    "sharedSecret": "",
    "pairingKey": "",
    "safetyDigest": "",
    "safetyCode": "",
    "envelopeNonce": "",
    "envelopePlaintextUtf8Base64url": "",
    "envelopePlaintextJcsHash": "",
    "envelopeCiphertextAndTag": "",
    "envelopeObject": {},
    "envelopeJcsHash": ""
  }
}
```

The file is UTF-8 without BOM, uses LF line endings, two-space indentation, one
final LF, and the exact producer property/array order shown by the accepted
schema. The published SHA-256 covers every file byte including the final LF.
It is not JCS-reformatted before hashing.

Every binary field, including raw invalid JSON and canonical UTF-8, uses
unpadded base64url; there is no alternative hex representation. UUIDs are
lowercase canonical D form, timestamps are UTC RFC 3339 with `Z`, and source
addresses use the frozen structured form.
Primitive cases have stable `id`, an authoritative RFC section in `source`,
exact inputs, and exact outputs.

Every negative case has exactly `id`, `operation`, `context`, `input`,
`expectedHttpStatus`, `expectedResponseBody`, `expectedPostState`, and
`expectedCleanupMetadata`. State cases add a deterministic `schedule`; every
step has a named linearization action and exact expected state/generation.
Invalid UTF-8 and duplicate-property cases are represented only by
`input.requestUtf8Base64url`, never by a JSON object. Race cases declare an
exact step schedule and winner rather than relying on thread timing. Cases
cover DELETE in every live/terminal/cache state,
allow/reject/cancel/pair commit winners, exact/different create source and token
replay, rate ordering, IPv4-mapped/IPv6/link-local identity, validation pointer
ordering, all three envelope replay branches, and post-cleanup replay
decisions. RFC primitive vectors remain separate from the Ligase end-to-end
vector.

Every HTTP/race context is a closed, inline coordinator snapshot. Behavior and
race cases never resolve fixture, source, token, nonce, or hash references from
another case. `incomingSource` is the actual caller and `storedExactSource` is
the sole stored source verifier. `incomingBearerBase64url` contains the raw
32-byte token and `storedTokenHashBase64url` contains
`SHA-256(raw request-token bytes)`; the runner compares the computed digest in
constant time. Positive vectors publish both values explicitly.

Every behavior and race case carries an ordered, unique `validationRules`
array. The global precedence is:

1. `strictHttpOrder`
2. `strictJsonFirstError`
3. `canonicalUuid`
4. `canonicalBase64url`
5. `sourceNormalization`
6. `tokenSha256Authentication`
7. `monotonicExpiryBeforeState`
8. `requestLockLinearization`
9. `envelopeReplayBeforeFirstUploadState`
10. `generationGuard`
11. `terminalCleanup`

Create requires rules 1-5. Envelope requires all eleven. Public status and
cancel require rules 1 and 3-7 plus 8, 10, and 11. The loopback list requires
rules 1 and 5; loopback allow/reject require rules 1, 3, 7, 8, 10, and 11.
Loopback callers still fail the loopback route gate before these rules. Race
and Android-cancel cases require rules 3-11. Binary primitive cases require
rule 4; JCS primitives require rules 2 and 4; the positive end-to-end vector
requires rules 2, 3, 4, and 6. The schema fixes each permitted array exactly; a
missing, additional, duplicated, or reordered rule invalidates the whole
vector file. Rules are evaluated in listed order, and the first failed rule is
the case result. They cover cross-field invariants that JSON Schema cannot
express, including deadline ordering, state/presence combinations, scope
validity, digest equality, readiness prerequisites, generation monotonicity,
and contiguous race steps.

This preserves the existing GameStream `pair` phases, client certificate,
server certificate, challenge/response, and paired storage. The old visible
`/api/pin` path remains available only as a temporary advanced compatibility
path until Android has shipped the attended-pairing protocol; it is not shown
in the normal Ligase UI. Removal requires a separately announced capability
version and successful two-device migration validation.

## Shared semantic color tokens

Host documentation is the common semantic source. Platform code maps these
tokens to native resources; product code consumes semantic names rather than
hex values.

| Token | Light | Dark | Meaning |
| --- | --- | --- | --- |
| `brand.primary` | `#6258D9` | `#968BFF` | Primary action and brand focus |
| `brand.secondary` | `#168FA6` | `#55C8D7` | Secondary brand/accent information |
| `background` | `#F5F7FC` | `#15171D` | Window or screen background |
| `surface` | `#FFFFFF` | `#20232C` | Cards, dialogs, raised regions |
| `text.primary` | `#1B1B1F` | `#F3F1FA` | Primary readable content |
| `text.secondary` | `#5F6270` | `#C8C5D0` | Supporting content |
| `border` | `#DCE3EF` | `#343947` | Dividers and component outlines |
| `selected` | `#ECEAFE` | `#302D52` | Selected row/tile background |
| `success` | `#148361` | `#56D19B` | Completed/healthy |
| `warning` | `#9A6700` | `#F2C14E` | Attention or recoverable risk |
| `error` | `#C42B1C` | `#FF8A80` | Failed or destructive |

Android maps these roles into a Material 3 `ColorScheme`; Host maps them into
WinUI theme resources. Native disabled, hover, pressed, focus, elevation, and
dynamic accessibility behavior remain platform-owned. Text/icon combinations
must meet WCAG AA contrast in their actual context; matching hex values never
overrides accessibility.

## Migration gates

1. Android first accepts nullable `publishedToClients`, defaulting missing to
   visible, and adds its UUID-keyed local-hidden store and “Hidden games” page.
2. Host then emits the field and adds Host-only visibility/delete UI.
3. Pairing advertises
   `LigaseAttendedPairingVersion=1` and `LigaseAttendedPairingPath`. Android
   uses it only when both are valid; otherwise the temporary advanced PIN
   compatibility flow remains.
4. Visible PIN removal occurs only after V2353A and AGS2-AL00 complete pairing,
   re-pair, reject, timeout, replay, restart, Sync, and launch validation.
5. Existing paired certificates and GameStream launch/media transport are not
   migrated or regenerated by this feature.
