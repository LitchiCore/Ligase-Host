# Authenticated device presence v1

`device-presence-v1.schema.json` is the machine owner for the Android-to-Host
heartbeat and for the presence/session fields projected by the Host Core. The
Android client and Host must review the same fixed schema and vectors before
either production implementation is committed.

## Authority and route

- Android sends `POST /ligase/v1/device-presence/heartbeat` over the existing
  paired HTTPS channel every 5 seconds while its Host connection is actively
  maintained.
- The body is exactly `{ "schemaVersion": 1 }`. It contains no UUID, address,
  timestamp, device name, or secret. Core derives the device UUID only from the
  already verified client certificate (`get_verified_cert`).
- A successful response is the exact `heartbeatResult` shape. The Core records
  only a monotonic in-memory receipt time keyed by that verified UUID. Receipt
  times are not persisted and are not accepted from the client.
- A heartbeat is live for 15 seconds. A newer valid heartbeat replaces only
  the same UUID's prior receipt. Unpaired, stale-certificate, malformed, or
  unauthorized requests receive no presence credit.

## Foreground, background, and restart boundary

Android owns scheduling, not presence truth. It sends at 5 seconds while the
app is foreground, or while an active streaming foreground service already
keeps the authenticated Host connection alive. On ordinary backgrounding it
sends no final heartbeat and must not create or extend a background service
merely to look online. Android Doze, force-stop, process death, or loss of
authentication therefore expires to offline within 15 seconds.

Android selects exactly one target. An active stream's authenticated Host ID
owns the target while that stream exists. Otherwise, while foreground, the
currently selected Host may be targeted only if that same Host still has a
maintained authenticated channel. Known/paired cards, IP addresses, and
`lastUsed` never cause fan-out. Ordinary background with no active stream sends
nothing; `onStop` does not send a final heartbeat or start/extend a service.

Each target/lifecycle change creates a new generation. The generation sends
immediately, then at monotonic ticks `start + N * 5000ms`. At most one request
may be in flight; a tick that finds one in flight is skipped rather than queued.
Leaving foreground, changing the selected/stream Host, stream disconnect, and
process cancellation invalidate the generation. A late response from an old
generation is discarded and never refreshes UI or a future ticket.

## HTTP envelope

The request is `POST` to the exact route with request `Content-Type` and
`Accept` both `application/json`; redirects are disabled. Request timeout is
3000ms. The client reads at most 512 response bytes and requires one complete
JSON object followed by EOF, without duplicate keys, BOM, trailing data, or a
second body. Only HTTP 200 with response `Content-Type: application/json` and
the exact `heartbeatResult` is accepted. Empty/truncated/oversized bodies,
timeouts, cancellation, TLS/client-auth failure, redirects, and every non-200
status receive no client success credit. The server receipt—not the response—
is the Host presence authority, so the Android client never declares itself
online from a response.

Freshness is the exact monotonic predicate `now - receipt < 15000ms`; equality
at 15000ms is expired. For the first 15 seconds after Core startup, a paired UUID with no fresh
heartbeat is `unknown`, not offline. A fresh heartbeat makes that UUID online
immediately. The warm-up predicate is likewise `uptime < 15000ms`; at exactly
15000ms a UUID without a receipt is offline. After the warm-up deadline, a paired UUID without a live receipt
is offline. If Desktop cannot read the Core projection, Desktop marks retained
cards unknown and does not reuse an old receipt as authority.

## Presence and session are independent

`presenceState` describes the authenticated client heartbeat only.
`sessionState` describes the current RTSP session only:

- online + none: **在线 · 无活动会话**
- online + streaming/observing: **在线 · 正在串流/观察**
- offline + none: **离线**
- unknown: **状态未知** (session may still be shown separately when proven)

RTSP membership never upgrades presence, and lack of RTSP never downgrades an
otherwise live heartbeat. Pairing records, IP addresses, UI activation,
persisted `lastSeen`, and cached device lists are never presence sources.

## Failure and privacy boundary

Heartbeat parsing is exact: extra keys and wrong types are rejected. Rate or
transport failures do not mutate pairing or session state. Logs and UI may
record the schema version and a one-way request correlation, but never client
certificate bytes, addresses, secrets, or a client-supplied timestamp.
