# Ligase Host desktop UI

Ligase Host is the native Windows control surface for its managed streaming
Core. It is not a WebView wrapper around Apollo's configuration site.

## Composition and ownership

`Ligase.Host.Desktop` is the composition root and Presentation host:

- `App.xaml.cs` resolves the structured installation and selected data root,
  registers Core Application/Infrastructure services, platform adapters, and
  page-scoped view models;
- `MainWindow` owns shell navigation, title-bar and window lifetime behavior,
  but not page business state;
- pages and view models consume typed services. They do not parse installation
  manifests or scripts, infer readiness from paths, or write product JSON;
- `Presentation/Onboarding/HostSetupViewModel` consumes
  `IInstallationReadinessService` and
  `IInstallationRecoveryLauncher`. The closed readiness DTO is the only owner
  of setup, artifact, display, firewall, identity/runtime, signing, and driver
  trust projections shown by the four setup cards;
- Apollo remains the runtime owner for sessions, encoders, input, audio, and
  GameStream compatibility. Desktop reaches it through narrow typed services
  and versioned routes.

Installation paths and launcher behavior are owned by
[`fresh-install.md`](fresh-install.md). Firewall scope and privileged actions
are owned by [`windows-firewall.md`](windows-firewall.md). Protocol and error
shapes remain with their route-specific canonical contracts; this UI document
does not define a universal error envelope.

## Navigation and first route

The WinUI 3 shell uses `NavigationView`, a custom title bar, semantic theme
resources, dependency injection, and page-scoped view models.

| Area | Responsibility |
| --- | --- |
| Complete Host setup | Initial typed-readiness route; four setup cards and safe recovery links |
| Overview | Host summary and guided entry points |
| Game library | Search, local view sorting, shared manual order, and launch-entry management |
| Add game | Steam discovery and confirmed non-Steam additions |
| Layout hall | Read-only installed layout catalog projection |
| Stream monitor | Core status, local preview, and explicit active-session cancellation |
| Devices | Pairing approvals, permissions, display policy, and device connection projection |
| Settings | Background lifetime, language, startup, and explicit LAN firewall action |

Every process launch initially navigates to **Complete Host setup**. The
`Setup` projection alone decides whether first-run work is required; the UI
does not infer first run from an empty directory. A completed setup can
continue to the ordinary shell without claiming that an encoder or media
session is active.

The four cards are:

1. **Core files**: Desktop, managed Core, and GameWatcher artifact readiness;
2. **Display capability**: physical-desktop streaming and optional virtual
   display are stated separately;
3. **LAN access**: exact Ligase-owned firewall readback and an explicit path to
   Settings;
4. **Host ready**: identity availability, managed Core runtime, and separately
   reported streaming capability.

Ordinary startup never invokes UAC, installs a display driver, removes a
certificate, or cleans historical firewall rules. A visible recovery action
only opens the verified installer seam or navigates to the explicit Settings
action represented by the typed snapshot.

## Shared manual order and local view sorting

The game-library toolbar separates local view sorting from shared Host order:

- Manual order is shared. After the user chooses **Start sorting**, whole cards
  can be dragged; displaced cards show the prospective order and dropping
  persists the complete published UUID sequence through the managed authority
  transaction.
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

Each page emphasizes one primary action. Error text explains what happened and
what the user can do next without exposing HTTP status codes, paths, secrets,
arguments, or exception text.

On the Add game page, scrolling the Steam result list past the header collapses
the large title and cover toolbar. A compact, keyboard-accessible row keeps
Back, Steam search focus, and an explicit Expand tools action available. The
96/24 pixel collapse/expand hysteresis prevents layout oscillation near the
boundary; the Steam search editor retains its UI Automation name and label.

Cover actions use four distinct states: selecting a candidate is preview only;
**Save cover** performs the library/Core/Sync transaction; **Use default
cover** is itself a preview until saved; and **Cancel** performs no library
mutation. Existing Steam items report success only after the same UUID and
portable identity have been read back from Core. Restoring the default clears
all five cover authority fields atomically and prunes only an unreferenced
Ligase-owned cached artifact. For a not-yet-added item, saving a preview only
prepares local artwork; the UI says that the library write occurs when Add is
confirmed.

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

The add page now exposes both drag/drop and a keyboard-accessible file picker.
Either route creates an in-memory preview only; dropping a shortcut never
writes the library. For a local executable the page also shows signed/local
`FileVersionInfo` metadata and compares the canonical target against install
directories proved by the existing Steam manifest/library parser. Exactly one
containing Steam install uses the canonical Steam add path. Zero matches uses
the ordinary local-executable path; multiple matches are never guessed and
require a second explicit fallback confirmation.

Confirmation re-resolves the Shell Link and Steam installation set and rejects
any shortcut, target, or match drift before invoking `LibraryMutationCoordinator`.
The resolver never executes the target, performs a web lookup, or infers an App
ID from display name. Duplicate detection remains canonical target plus
arguments, never display name. Cancel clears the preview and performs no
library mutation.

The Stream Monitor page provides an explicit **End stream** action. It requires
confirmation, disconnects the active session without deleting pairing or
library data, and reports that no session is active when repeated. The desktop
calls a loopback-only Ligase core route; remote callers cannot use this control.

