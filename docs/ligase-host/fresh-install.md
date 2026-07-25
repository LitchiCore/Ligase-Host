# Ligase Host fresh installation boundary

The Windows release artifact is built by
`packaging/windows/ligase/Build-LigaseInstaller.ps1`. It accepts one explicit
C++ build root and builds the native root launcher, Desktop, managed Core, and GameWatcher from the same
Git HEAD, configuration, and x64 platform. The staging manifest records the
relative path, byte length, and SHA-256 of all four required executables.
Missing or mismatched artifacts fail closed.

The Ligase installer is independent of the legacy Apollo CPack installer.
It never calls Apollo's migration script and never imports an existing Apollo
configuration, certificate, or state file. A fresh install creates one root
bootstrap from the confirmed Data Root selection. Fresh installs propose one
machine-scoped
`%ProgramData%\Ligase Host\Instances\<canonical-lowercase-UUID>` path; the
proposal is generated once without creating the directory and remains stable
across Back/Next navigation. Ordinary users do not need to choose a path.
The advanced option accepts an explicit `/DataRoot=`-equivalent local path
through the same canonical validator. An
upgrade with a valid, accessible bootstrap preserves its bytes and data-root
binding exactly. Malformed or inaccessible existing bootstrap state fails
closed and is never silently replaced. Desktop creates the Host UUID,
certificate, library, and managed configuration only when that selected data
root has no identity.

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
- `Ligase Host.exe` is the native root launcher. It validates the structured
  manifest and Desktop hash, forwards arguments without changing the child
  working directory, and contains no product state or secrets;
- `ligase-install-manifest.json`, `ligase-bootstrap.json`, and
  `Uninstall.exe` remain at the installation root.

The Start Menu shortcut points to
`Ligase Host.exe`; the real WinUI application remains
`Desktop/Ligase.Host.Desktop.exe`. Every required executable is addressed by
its manifest `relativePath` and verified by hash. Upgrading a legacy flat
installation removes only exact paths listed by the new manifest's
`legacyFlatOwnedEntries`; unknown files and all user data are retained.

The legacy flat list is upgrade-cleanup input only; it is not a supported
runtime layout or fallback search path. A missing structured payload fails
closed. Product launch must not be rescued with repository/Debug binaries,
hand-copied dependencies, or a child working-directory change.

The Components page contains a selected-by-default **Create desktop shortcut**
option. A user may clear it before installation. Install and upgrade remove
only the exact Ligase-owned desktop shortcut before applying the current
selection; uninstall removes that same exact shortcut. The Start Menu shortcut
remains required and is independent of this option.

Interactive installation is the preferred operator path: select the structured
program directory, review the proposed machine data directory, and use the
advanced custom-directory control only when needed. The confirmation page
shows the final program path, data path, desktop-shortcut selection, virtual
display selection, and exact Ligase-owned firewall action before any product
or data write. Automation must invoke
`Deployment/Invoke-LigaseInstaller.ps1`, which uses
one tested Windows argv serializer and passes one serialized argument string
to `Start-Process`; callers must not concatenate or pre-quote a native command
line. The installer is still the final authority: it parses the
original native argv once, rejects duplicate, malformed, unknown, split, UNC,
device, root, relative, or non-canonical path arguments, and validates the
final UI values before the first program, bootstrap, or firewall write.
An empty or unconfirmed value never falls back to LocalAppData.

Fresh instance directories are owned by the WTS interactive-session operator,
not by the elevated credential account. The installer resolves that identity
from the current interactive session and verifies it against the shell token.
The instance directory disables inherited access and has exactly three
allow entries: the operator has inheritable Modify access, while SYSTEM and
the local Administrators group have inheritable Full Control. It grants
neither Users nor Authenticated Users and adds no deny entry. The installer
impersonates the resolved operator for a create/atomic-move/delete probe before
writing the bootstrap. Session 0, missing or ambiguous session identity, ACL
drift, or a failed write probe aborts and removes the fresh empty instance.
Existing valid bootstrap bytes and ACLs are preserved during upgrade; insecure
legacy ACL repair requires a separate explicit action.

Desktop composition, first-route behavior, and installed-product acceptance are
documented in [`desktop-ui.md`](desktop-ui.md). That document links here rather
than duplicating manifest, script, cleanup, or privileged-action rules.

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
Apply is followed by an independent exact readback. Both owned TCP and UDP
rules must match program, ports, Private profile, LocalSubnet, inbound allow,
and blocked edge traversal before the integration outcome can be successful.
Bootstrap or firewall mismatch aborts the installer and prevents its completion
page from claiming success. The result page repeats the exact data path and
the verified firewall state.

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
