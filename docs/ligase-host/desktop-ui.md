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
| Settings | Encoder, display, network, audio, input, maintenance |

Only game discovery is functional in the first vertical slice. Disabled UI is
intentional where an Apollo write API does not yet exist.

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
