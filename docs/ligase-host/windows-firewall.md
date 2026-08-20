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

Apply and Readback accept `configured` only when both v1 rules match and every
other owned v0/v1 name is absent. Remove performs a fresh exact-name readback;
if any owned rule remains it restores the pre-operation owned snapshots and
fails. Repeating Apply or Remove therefore converges on the same exact state,
while rules outside the manifest remain untouched.

## Attended-pairing correlation and physical toast check

The manifest offsets are mechanically correlated in
`WindowsFirewallTests.ManifestPortsMatchProductionListenersAndAttendedPublicRoute`
with `nvhttp::PORT_HTTPS`, `nvhttp::PORT_HTTP`,
`rtsp_stream::RTSP_SETUP_PORT`, and the three stream UDP constants. The same
test requires the public attended-pairing POST route and advertised capability
to be registered on the GameStream HTTP listener at `B`. This makes the P2
firewall row an implemented installer capability; the older roadmap statement
that automatic firewall rules had not started is stale. A real elevated
Apply/Remove remains an installer acceptance boundary, not a unit-test claim.

For a physical Windows notification click, run an already isolated Host/Core
instance and then invoke the repository client without administrator rights:

```powershell
python scripts/ligase/test-attended-pairing-synthetic.py `
  --base http://127.0.0.1:48989 `
  --mode ui-toast-reject
```

Minimize or background the Host before the request becomes ready. If the banner
is not visible, press **Win+N**, locate the Ligase notification whose displayed
safety code matches the runner's `WAITING_FOR_TOAST_REJECT` generation, and
click that exact notification. Confirm that the existing Host window is activated and
navigates to that request, verify the displayed safety code against the client
terminal, then choose **Reject**. The client accepts only the matching rejected
terminal and verifies the request left the live list. If no decision arrives
within 90 seconds, it sends the authenticated one-time cancel and verifies the
cancelled terminal before emitting one structured `FAIL` JSON object with the
same request ID, `terminalState=cancelled`, and `liveRequestRemoved=true`. Keys,
request tokens, certificate
bytes, and the safety code are memory-only and are never written to disk. This
check does not emulate a physical notification click; the frontend must record
that click separately.

Before a fresh physical generation, read
`<DataRoot>/diagnostics/pairing-notification-v1.json`. The registration state
must be `registered`, setting `Enabled`, and the prior request must either be
withdrawn with readback or absent. During the generation, the evidence must
move to `shownReadback` with the same request correlation hash; after the click,
`activationReceived=true` and `activationRequestMatch=true`. A stale activation,
disabled setting, show failure, missing Notification Center readback, or
unproven withdrawal blocks physical credit.

The legacy installer batch entrypoints now delegate to this script for base
port `48989`. A deployment using another base port must pass that port
explicitly so upgrading replaces the same stable names instead of accumulating
per-build program rules.

## Current verification boundary

Readback and dry-run are safe for a standard user. Actual Apply/Remove is only
accepted when an elevated installer/test window performs it and readback
matches the program, profile, remote scope, protocol, and ports. Unit tests or
a disabled firewall do not satisfy that release gate.
