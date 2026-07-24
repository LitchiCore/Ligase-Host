# Ligase Host desktop UI

## Shared manual order and local view sorting

The game-library toolbar separates local view sorting from shared Host order:

- Manual order is shared, adjusted with per-game up/down controls, and persisted
  through the managed authority transaction.
- Name A-Z/Z-A, added newest/oldest, and last played are deterministic local
  view choices. They do not change the library revision or Sync.

Manual movement uses `LibraryMutationCoordinator`, reloads the managed core,
and requires core readback of revision, sort mode, and the exact UUID sequence.
Every published UUID participates, including `desktop` and `virtualDesktop`;
the system entries can be moved but still cannot be hidden or deleted. Newly
added or republished items append; hidden items leave the public sequence
without leaking Host-only metadata. All identities remain unchanged. Operate
clients may submit the same complete manual UUID sequence with optimistic
`baseRevision`; `409` requires a fresh pull and is never automatically replayed.

Ligase Host is a native Windows control surface for Apollo's C++ streaming core.
It is not a WebView wrapper around Apollo's existing configuration site.

## First-use product path

The default experience exposes only four user goals: start the Host, add a
game, see a device, and start streaming. Port allocation, certificates,
application manifests, Sync revisions, encoder probing, and resolution
inheritance are automatic and absent from first use. Diagnostics use
progressive disclosure under advanced settings.

Each page emphasizes one primary action. Error text must explain both what
happened and what the user can do now, and should offer retry, re-pair, or
automatic repair instead of exposing HTTP status codes or exception text.

## Local Windows shortcut preview boundary

The first shortcut-import boundary accepts only local `.lnk` files that the
user explicitly drops into Ligase Host. `WindowsShortcutResolver` reads each
Shell Link through the Windows COM API and never starts or resolves the target
by execution. Its typed preview returns:

- `executable` for an existing local `.exe`, with normalized target, arguments,
  working directory, optional existing icon source, and a SHA-256 duplicate key
  over canonical target plus arguments;
- `steamShortcut` with `SteamAppId` for `steam.exe -applaunch <id>` and
  `steam://run|rungameid/<id>`, so UI can direct the user to the Steam flow;
- `unsupported`, `risk`, or `invalid` plus a stable machine code for every
  fail-closed result.

Machine codes are `none`, `notShortcut`, `shortcutNotFound`,
`damagedShortcut`, `networkLocation`, `targetMissing`, `targetIsDirectory`,
`urlTarget`, `uwpTarget`, `scriptTarget`, `commandShellTarget`,
`installerTarget`, `unsupportedTarget`, and `invalidSteamShortcut`. Network
targets, URL/UWP entries, folders, scripts, command shells, installers, missing
targets, and damaged links are never silently added.

Arguments may contain private material. They remain only in the in-memory typed
preview needed for a later confirmed add; preview `ToString`, logs, errors, and
documentation must not include their complete value. The duplicate identity is
a digest, not a printable target/argument concatenation.

This service does not add applications. A later UI integration must show a
preview and require explicit confirmation, then use
`LibraryMutationCoordinator` under managed authority. Duplicate detection is
based on canonical target plus arguments, never display name.

## Product boundary

- `Ligase.Host.Desktop` owns setup, discovery, day-to-day host status, devices,
  games, and guided configuration.
- Apollo remains the streaming engine and the source of truth for active
  sessions, encoders, input, audio, and GameStream compatibility.
- The desktop process communicates with Apollo through narrow, versioned
  Ligase routes. It must not edit `apps.json` concurrently with Apollo.
- Artemis/TouchKit remains a separate Android client. Host/client capabilities
  are synchronized through the paired GameStream HTTPS channel; this repository
  does not modify the Android project.
- Ligase Host does not locate or launch an existing system Apollo service. A
  Ligase-owned core must be bundled beside the desktop executable or built in
  this repository.

## Navigation

The first shell uses WinUI 3 `NavigationView`, a custom title bar, theme resource
dictionaries, dependency injection, and page-scoped view models.

| Area | Responsibility |
| --- | --- |
| Overview | Service health, streaming readiness, guided fixes |
| Game library | Discover, search, import, and maintain launch entries |
| Devices | Paired devices, attended pairing approvals, permissions, display policy, and connection state |
| Settings | Background lifetime, Windows startup, and display language |

The current vertical slice includes Steam discovery, non-Steam applications,
persistent library sorting, physical/virtual desktop entries, per-application
resolution settings, an isolated Apollo process, device access management, and
a low-frame-rate desktop monitor.

