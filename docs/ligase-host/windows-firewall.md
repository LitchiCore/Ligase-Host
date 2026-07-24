# Windows firewall boundary

Ligase Host owns two inbound Windows Defender Firewall rules for its managed
`sunshine.exe`. The rules are an installer/deployment boundary, not an
automatic action performed during ordinary Desktop startup.

## Port matrix

For HTTP base port `B`, LAN access uses only:

| Transport | Ports | Purpose |
| --- | --- | --- |
| TCP | `B-5`, `B`, `B+21` | paired HTTPS, GameStream HTTP and attended enrollment, RTSP |
| UDP | `B+9`, `B+10`, `B+11` | video, control/input, audio |

The configuration/admin Web endpoint at `B+1` is not opened. Loopback
authority, pairing approval, device permission, and mutation routes remain
loopback-only even though they share a process.

Every rule is inbound allow, `Profile=Private`,
`RemoteAddress=LocalSubnet`, and bound to the exact managed
`sunshine.exe`. No Public-profile, any-remote, any-program, all-port, firewall
disable, or edge-traversal rule is permitted. `LocalSubnet` applies to the
active Private network's IPv4 and IPv6 local subnets.

## Manifest and operations

`src_assets/windows/misc/firewall/ligase-firewall-v1.json` is the
machine-readable port/scope manifest. Stable owned rule names begin with
`Ligase.Host.Lan.`. Apply and remove enumerate only the manifest's exact
owned names; they never delete historical `Apollo`, Sunshine, user, or
Windows-generated rules.

`Manage-LigaseFirewall.ps1` supports:

- `DryRun`: validate inputs without reading or writing rules;
- `Readback`: compare installed rules with the exact desired plan;
- `Apply`: replace the owned v0/v1 rules, add v1 rules, and read back;
- `Remove`: remove only the owned names.

Apply and Remove require an already-elevated process. The script never invokes
`RunAs`, changes global firewall state, or produces an implicit UAC prompt.
An installer or a future explicit Desktop “配置局域网访问” action may invoke it
once through a controlled elevated helper. Cancellation or denial returns
`requiresElevation`; Host remains usable locally, while LAN readiness must be
shown as unavailable rather than silently weakening the rules.

The legacy installer batch entrypoints now delegate to this script for base
port `48989`. A deployment using another base port must pass that port
explicitly so upgrading replaces the same stable names instead of accumulating
per-build program rules.

## Current verification boundary

Readback and dry-run are safe for a standard user. Actual Apply/Remove is only
accepted when an elevated installer/test window performs it and readback
matches the program, profile, remote scope, protocol, and ports. Unit tests or
a disabled firewall do not satisfy that release gate.
