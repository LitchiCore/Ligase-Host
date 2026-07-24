# Ligase Host Android sync contract

## Transport and identity

- All endpoints use Apollo's paired GameStream HTTPS server.
- Detect support from `serverinfo/LigaseSyncVersion == 1`; the advertised
  `LigaseSyncPath` is `/ligase/v1/sync`.
- Ligase Android must treat a Host without Sync v1 as incompatible and show an
  upgrade-required state. It must not rebuild the product library from the
  legacy applist response.
- Resolve the port from `serverinfo/HttpsPort`; do not derive it from the
  configured HTTP base port. A default Ligase instance uses HTTP `48989` and
  paired HTTPS `48984`.
- Reuse the client certificate and TLS behavior already used by `NvHTTP`.
- The application key is the Apollo `UUID`. Merge the sync item `id` with the
  matching `applist/App/UUID`.
- Keep the numeric GameStream `ID` from `applist` for launch compatibility.
  Launch continues to send both `appuuid` and `appid`.
- Never use the display name as an identity.
- Collection ownership, global publication, local hiding, and attended pairing
  are defined in
  [`multi-client-product-contract.md`](multi-client-product-contract.md).

## Product protocol versus transport ABI

Ligase does not preserve compatibility with Apollo's product configuration,
Web UI data model, installed Windows service, or hand-edited `apps.json`.
Ligase-owned JSON and Sync v1 are the product protocol.

The following GameStream behavior remains a transport ABI until Ligase replaces
the streaming transport itself:

- `pair` and `unpair`;
- `serverinfo`, including `uniqueid`, `hostname`, `HttpsPort`, `PairStatus`,
  `state`, `currentgame`, `currentgameuuid`, `appversion`,
  `ServerCodecModeSupport`, and `GfeVersion`;
- `LocalIP`, `ExternalIP`, `ExternalPort`, and `mac` while remote discovery and
  wake-on-LAN remain supported;
- `applist`, where only `UUID` and numeric `ID` enter the Ligase launch adapter;
- `appasset(appid)`, `launch`, `resume`, and `cancel`;
- the existing RTSP, video, audio, input, encryption, controller, mode, HDR,
  and session parameters.

`launch` and `resume` continue to submit both `appuuid` and `appid`. Their
responses retain `gamesession`/`resume` and optional `sessionUrl0`; `cancel`
retains `cancel`.

`Permission`, `VirtualDisplayCapable`, `VirtualDisplayDriverReady`,
`ServerCommand`, `MaxLumaPixels*`, and `gputype` are not Sync v1 library fields.
They may remain in the transport response while internal dependencies exist,
but Android must not treat them as authoritative library metadata.

## Read the complete snapshot

`GET /ligase/v1/sync`

```json
{
  "schemaVersion": 1,
  "capabilities": {
    "hdrEncodingSupported": true
  },
  "library": {
    "revision": 12,
    "updatedAt": "2026-07-23T05:30:00Z",
    "sortMode": "manual",
    "items": [
      {
        "id": "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91",
        "kind": "steam",
        "name": "Example",
        "steamAppId": 123,
        "system": false,
        "publishedToClients": true,
        "addedAt": "2026-07-23T05:00:00Z",
        "updatedAt": "2026-07-23T05:00:00Z",
        "lastPlayedAt": null
      }
    ]
  },
  "streaming": {
    "schemaVersion": 1,
    "revision": 4,
    "updatedAt": "2026-07-23T05:20:00Z",
    "globalResolution": {
      "width": 1920,
      "height": 1080
    },
    "apps": {
      "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91": {
        "resolution": {
          "width": 2560,
          "height": 1440
        }
      }
    }
  }
}
```

Host paths, working directories, and commands are intentionally excluded.

When `sortMode` is `manual`, `library.items` is in the Host's shared canonical
order. Android and other clients preserve that array order after UUID-only
merging with `applist`. Name, added-time, and last-played sorting are local view
choices: a client may apply them without writing Host state. Older snapshots
may still contain `nameAscending`, `nameDescending`, `addedNewest`,
`addedOldest`, or `lastPlayedNewest`; clients parse those values for
compatibility, but new product writes use `manual`.

The two system entries participate in the shared manual order like other
published items. They can move, but they remain protected from hide/delete
operations. UUID identity never changes when order changes.

`publishedToClients` is an additive Sync v1 field controlled only by Host. A
missing value from an older Sync v1 snapshot means visible. Android must parse
it as nullable and calculate `hostPublished = value != false`; it must not let a
local-hidden-game action modify this field. Both system entries are always
published in the first implementation.

HDR capability has intentionally separate meanings:

- `capabilities.hdrEncodingSupported` is the Host's current runtime ability to
  encode the HDR/10-bit stream. It comes from Apollo's active encoder probe.
- Android must independently determine whether its current decoder and display
  can present HDR.
- Product-level HDR availability is true only when both sides support it.
- Do not interpret the legacy applist `IsHdrSupported` value as evidence that a
  particular game actually produces HDR content.
- The legacy applist value remains inside the GameStream adapter and must not be
  copied into the authoritative library domain.

Effective resolution:

