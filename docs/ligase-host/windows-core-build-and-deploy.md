# Ligase Host Windows core build and parallel deployment

This document defines the reproducible Windows build, isolated validation, and
rollback path for the Ligase-owned C++ core. It does not replace an installed
Apollo instance, register a service, or modify the system `PATH`.

## Pinned toolchain

- MSYS2 base: `2026-03-22`
- MSYS2 environment: UCRT64
- GCC, CMake, and Ninja: packages from that MSYS2 installation
- Node.js: official Windows x64 `v24.18.0`
- Build type: `Release`

The build script verifies the fixed SHA-256 of both downloaded archives.
Toolchains, build output, and deploy output live below
`%LOCALAPPDATA%\LigaseBuild`.

## Build command

Run this command from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\ligase\build-windows-core.ps1
```

The script downloads and verifies the toolchain, installs the documented
UCRT64 packages, creates an out-of-tree CMake build, compiles the C++ core and
Web UI, runs `cmake --install`, and reports the commit, file version, absolute
binary path, and SHA-256.

`BRANCH`, `BUILD_VERSION`, and `COMMIT` are injected explicitly. This prevents
different line-ending settings in Windows Git and MSYS2 Git from incorrectly
marking the binary as dirty.

## Parallel validation

The default validation port family uses base port `49989`. The launcher checks
the full port family first. If any required port is occupied, it stops without
terminating or replacing another instance.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\ligase\start-parallel-core.ps1 `
  -DeployDirectory "$env:LOCALAPPDATA\LigaseBuild\deploy\<commit>" `
  -BasePort 49989
```

Important ports:

- `49984`: GameStream HTTPS and Ligase Sync v1
- `49989`: GameStream HTTP
- `49990`: local configuration UI
- `50010`: RTSP

The new instance keeps its application list, certificates, pairing state, and
logs in the deploy directory's `config` subdirectory. The old Apollo install,
service, configuration, and ports are not reused.

## Runtime evidence

Keep at least the following evidence:

```powershell
netstat -ano | Select-String "<new-pid>|<old-pid>"
Get-FileHash "<deploy>\sunshine.exe" -Algorithm SHA256
curl.exe -s "http://127.0.0.1:49989/serverinfo?uniqueid=ligase-smoke"
curl.exe -s "http://127.0.0.1:49989/ligase/v1/devices"
```

The Ligase parallel configuration uses `address_family=both`. On a system with
IPv6 loopback enabled, verify both families without changing system networking:

```powershell
curl.exe --noproxy "*" -sS "http://[::1]:49989/serverinfo?uniqueid=ligase-ipv6-smoke"
curl.exe --noproxy "*" -sS "http://127.0.0.1:49989/serverinfo?uniqueid=ligase-ipv4-smoke"
Get-NetTCPConnection -State Listen -LocalPort 49989 |
  Select-Object LocalAddress, LocalPort, OwningProcess
```

Both responses must expose the same `uniqueid` and `LigaseSyncVersion=1`.
`LocalAddress=::` confirms the dual-stack listener on Windows; it does not by
itself prove LAN firewall, mDNS, or native streaming acceptance.

`serverinfo` must expose:

- `LigaseSyncVersion=1`
- `LigaseSyncPath=/ligase/v1/sync`
- `LigaseHdrEncodingSupported`
- the isolated instance's `uniqueid` and `HttpsPort`

The three Sync routes use the paired HTTPS port and require a paired client
certificate:

- `GET /ligase/v1/sync`
- `POST /ligase/v1/streaming`
- `POST /ligase/v1/library/sort`

A TLS failure without a client certificate is expected security behavior and
does not count as route acceptance.

## Rollback

The parallel instance is not a service. Rollback only stops the PID reported by
the launcher:

```powershell
Stop-Process -Id <new-pid>
```

The old Apollo process continues using its original binary and ports, so no old
files need to be restored.

## Product boundary for non-technical users

Ports, certificates, JSON files, and commands in this document are development
and diagnostic details. The default product path is only:

1. Start Ligase Host.
2. Add a game.
3. See the device.
4. Start streaming.

The desktop app must automatically choose ports, create certificates, maintain
sync files, and recommend encoder settings. Protocol and port details belong in
advanced settings. A failure message must explain what happened and what the
user can do next, with retry or automatic repair as the preferred action.