## Independent runtime axes

These five axes must be read and displayed independently:

| Axis | Owner and meaning | What it does not prove |
| --- | --- | --- |
| Desktop lifecycle | Windows Desktop process/window/tray and single-instance activation | Core started, device connected, or stream active |
| Managed Core lifecycle | `ApolloInstanceManager` generation-bound process state | Encoder readiness, a connected device, or media traffic |
| Device state | Paired-device projection and its `connected` field | A currently active stream for that device |
| Launch/session state | Core launch/session authority, including app identity and `currentgame` projections where applicable | Active RTSP/media transport in every system-entry flow |
| Media transport | Active RTSP/session and client connection evidence | Target game process health after disconnect or quit |

`ApolloInstanceManager.IsRunning` means only that the owned Core process has
not exited. It must never be labelled “streaming.” Likewise, `connected`,
`currentgame`, or a UI preview sample cannot substitute for active
RTSP/session evidence. Real stream acceptance records the state while the
client is connected, not only the `FREE` samples before and after.

See [`managed-core-lifecycle.md`](managed-core-lifecycle.md) for Core process
ownership and bounded stop outcomes, and
[`device-access.md`](device-access.md) for device permission/session actions.

## Runtime data

Runtime data lives below the data root selected by the root bootstrap. The UI
must obtain that root from typed installation/path services; it must not assume
`%LOCALAPPDATA%`, reuse a previous instance, or derive it from the current
working directory.

Common files relative to the selected data root include:

| Relative file | Authority and contents |
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
- A single left click on the tray icon restores the one existing window. A
  Windows double-click sequence is coalesced into that same single restore;
  it never creates or activates a second window. Right click remains reserved
  for the tray menu.
- The Settings page also exposes an explicit exit action. A full exit removes
  the tray icon and stops only the Ligase-owned Apollo process.
- Optional Windows startup uses the current-user Run key with `--minimized`, so
  login startup does not display the main window.
- Runtime preferences are stored under the selected data root and never import
  state from an existing system Apollo installation.
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

## Verification matrix

Evidence is reported at the layer where it was collected:

| Layer | Required evidence | Does not establish |
| --- | --- | --- |
| Source and automated | focused/full tests, Debug/Release x64 build, diff and secret/path scans | packaged resources or installed UI |
| Packaged payload | structured tree, manifest/hash validation, XBF/PRI/runtime presence, negative fail-closed cases | installed activation or user-visible rendering |
| Installed product | root launcher from a non-install CWD, single-instance behavior, first-route cards, light/dark and narrow/wide UI, Core/FREE and exit/restart | real pairing or active media |
| Real integration | attended pairing, permission readback, active RTSP/session sampling, disconnect/quit target-process semantics | another device, network, or driver configuration not tested |

The frozen color-token production mapping has automated coverage but remains
`REAL_UI_PENDING` until a containing installer passes installed light/dark,
focus, disabled, and status-state inspection. An older installed build must not
be cited as that evidence.

## Documentation discipline

Every product change must update its affected documentation in the same commit:

- user-visible desktop behavior belongs in this document;
- Host/Android fields, routes, revisions, and fallback rules belong in
  `android-sync-contract.md`;
- installation layout/launcher/bootstrap changes belong in
  `fresh-install.md`;
- Core process lifetime changes belong in `managed-core-lifecycle.md`;
- firewall/UAC scope belongs in `windows-firewall.md`;
- route-specific protocol, security, error, permission, and session semantics
  belong in their canonical contract and are linked rather than copied here;
- real acceptance-step changes belong in the verification matrix above;
- a code change is not considered complete if the documented behavior or
  protocol no longer matches the implementation.

Every task final report and commit message declares
`DOC_IMPACT=UPDATED|NONE` with a reason. Mechanical or test-only work may use
`NONE`; changes to user behavior, UI flow, typed owners/dependencies,
installation, protocol/security/session behavior, privileged actions, or real
acceptance steps require `UPDATED` in the same commit. Planned and
`REAL_UI_PENDING` behavior is never described as implemented or passed.

## Steam discovery contract

### Cover artwork

- Adding a Steam or non-Steam application searches the public LizardByte
  GameDB index by display name and uses the first exact-prefix result by
  default.
- The add page exposes the same result set so the user can search again and
  choose a different cover before adding. Failure to search or download a
  cover never blocks adding the application.
- An existing Steam item exposes **Select cover** both from its public Manage
  menu and from the **Added** group on the add page. Both entries resolve the
  canonical library item UUID from the card membership and use the same
  existing-item transaction; the add-page preview-only selection path is
  reserved for games that are not yet in the library.
  The Host refreshes the current library item and Steam manifest, previews only
  the local verified capsule for the same App ID, and writes nothing until the
  user confirms **Use this cover**. Success reports the persisted library
  revision and content digest; a failed transaction reports that the prior
  library and Sync projections were retained rather than reusing the add-page
  in-memory “selected” message.
- Existing-item replacement preserves the canonical app UUID. Android refreshes
  the same UUID and validates the new `/appasset` bytes against the Sync cover
  digest; the exact transaction is owned by
  [`library-authority.md`](library-authority.md).
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
