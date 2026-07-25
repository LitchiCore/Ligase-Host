# Ligase Host library authority

Status: implemented in the Host P1 authority boundary.

## Ownership

The desktop product library has one writer: the .NET
`LibraryMutationCoordinator`. The C++ streaming core consumes the generated
`ligase-sync.json` and `apps.json` projections. The C++ core does not expose a
library mutation API.

The desktop may write only when all of the following identify the one core it
started:

- the original process handle is alive and its creation time is unchanged;
- the expected base port has exactly one Ligase core;
- loopback `serverinfo` returns the expected Host UUID;
- a per-start nonce, authority token, and normalized-root fingerprint match the
  values consumed by that core.

An external core, multiple cores, an exited core, or any identity mismatch makes
the library read-only. The UI must explain how to recover and its command
handlers must repeat the authority check.

## Explicit product data root

Managed structured deployments place `ligase-bootstrap.json` at the
installation root and resolve it through the typed `InstallationLayout`
authority; the Desktop executable lives under `Desktop/`. The bootstrap
contains `{ "dataRoot": "<absolute-directory>" }` and binds the installation
to one explicit product root without relying on the process working directory.
Automation may instead use `--data-root <absolute-directory>` or the
child-process environment variable `LIGASE_DATA_ROOT`. Command line wins over
environment, which wins over the typed bootstrap path. This is the product data
boundary used by the desktop, its managed core, and every generated projection.
A relative, missing, or duplicate command-line value is rejected.

An ordinary unpackaged first run without this option still uses
`%LOCALAPPDATA%\Ligase Host`. Deployment and acceptance automation must pass an
explicit root; it must not infer authority from the process package context or
from a redirected `LocalAppData` directory.

## Projection transaction

Collection writes are serialized. Before mutation, the coordinator snapshots
only:

- `library.json`;
- `ligase-sync.json`;
- `apollo/apps.json`.

The repository writes all projections using their existing same-directory
temporary-file replacement. Only after all files are complete does the
coordinator request a core reload. Success requires core read-back of:

- the unchanged Host UUID;
- the canonical library UUID and publication/store metadata;
- the loaded applist UUID and numeric launch ID.

Failure restores the exact three snapshots, reloads the core again, and compares
the core's restored consumption view with its pre-write view.

The same coordinator is the only desktop entry point for adding, deleting,
publishing, and sorting library items. The game library exposes deletion through
each non-system item's **Manage** action, with an explicit confirmation that the
item disappears from every client while its executable remains untouched.
Desktop and virtual-desktop system entries cannot be deleted. A successful UI
result is shown only after reload and core read-back succeed.

## Loopback core contract

The HTTP base port exposes two loopback-only `POST` routes:

- `/ligase/v1/authority/readback`
- `/ligase/v1/authority/reload`

Both require JSON `{ "token": "<per-process authority token>" }`. They reject
non-loopback callers and token mismatches. Reload also rejects an active game
session. Read-back returns the current Host UUID, start nonce, root fingerprint,
Sync library projection, and the actual `proc::proc.get_apps()` UUID/numeric-ID
view. It never returns the filesystem path and is not a second mutation API.

Run the isolated route smoke test with:

```powershell
.\scripts\ligase\test-authority-readback.ps1 -Binary <built-sunshine.exe>
```

The test uses an isolated data root and port, then stops only its own process.

## P1 cross-device evidence boundary

Android verified that the same Host UUID and pinned certificate changed from
three to four visible items after the fixture was added, using the fixture's
canonical UUID plus its numeric launch mapping. The Host then removed that
fixture through `LibraryMutationCoordinator`; library, Sync, applist, and core
read-back no longer contained the UUID, the library revision advanced to 6,
the streaming revision remained 6, and the original three entries remained.

The Android refresh after deletion was not executed because that device had no
certificate for the managed instance and the current first-run flow requires a
separately initialized management account. This is tracked as
`BLOCKED_BY_HOST_FIRST_RUN_PAIRING`; it is not represented as a successful
client-side deletion refresh and no hidden account or compatibility route was
created to bypass it.
