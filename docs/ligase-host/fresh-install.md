# Ligase Host fresh installation boundary

The Windows release artifact is built by
`packaging/windows/ligase/Build-LigaseInstaller.ps1`. It accepts one explicit
C++ build root and builds Desktop, managed Core, and GameWatcher from the same
Git HEAD, configuration, and x64 platform. The staging manifest records the
relative path, byte length, and SHA-256 of all three required executables.
Missing or mismatched artifacts fail closed.

The Ligase installer is independent of the legacy Apollo CPack installer.
It never calls Apollo's migration script and never imports an existing Apollo
configuration, certificate, or state file. A fresh install creates one root
bootstrap from the explicit `/DataRoot=` selection, or from the documented
per-user default when no selection was supplied. An upgrade with a valid,
accessible bootstrap preserves its bytes and data-root binding exactly.
Malformed or inaccessible existing bootstrap state fails closed and is never
silently replaced. Desktop creates the Host UUID, certificate, library, and
managed configuration only when that selected data root has no identity.

## Structured installation layout

`installLayout: "structured-v1"` has one typed path authority:
`InstallationLayoutResolver`. Product code resolves it from the Desktop
installation entry and never depends on the current working directory or
scattered parent-directory guesses. The installation root contains only
top-level state/installer entries and these owned trees:

- `Desktop/` contains the desktop executable and all private .NET, WinUI,
  runtime, native, and locale dependencies;
- `Core/` contains the managed `sunshine.exe` and its assets;
- `Tools/GameWatcher/` contains the self-contained watcher;
- `Deployment/Firewall/` and `Deployment/Drivers/` contain privileged
  deployment assets, while `Deployment/Manage-LigaseInstallation.ps1` is the
  action seam;
- `ligase-install-manifest.json`, `ligase-bootstrap.json`, and
  `Uninstall.exe` remain at the installation root.

The Start Menu shortcut points to
`Desktop/Ligase.Host.Desktop.exe`. Every required executable is addressed by
its manifest `relativePath` and verified by hash. Upgrading a legacy flat
installation removes only exact paths listed by the new manifest's
`legacyFlatOwnedEntries`; unknown files and all user data are retained.

The managed Core and GameWatcher are required. SudoVDA is optional: without it,
physical-desktop streaming remains available while virtual-display-only
features must be disabled with `virtualDisplayNotInstalled`. The readiness
readback distinguishes `available`, `notInstalled`, `rebootRequired`, and
`failed`. Encoder availability is a runtime Core capability and must be checked
before claiming that streaming is ready; the installer does not infer it from
the GPU vendor or driver name.

Builds are explicitly `UnsignedDev` or `PublicRelease`. An unsigned development
installer is named `UNSIGNED-DEV`, reports `nonRelease`, and must never be
uploaded as a public release. Public builds require an external signer command
that receives only the artifact path; certificates and private keys never
enter the repository, command line, logs, manifest, or staging directory.
Desktop, Core, GameWatcher, installer, and generated uninstaller must have a
valid Authenticode chain, an RFC 3161 timestamp, and one allowlisted publisher.
The manifest records unsigned-content and final signed hashes plus signer
subject/thumbprint/timestamp state. SudoVDA is not re-signed; its catalog's
existing signature, publisher, hash, and trust status are recorded and a bad
catalog blocks `PublicRelease`. The current repository has no release
certificate. SignPath Foundation is the preferred CI signer for this open
source project; an OV certificate or Microsoft Artifact Signing are fallback
options.

The bundled SudoVDA catalog currently uses a self-signed
`CN=sudovda@su.mk` certificate. A `Valid` result on a machine where that
certificate was previously inserted into Root/TrustedPublisher is classified
as `locallyTrustedSelfSigned`, never `caTrusted`. The manifest records catalog
hash, certificate thumbprint, subject, issuer, self-signed flag, timestamp
state, and trust class. `PublicRelease` rejects this dependency until a
publicly trusted, timestamped driver package is available. Development install
requires an explicit warning before trusting it. Historical copies in
LocalMachine or CurrentUser stores are read back as residue; they are never
silently removed. Certificate removal is permitted only for stores recorded
as owned by that installer execution and after confirming that no dependent
driver remains.

Firewall installation uses only the Ligase-owned manifest and
`Manage-LigaseFirewall.ps1`: Private profile, LocalSubnet, the managed
`sunshine.exe`, and the exact product port family. It never disables Windows
Firewall and never removes non-owned Apollo/Sunshine rules. Historical broad
rules remain a separately disclosed, opt-in cleanup decision.

`Manage-LigaseInstallation.ps1` is the typed action seam used by NSIS and
automation. `DryRun` and `Readback` are ordinary-user, zero-mutation actions.
`Install` and `Uninstall` are serialized by a global mutex and are invoked only
inside the explicit elevated installer. Machine output contains codes and
capability states, not install paths, data paths, certificates, arguments, or
exception text.

Uninstall always removes owned program files and Ligase-owned firewall rules.
Personal data is preserved by default. The explicit clean-reset choice moves
the active instance root to a recoverable quarantine rather than deleting
identity, pairing, library, covers, layouts, preferences, stream settings, or
logs in place. A later fresh reinstall receives a new root and identity;
ordinary repair/upgrade preserves a valid existing bootstrap.