The Stream Monitor page provides an explicit **End stream** action. It requires
confirmation, disconnects the active session without deleting pairing or
library data, and reports that no session is active when repeated. The desktop
calls a loopback-only Ligase core route; remote callers cannot use this control.

## Runtime data

All Ligase-owned files live below `%LOCALAPPDATA%\Ligase Host`:

| File | Authority and contents |
| --- | --- |
| `library.json` | Host-owned application collection, sort mode, timestamps, and revision |
| `streaming.json` | Global resolution and UUID-keyed application overrides |
| `ligase-sync.json` | Sanitized Android snapshot; excludes paths, commands, and working directories |
| `preferences.json` | Desktop lifetime, startup, and language preferences |
| `apollo/apps.json` | Generated Apollo launch entries |
| `apollo/state.json` | Apollo identity and paired-client state |

`streaming.json` defaults to 1920×1080. An application inherits that global
resolution unless its UUID has an entry under `apps`. Clearing the application
entry restores inheritance. Both streaming and library documents use monotonic
revision numbers for optimistic concurrency.

Apollo advertises `LigaseSyncVersion` and `LigaseSyncPath` in `serverinfo`.
Paired Android clients use the GameStream HTTPS port for the versioned sync
API. The desktop Devices page uses the loopback-only
`GET /ligase/v1/devices` endpoint on the Ligase HTTP base port. See
[`android-sync-contract.md`](android-sync-contract.md) for the wire format.
The sync response also reports Apollo's runtime HDR encoding capability.
Clients combine it with their own decoder/display capability; Ligase does not
guess whether an individual game actually emits HDR content.

Sync v1 is a required Ligase product capability. Clients must present a Host
without it as incompatible instead of falling back to an applist-derived
library. GameStream pair, discovery, launch, and media fields remain a transport
ABI only; they are not compatibility promises for Apollo's configuration or
Web UI product model.

## Devices page

- Reads paired clients from Apollo instead of maintaining a second device
  database.
- Uses the UI-managed core when it is running. During development or recovery,
  if that core is unavailable, it probes only locally listening Ligase port
  families and attaches when exactly one Sync-v1-capable core is present. It
  never guesses between multiple cores or treats an ordinary Apollo instance as
  authoritative.
- Shows pending Android pairing requests with device identity, a short safety
  fingerprint, expiry, and explicit Allow/Reject actions. The normal user path
  never displays or asks for a PIN.
- The pending projection is read at most once per second from the managed
  core's loopback-only attended-pairing endpoint. Process identity, base port,
  Host UUID, and managed `startNonce` must all remain stable; a core switch
  clears pending cards and notification deduplication state instead of letting
  an old request act on a new instance.
- A request that has not yet bound its encrypted envelope and held
  `getservercert` operation displays “establishing secure connection.” Allow is
  disabled while Reject remains available. Once ready, the card displays the
  same `XXXX-XXXX` safety code as Android and a natural-language source class;
  it never exposes the request token, certificate fingerprint, JSON, IP in a
  notification, or the internal legacy PIN.
- When the window is active, one queued in-app prompt offers to open the
  Devices page; it never approves from the prompt. When the window is hidden,
  inactive, or minimized, an unpackaged Windows App SDK notification contains
  only the device name, safety code, and an instruction to open Ligase Host.
  Notifications are deduplicated by managed-instance key plus request ID,
  removed at terminal state/core switch, and stale activation can only display
  an expired-request message.
- Notification activation and ordinary second launches are redirected through
  the Windows App SDK single-instance activation channel. The existing window
  is restored; the managed-instance key and request ID remain attached to the
  activation instead of being replaced by a generic “show window” signal.
- After Allow, the page continues reading the pending projection and refreshes
  Apollo's dynamic paired-device projection when the request leaves the live
  list. No Host restart is required.
- New requests default to **Operate**. Each pending card has an unchecked
  **Observe only** option; the UI must persist that pending selection through
  the loopback access route before it calls the frozen empty-body Allow route.
- Paired device cards show **Operate** or **Observe only** and expose a
  management dialog. Permission changes and device deletion are core-owned
  loopback operations, not direct edits of `state.json`.
- Device deletion names the device and explains that pairing must be repeated.
  An active device is not silently disconnected: the first delete returns a
  conflict and the UI separately offers **End stream and delete**.
