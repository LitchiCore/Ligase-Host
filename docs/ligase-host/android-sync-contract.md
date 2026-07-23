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
    "sortMode": "nameAscending",
    "items": [
      {
        "id": "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91",
        "kind": "steam",
        "name": "Example",
        "steamAppId": 123,
        "system": false,
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

## Update library sort mode

`POST /ligase/v1/library/sort`

```json
{
  "baseRevision": 12,
  "sortMode": "lastPlayedNewest"
}
```

Allowed values:

- `nameAscending`
- `nameDescending`
- `addedNewest`
- `addedOldest`
- `lastPlayedNewest`

The response is the updated `library` object. Android may update only the sort
mode. The Host remains the authority for adding and removing applications.

## Revision conflicts

Both write endpoints require the revision from the last successful snapshot.
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

Other relevant errors are `400` for an invalid payload, `403` for insufficient
paired-client permission, `404 appNotFound`, and `404 syncUnavailable`.
