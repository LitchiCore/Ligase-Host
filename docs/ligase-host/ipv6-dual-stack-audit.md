# IPv6 / dual-stack Host audit

Status: infrastructure stage I implemented; full LAN acceptance remains open.

## C++ core

- HTTP and HTTPS select `0.0.0.0` for `address_family=ipv4` and `::` for
  `address_family=both`.
- RTSP session URLs already use `net::addr_to_url_escaped_string`, which adds
  brackets around IPv6 literals.
- ENet uses `AF_INET6` in `both` mode.
- Network classification already recognizes IPv4-mapped IPv6, IPv6 loopback,
  ULA, and link-local ranges.
- Windows, Linux, and macOS have mDNS publishers. Publishing is associated
  with the service/Host name, not used as Host identity.
- The legacy GameStream `LocalIP` serverinfo field intentionally remains
  `127.0.0.1` for IPv6 requests because existing Moonlight clients treat the
  field as an IPv4 value. It is not a Ligase endpoint or identity source.

Ligase launch configuration and the parallel deployment script now explicitly
use `address_family=both`.

## Desktop and .NET core

Previous assumptions:

- `ApolloCoreLocator`, `ApolloDeviceService`, and `ApolloSessionService`
  concatenated `http://127.0.0.1:{port}`.
- `ApolloCoreEndpoint` contained only a base port and identity metadata.
- Port allocation tested IPv4 sockets only.

Stage I changes:

- `LigaseEndpoint` separates scheme, normalized host, port, zone, and source.
- URI construction brackets IPv6 and formats RFC 6874 zones.
- A scoped IPv6 endpoint is rejected by ordinary `BuildUri`; it must use the
  future scope-aware transport adapter, preventing .NET `Uri` from silently
  discarding the zone.
- Core location probes `::1` first and falls back to `127.0.0.1`.
- Device and session calls reuse the resolved structured endpoint.
- Port allocation tests a dual-mode IPv6 TCP and UDP bind when the OS supports
  IPv6, and falls back to IPv4 on IPv4-only systems.

## Identity, certificate, pairing, and configuration

- Host identity remains `hostUniqueId`; paired identity additionally pins the
  X.509 certificate.
- No new certificate, pairing, or attended-pairing field binds identity to an
  IP address.
- Endpoint candidates may change without creating a new Host identity.
- The frozen endpoint schema and migration rules are in
  `endpoint-contract-v2.md`.
- No system network, VPN, route, or firewall setting is changed by stage I.

## Local runtime evidence

On 2026-07-23 the isolated core was started with `address_family=both`:

```text
binary: C:\Users\dwsgap\AppData\Local\LigaseBuild\deploy\7d7a06de\sunshine.exe
PID: 64808
base port: 49989
listeners: [::]:49984, [::]:49989, [::]:49990, [::]:50010
```

Both probes returned Ligase Sync v1 and the same Host UUID:

```powershell
curl.exe --noproxy "*" "http://[::1]:49989/serverinfo?uniqueid=ligase-ipv6-probe"
curl.exe --noproxy "*" "http://127.0.0.1:49989/serverinfo?uniqueid=ligase-ipv4-regression"
```

Observed UUID:
`53BEB7EC-9788-CC23-461A-061F153029A5`.

## Remaining acceptance gaps

Stage I does not claim full IPv6 product support. The following still require
isolated follow-up acceptance:

- two real LAN devices on IPv6-only and dual-stack networks;
- global/ULA and link-local traffic with a real interface zone;
- mDNS returning multiple addresses for one Host and bounded candidate racing;
- Windows firewall rules scoped to the installed Ligase binary and selected
  profiles;
- HTTPS certificate pinning over IPv6;
- pairing and attended-pairing over IPv6;
- native RTSP, video, audio, input, cancel, and reconnect over IPv6;
- Android native transport handling of link-local socket scope;
- failure tests for unreachable IPv6 with healthy IPv4 fallback.

No global firewall rule should be created merely to make these tests pass.