1. Use `streaming.apps[appUuid].resolution` when present.
2. Otherwise use `streaming.globalResolution`.
3. Host applies the same rule again during `/launch`, so the synchronized Host
   configuration is authoritative.

Android may use applist only after a successful Sync v1 snapshot. Merge strictly
by UUID to obtain the numeric `appid`. If a sync item has no applist UUID match,
show a synchronization error for that item and disable launch. Never fall back
to a name match.

## Update streaming resolution

`POST /ligase/v1/streaming`

Update the global default:

```json
{
  "baseRevision": 4,
  "globalResolution": {
    "width": 2560,
    "height": 1440
  }
}
```

Set an application override:

```json
{
  "baseRevision": 5,
  "app": {
    "id": "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91",
    "resolution": {
      "width": 1920,
      "height": 1080
    }
  }
}
```

Clear an override and return to the global setting:

```json
{
  "baseRevision": 6,
  "app": {
    "id": "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91",
    "resolution": null
  }
}
```

The response is the updated `streaming` object. Width must be between 320 and
16384; height must be between 240 and 16384.

## Update shared manual library order

`POST /ligase/v1/library/sort`

Only a paired client with `operate` permission may call this route. The request
contains the revision from the last Sync snapshot and every currently
published application UUID exactly once, including both system UUIDs, in the
desired order:

```json
{
  "baseRevision": 12,
  "orderedAppUuids": [
    "78a25216-f239-45bd-b4aa-f41c814066e9",
    "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91",
    "9af5103b-1dc0-4562-8421-62f95d855a8a",
    "8902cb19-674a-403d-a587-41b092e900ba"
  ]
}
```

`baseRevision` is a JSON integer token in `1..9007199254740991`; decimal,
exponent, zero, negative, and overflowing values are invalid. UUIDs must already
be lowercase canonical D form. The sequence is rejected if it has a duplicate,
an unknown or missing published UUID, or an unpublished UUID. Names, numeric
app IDs, paths, and fuzzy matching are never accepted.

Both system entries are sent in the requested sequence and are reorderable,
while their hide/delete protection remains unchanged. Unpublished Host-only
entries are not sent or returned. Host retains them after the public sequence
without exposing their names, UUIDs, paths, or relative order. A newly added
published item is appended to the public manual sequence. Hiding an item removes
it from the public sequence; publishing it again appends it to the public
sequence.

Success is HTTP `200` with the minimal public projection:

```json
{
  "revision": 13,
  "sortMode": "manual",
  "orderedAppUuids": [
    "78a25216-f239-45bd-b4aa-f41c814066e9",
    "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91",
    "9af5103b-1dc0-4562-8421-62f95d855a8a",
    "8902cb19-674a-403d-a587-41b092e900ba"
  ]
}
```

Host UI manual up/down actions use the same domain coordinator and validation.
Name, added-time, and last-played controls are local view-only operations and
never call this route.

Malformed revision or UUID/order input returns HTTP `400` with
`{"error":"invalidManualOrder"}`. Observe clients return HTTP `403`
`permissionDenied`. A persistence or projection failure returns HTTP `500`
`libraryUpdateFailed` without exception text or Host paths.

## Revision conflicts

Streaming writes and shared manual ordering require the revision from the last
successful snapshot.
HTTP `409` returns:

```json
{
  "error": "revisionConflict",
  "currentRevision": 13
}
```

On conflict, discard the optimistic local write, fetch
`GET /ligase/v1/sync`, merge by UUID, and let the user retry. Do not retry a
stale write automatically.

### Verified conflict behavior

The Android/Host joint test on 2026-07-23 established the required behavior:

1. Android held streaming revision `4`.
2. Host advanced the authoritative snapshot to revision `5` without changing
   the effective 1600×900 setting.
3. Android submitted an application override with stale `baseRevision: 4`.
4. Host returned HTTP `409`.
5. Android did not replay the write, immediately fetched Sync v1, and loaded
   revision `5`, `apps: {}`, and the unchanged global resolution.
6. The UI used natural-language conflict text and asked the user to perform the
   operation again.

HTTP status remains sufficient for the conflict branch, but the Android
transport must also retain the JSON error response body for diagnostics. Losing
the body must not change the no-replay rule or tempt either side to infer errors
from exception text.

### Verified launch behavior

The V2353A/Host joint test on 2026-07-23 established the complete launch path:

- Android launched the desktop item by its synchronized canonical UUID and
  numeric applist ID, sending both `appuuid` and `appid`.
- The request carried `mode=1600x900x60` from the effective synchronized
  resolution and `virtualDisplay=0` for the physical desktop entry.
- Host returned `gamesession=1` and RTSP port `50010`.
- RTSP negotiation, HEVC decode, and the Android 1600×900 rendering surface all
  succeeded.
- Host reported HDR encoding capability, while V2353A reported no usable HDR
  display path. Android therefore launched with HDR disabled, and the runtime
  log confirmed `Display HDR mode: disabled`.

This is the required HDR layering behavior: Host capability alone never enables
HDR. The launch request also proves that Sync UUID identity, applist numeric ID,
resolution inheritance, and the GameStream transport adapter remain separate
layers.

Other relevant errors are `400` for an invalid payload, `403` for insufficient
paired-client permission, `404 appNotFound`, and `404 syncUnavailable`.
