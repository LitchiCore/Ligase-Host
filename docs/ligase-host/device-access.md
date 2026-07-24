# Ligase Host device access

This document freezes the Host-owned access and revocation boundary for paired
GameStream clients. It does not change attended-pairing v1 public wire fields.

## Product modes

| Mode | Core permission | Allowed |
| --- | --- | --- |
| `operate` | all current GameStream permissions | Read the library, launch or resume, send input, end the client's session, and make authorized Ligase Sync streaming changes |
| `observe` | view and list only | Read server info, Sync, applist, assets, device state, and safely resume an existing view-only session |

`operate` is the default for a new attended request. The pending UI exposes an
unchecked “Observe only” option. Observe mode cannot launch a new application,
cancel or quit a session, mutate Sync ordering/resolution, or inject any input.
Unknown or custom permission masks project as observe, so the UI fails closed.

An operate-to-observe downgrade updates the core's named-device record and the
live session's device information synchronously. The existing input permission
checks therefore reject subsequent controller, touch, mouse, and keyboard
packets. An observe-to-operate upgrade affects subsequent requests without
reviving an expired pairing continuation.

## Loopback routes

All routes below return `404` to non-loopback callers and require canonical
lowercase UUID D path segments.

| Route | Body | Success | Stable errors |
| --- | --- | --- | --- |
| `PUT /ligase/v1/pairing/requests/{requestId}/access` | `{"mode":"operate"}` or `{"mode":"observe"}` | `204` | `400 invalidRequest/invalidAccessMode`, `404 requestNotFound`, `409 invalidState` |
| `PUT /ligase/v1/devices/{deviceId}/access` | same | `200 {"accessMode", "uuid"}` | `400 invalidRequest/invalidAccessMode`, `404 deviceNotFound` |
| `DELETE /ligase/v1/devices/{deviceId}` | exactly empty | `204` | `400 unexpectedBody`, `409 deviceActive` |
| `POST /ligase/v1/devices/{deviceId}/end-session-and-delete` | exactly empty | `204` | `400 unexpectedBody` |

Pending access must succeed before the UI invokes the existing attended
empty-body Allow action. The request-local selection is erased with its
terminal pairing state or process restart.

Paired access writes are owned by the core's named-device persistence and are
reflected immediately by `GET /ligase/v1/devices` as
`access_mode=operate|observe`. Delete is idempotent for an already absent
device. Active deletion is deliberately two-step; only the explicitly named
end-session route can stop the device's session before revoking its certificate
and named-device record.

An authenticated paired client reads its own effective mode from HTTPS
`/serverinfo`:

```xml
<LigaseClientAccessMode>operate</LigaseClientAccessMode>
```

The value is `operate` or `observe`. It is emitted only after HTTPS client
certificate verification; public HTTP serverinfo omits it. Only the exact full
permission set with client commands enabled reports `operate`. Unknown or
custom permission combinations report `observe` so clients fail closed. A
permission update is reflected by the next HTTPS serverinfo request.

## Enforcement

The certificate maps the connection and RTSP session to the named device UUID.
Core authorization remains authoritative:

- launch/resume/cancel use the named-device permission;
- all input injection paths use the live session permission;
- Ligase Sync writes require launch permission;
- HTTPS `GET /ligase/v1/sync`, `/applist`, and `/appasset` require list
  permission and remain available to observe mode;
- an observe client without list permission fails closed with no library names,
  UUIDs, ordering, metadata, or cover content.

Neither the Desktop UI nor Android can elevate access by editing a product
projection. Android has no permission mutation or deletion entry point.
