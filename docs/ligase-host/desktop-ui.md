# Ligase Host desktop UI

Ligase Host is a native Windows control surface for Apollo's C++ streaming core.
It is not a WebView wrapper around Apollo's existing configuration site.

## Product boundary

- `Ligase.Host.Desktop` owns setup, discovery, day-to-day host status, devices,
  games, and guided configuration.
- Apollo remains the streaming engine and the source of truth for active
  sessions, encoders, input, audio, and GameStream compatibility.
- The desktop process will communicate with Apollo through a narrow,
  versioned local API. It must not edit `apps.json` concurrently with Apollo.
- Artemis/TouchKit remains a separate Android client. Host/client capabilities
  will be negotiated later; this slice does not modify the Android project.

## Navigation

The first shell uses WinUI 3 `NavigationView`, a custom title bar, theme resource
dictionaries, dependency injection, and page-scoped view models.

| Area | Responsibility |
| --- | --- |
| Overview | Service health, streaming readiness, guided fixes |
| Game library | Discover, search, import, and maintain launch entries |
| Devices | Pairing, permissions, capabilities, last-seen state |
| Settings | Background lifetime, Windows startup, and later host configuration |

The current vertical slice includes Steam discovery, non-Steam applications,
persistent library sorting, physical/virtual desktop entries, an isolated
Apollo process, and a low-frame-rate desktop monitor. Placeholder areas remain
intentional where a stable Apollo control API does not yet exist.

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

## Build and probe

The desktop project requires the .NET 8 SDK and Windows build tools:

```powershell
dotnet build src/Ligase.Desktop/Ligase.Host.Desktop.csproj -c Debug -p:Platform=x64
dotnet test tests/Ligase.Host.Desktop.Tests/Ligase.Host.Desktop.Tests.csproj
dotnet run --project tools/Ligase.SteamProbe/Ligase.SteamProbe.csproj
```

The probe is read-only and prints the discovered library as JSON. It is useful
for diagnosing Steam discovery without starting the desktop UI.

## Steam discovery contract

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

The next slice should add an Apollo-owned import endpoint with an idempotency
key derived from `steam:<appid>`, then enable the **Add** action.