- Displays name, stable device UUID, connected/paired state, display policy,
  permission mask, and whether client commands are allowed.
- The local endpoint rejects non-loopback callers.
- Empty, core-not-running, interface-unavailable, and read-failure states remain
  distinct and provide a retry or corrective next step.
- Disconnecting or unpairing a device requires a separate destructive-action
  confirmation flow.

## Library ownership and visibility

- Steam/non-Steam discovery, add, remove, and collection changes exist only in
  Host.
- Every non-system item has a Host-owned “Show on clients” publication state.
  Turning it off hides the UUID from normal client views without deleting it.
- Android may additionally hide an item only on that device. Host-global hiding
  wins and cannot be restored by Android.
- Desktop and virtual desktop are non-hideable system recovery entries in the
  first implementation.
- Delete is unavailable for system entries. Deleting any other item names the
  item in a confirmation and explains that it disappears from every client.
- Library mutations pass through `ApplicationLibrary`; pages and view models
  never write product JSON or Apollo manifests directly.

See [`multi-client-product-contract.md`](multi-client-product-contract.md) for
the frozen cross-client semantics, pairing protocol, and shared color tokens.

## Background lifetime

- Closing the main window hides it to the Windows notification area by default.
  The Ligase-owned Apollo process keeps running so clients can still connect.
- Launching Ligase Host again activates the existing window instead of starting
  a second UI or Apollo instance.
- The tray menu can show the window, start or stop the Ligase streaming core,
  or exit the product.
- The Settings page also exposes an explicit exit action. A full exit removes
  the tray icon and stops only the Ligase-owned Apollo process.
- Optional Windows startup uses the current-user Run key with `--minimized`, so
  login startup does not display the main window.
- Runtime preferences are stored under `%LOCALAPPDATA%\Ligase Host` and never
  share state or ports with an existing Apollo installation.
- The display language supports system default, Simplified Chinese, and
  English. Language changes are persisted and applied on the next launch.
- Settings includes **Send Windows test notification**. It verifies the same
  unpackaged App SDK notification registration and activation channel used for
  attended pairing. The notification contains no request authority; clicking
  it only restores the existing Host window and navigates to Devices.

## Build and probe

The desktop project requires the .NET 8 SDK and Windows build tools:

```powershell
dotnet build src/Ligase.Desktop/Ligase.Host.Desktop.csproj -c Debug -p:Platform=x64
dotnet test tests/Ligase.Host.Desktop.Tests/Ligase.Host.Desktop.Tests.csproj
dotnet run --project tools/Ligase.SteamProbe/Ligase.SteamProbe.csproj
```

The probe is read-only and prints the discovered library as JSON. It is useful
for diagnosing Steam discovery without starting the desktop UI.

## Documentation discipline

Every product change must update its affected documentation in the same commit:

- user-visible desktop behavior belongs in this document;
- Host/Android fields, routes, revisions, and fallback rules belong in
  `android-sync-contract.md`;
- build or verification changes belong in the build section above;
- a code change is not considered complete if the documented behavior or
  protocol no longer matches the implementation.

## Steam discovery contract

### Cover artwork

- Adding a Steam or non-Steam application searches the public LizardByte
  GameDB index by display name and uses the first exact-prefix result by
  default.
- The add page exposes the same result set so the user can search again and
  choose a different cover before adding. Failure to search or download a
  cover never blocks adding the application.
- Downloaded artwork is restricted to HTTPS images from `images.igdb.com`,
  validated as PNG, size-limited, and stored under the managed Ligase data
  root. The canonical path is written to Apollo `image-path`, so GameStream
  `appasset` clients receive the same artwork.
- Desktop and virtual-desktop use separate built-in Ligase artwork. Desktop
  depicts a mirrored physical display; virtual desktop depicts an extended
  display created for streaming.

The current Windows implementation:

1. Locates Steam from per-user/machine registry keys, then the conventional
   Program Files path.
2. Parses both current object-style and legacy string-style
   `steamapps/libraryfolders.vdf` entries.
3. Reads `appmanifest_*.acf` from every library.
4. De-duplicates by Steam App ID and exposes name, install path, size, and
   `steam://rungameid/<appid>`.
5. Excludes known non-launchable Steam runtime components.
6. Skips an inaccessible or transiently corrupt manifest without discarding
   healthy libraries.

Steam and non-Steam additions are Host-owned. Android receives their sanitized
metadata through the sync snapshot and does not submit Windows paths or
commands.
