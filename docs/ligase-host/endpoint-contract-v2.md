# Ligase Endpoint Contract v2

Status: frozen for Host and Android implementation.

This contract describes connection locations. It does not define Host identity.
`hostUniqueId` and the pinned X.509 certificate identify a Host; an endpoint is
only one transient way to reach it.

## Endpoint value

```json
{
  "scheme": "http",
  "host": "fe80::1234",
  "port": 48989,
  "zone": "wlan0",
  "source": "mdns"
}
```

Fields:

- `scheme` is one of `http`, `https`, or `rtsp`. The GameStream/serverinfo
  discovery endpoint uses `http`. Its HTTPS port still comes from serverinfo;
  clients must not derive it from the HTTP port.
- `host` is a hostname, canonical IPv4 literal, or canonical IPv6 literal. It
  never contains brackets, a port, or a zone suffix.
- `port` is an integer from 1 through 65535.
- `zone` is nullable and is only valid for IPv6 link-local unicast addresses.
- `source` is nullable and is one of `manual`, `mdns`, `local`, `remote`, or
  `loopback`. It affects candidate preference, never identity.

Address family is derived by the strict parser and is not persisted.

## Normalization and validation

- Hostnames are trimmed, converted to IDNA ASCII, lowercased, and validated for
  empty labels, label length, total length, control characters, and invalid
  label syntax.
- IPv4 literals use canonical decimal dotted notation.
- IPv6 literals use canonical compressed lowercase notation.
- IPv6 multicast addresses are outside v2 endpoint scope.
- Link-local IPv6 requires a non-empty `zone`; there is no implicit default
  interface.
- Global, unique-local, and loopback IPv6 reject a `zone`.
- A numeric zone is a decimal interface index from 1 through 4294967295.
- A named zone is trimmed, contains 1 through 128 Unicode scalar values, and
  may contain interior spaces. It rejects control characters and
  `%`, `[`, `]`, `/`, `\`, `?`, `#`, or `:`.
- A stored zone never contains the `%` or `%25` delimiter.

Strict model input rejects scheme text, paths, queries, fragments, user info,
brackets, or an embedded port in `host`.

## Formatting

Display keeps address and port in separate fields. A URI formatter constructs
the authority:

- `apollo.local` -> `apollo.local:48989`
- `192.0.2.10` -> `192.0.2.10:48989`
- `2001:db8::10` -> `[2001:db8::10]:48989`
- `fe80::1234` with zone `Ethernet 2` ->
  `[fe80::1234%25Ethernet%202]:48989`

The formatter, not the caller, adds IPv6 brackets and encodes the zone. A
transport adapter resolves a scoped `Inet6Address`/socket address; callers must
not pass the RFC 6874 authority directly to a library that cannot preserve the
scope.

Logs may display an endpoint but must not describe it as Host identity.

## Identity and candidates

A Host record is keyed by normalized `hostUniqueId` and separately pins its
certificate. The same Host can have multiple endpoint candidates. A changed
address or hostname does not create a new Host.

Candidate deduplication uses:

```text
(scheme, normalizedHost, normalizedZone, port)
```

`source` is excluded from this key. Every successful public serverinfo probe is
accepted only after its normalized `uniqueid` matches the target Host. Paired
HTTPS additionally uses the pinned certificate.

## Persistence and migration

The endpoint-set serialization is:

```json
{
  "version": 2,
  "endpoints": [
    {
      "scheme": "http",
      "host": "192.0.2.10",
      "port": 48989,
      "zone": null,
      "source": "manual"
    }
  ]
}
```

Writers emit `version: 2` and the `endpoints` array. Optional null fields may be
omitted. Readers continue to accept Android's existing
`local`/`remote`/`manual`/`ipv6` `{address, port}` slots and map them without
changing the Host UUID or pinned certificate. The first successful write uses
v2. Existing IPv4 values are not re-resolved through DNS and their ports are
not changed.

Bracketed IPv6 and legacy `host:port` text are accepted only by the manual-entry
migration parser, normalized immediately, and never persisted in that form.

## Candidate selection

- A previously healthy candidate may be tried first.
- IPv6 and IPv4 candidates are interleaved.
- Attempts start with a 250 ms stagger.
- Each candidate has a 3 second connect/probe deadline.
- The entire selection has a 5 second deadline.
- The first healthy serverinfo response with the expected `hostUniqueId` wins;
  remaining attempts are cancelled.
- A TCP connection without a valid, matching serverinfo response is not a win.
- IPv6-only and IPv4-only environments use their available candidates without
  waiting for the absent family.

Failures are reported in natural language and distinguish no route, timeout,
identity mismatch, certificate mismatch, and an incompatible Host.

## Manual entry

The normal UI has two fields: computer address and port (default 48989). It
accepts hostname, IPv4, IPv6, and link-local IPv6 plus zone. Scheme, path,
query, fragment, and user info are rejected. Brackets and embedded legacy
ports are migration-only input.

## Listener and discovery requirements

New Host listeners use dual-stack where the OS supports it and retain explicit
IPv6-only and IPv4-only modes. Listener configuration is separate from
advertised candidates. mDNS may advertise multiple addresses for one stable
Host identity. Certificate generation, pairing, and attended-pairing
transcripts must not bind identity to an IP address.

Complete LAN acceptance requires IPv4-only, IPv6-only, dual-stack, global/ULA,
link-local zone, mDNS multi-address, firewall, RTSP, and native streaming tests.
Loopback tests alone do not satisfy that gate.

## Standards basis

- RFC 4007: IPv6 scoped address architecture and zone indices.
- RFC 6874: `%25` delimiter for an IPv6 zone in URI literals.
- RFC 8305: staggered IPv6/IPv4 connection attempts; v2 fixes the initial
  implementation delay at 250 ms.
