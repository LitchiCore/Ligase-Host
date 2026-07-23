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

ChaCha20-Poly1305 uses `pairingKey`, the supplied nonce, and
`transcriptHash` as AAD. A nonce may be used only once with this key. Host
returns `204` and retains only the encrypted envelope until local approval.

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

### Status polling and local approval

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

Allowed states are `pending`, `approved`, `paired`, `rejected`, `expired`, and
`failed`. `failure`, when present, is one of `certificateMismatch`,
`pairSessionMissing`, `cryptoFailure`, or `legacyPairingFailed`.

The pending-list and approval endpoints are loopback-only:

```text
GET  /ligase/v1/pairing/requests
POST /ligase/v1/pairing/requests/{requestId}/allow
POST /ligase/v1/pairing/requests/{requestId}/reject
```

Allow returns `202`, changes the state to `approved`, decrypts the PIN only in
memory, injects it into the matching held `nvhttp` session, and erases PIN,
shared secret, ephemeral private key, and envelope immediately after the
legacy certificate step consumes them. Successful legacy pairing changes the
state to `paired`. Reject returns `200`, changes the state to `rejected`, and
destroys the held session and all secrets.

Public endpoint results:

| Condition | Status |
| --- | --- |
| Created | `201` |
| Exact idempotent create | `200` with the original response |
| Envelope accepted | `204` |
| Poll accepted | `200` |
| Invalid schema/encoding | `400 invalidRequest` |
| Invalid/absent bearer token | `401 invalidRequestToken` |
| Unknown request | `404 requestNotFound` |
| Same request ID with different transcript | `409 requestIdConflict` |
| Expired terminal request or late envelope | `410 requestExpired` |
| Rate limit exceeded | `429 rateLimited` |

On `rejected`, `expired`, `failed`, `401`, `404`, or `410`, Android immediately
cancels the held legacy pair operation, erases its PIN and ephemeral private
key, and shows a natural-language retry action. It does not automatically replay
the request. On `paired`, it erases enrollment state and continues the existing
certificate-paired flow.

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
is limited to five requests per source address per minute and at most one live
request per client-certificate fingerprint.

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
