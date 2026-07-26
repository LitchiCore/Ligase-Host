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

Fresh instance directories are owned by the local Administrators group, never
by the elevated credential account or the interactive operator. The installer
resolves the Host operator from its WTS interactive session and verifies that
identity against the shell token. The instance directory disables inherited
access and has exactly three explicit allow entries and no explicit deny or
additional entry: the operator has inheritable Modify access, while SYSTEM and
the local Administrators group have inheritable Full Control. It grants
neither Users nor Authenticated Users. The installer impersonates the resolved
operator for a create/atomic-move/delete probe before writing the bootstrap.
Session 0, missing or ambiguous session identity, ACL drift, or a failed write
probe aborts and removes the fresh empty instance. Existing valid bootstrap
bytes and ACLs are preserved during upgrade; the data-directory control is
locked to that existing binding. Readback reports a missing directory, an ACL
drift, and a different interactive operator as distinct typed states; the last
case is presented as “此 Host 数据属于另一 Windows 账户”.

An upgrade may offer one automatic migration only for a narrowly identified
legacy instance: the bootstrap is valid, its canonical fixed-disk path is
exactly one UUID child below the current WTS operator's LocalAppData
`Ligase Host/Instances` directory, the same operator is represented in the
source ACL, the source is enumerable and readable under that operator token,
and readback classifies the inherited legacy ACL as `aclDrift`. A wrong user,
missing or inaccessible directory, quarantine, reparse point, UNC/device/root
path, unknown location, or an already-standard ProgramData directory with ACL
drift fails closed. The migration flow never silently repairs the source ACL
or changes its identity.

The installer generates one stable target UUID for the current UI session and
shows the source, the exact
`%ProgramData%\Ligase Host\Instances\<canonical-lowercase-UUID>` target, and
the migration action before any write. Back/Next navigation does not regenerate
that target. The privileged helper is the sole migration owner. Under the
single installer gate and with Desktop, Core, GameWatcher, and launcher
processes stopped, it records the exact bootstrap bytes/hash and a bounded
source snapshot, rejects reparse points and hard links, and copies files plus
empty directories into a uniquely owned pending directory. The snapshot is an
exact relative-path, kind, size, and SHA-256 set with both entry-count and byte
limits. Required authority, library, and Sync JSON files are parsed only to
prove strict UTF-8 JSON readability; their contents and identity semantics are
never logged, interpreted, or rewritten.

The pending root receives the same Administrators-owned exact ACL as a fresh
root and must pass the WTS operator create/atomic-replace probe. A random
transaction marker proves pending/target ownership before rollback cleanup;
the marker is removed only after final target verification and is never part
of product data. Before
switching authority, the helper repeats the source exact-set, source ACL, and
bootstrap hash checks. It then atomically installs the target and bootstrap and
requires immediate `Existing` readback. Any failure restores the original
bootstrap bytes, removes only the pending/target paths created by that
transaction, and leaves the source bytes, path, and ACL unchanged. A later
firewall integration failure performs the same compensation. On success the
old source remains unchanged as explicit rollback evidence; deleting or
changing its ACL is a separate user-authorized cleanup action. The result page
states that the migration completed and shows the retained rollback-evidence
path without logging file contents or secrets.

When no bootstrap exists, the installer also checks one closed orphan-recovery
location: the current WTS operator's LocalAppData
`Ligase Host/Instances` directory. It inspects direct canonical UUID children
only and never scans other disks or guesses by name. Zero eligible children
continues as an ordinary fresh install. Exactly one fixed-local, non-reparse,
operator-readable legacy root with readable authority, library, and Sync JSON
enables `orphanLegacyRecovery`. Multiple children, unknown entries, wrong-user
or inaccessible state, reparse points, or malformed required JSON fail closed.

The Data Root page then requires an explicit choice between
`recoverOrphanLegacyDataRoot` and creating a new identity, with a warning that
the latter ignores the old data. Recovery shows the source and one
session-stable ProgramData target before confirmation. The privileged helper
uses the same bounded exact-copy, hard-link/reparse rejection, secure ACL,
operator probe, source-drift, and marker-owned rollback rules as migration, but
does not fabricate an old bootstrap. It atomically creates the bootstrap only
after the target verifies as an exact byte-preserving copy and immediately
reads back `Existing`. Failure leaves the bootstrap absent and removes only
this transaction's marked pending/target paths. Success reports
`recoveredOrphanLegacyDataRoot` and retains the source path, bytes, ACL,
identity, certificates, and library as rollback evidence.

An orphan probe is not authorization to choose either outcome. Interactive
setup keeps both choices initially clear. Silent `/S` setup requires exactly
one explicit closed `/OrphanLegacyAction=Recover` or
`/OrphanLegacyAction=CreateFresh` argument whenever an eligible orphan exists.
An absent, duplicate, malformed, or unknown action exits with machine code 18
before program files, Data Root, bootstrap, ARP, shortcuts, or firewall state
can be written. The required section independently accepts only those two
decisions and never infers `CreateFresh` from an empty value.

The native argv resolver is the single owner of this choice. Its closed result
distinguishes an interactive `proposal` from `confirmedRecover` and
`confirmedCreateFresh`; NSIS consumes that projection and does not parse a
parallel action value. Thus an explicit silent action reaches the same product
branch as the corresponding interactive choice, while a proposal alone never
authorizes either branch. A radio selection only constructs candidate argv:
the page and the required section each replace their local candidate with the
resolver's confirmed projection and require an exact match before any product
write.

Every installer invocation owns one persistent typed outcome at
`%ProgramData%\Ligase Host\Installer\last-outcome.json`. The evidence directory
uses the same protected Administrators-owned ACL shape as an instance root:
SYSTEM and Administrators have Full Control, and the WTS interactive Host
operator has Modify; broad local-user groups and explicit deny or additional
entries are not accepted. The file is atomically replaced and contains only a
closed schema: candidate source commit, phase, stable result code, safe
volume/leaf/hash projections for program and data paths, DataRoot action,
helper exit code, rollback state, firewall readback state, bounded residue
states, and an UTC timestamp. It never contains exception text, stack traces,
command lines, raw bootstrap or authority documents, certificates, tokens, or
other secrets. Initialization, confirmation, cancellation, integration
failure, final readback, and success each update this same evidence.

Installed shortcuts are machine-scoped. Start Menu and the optional Desktop
shortcut use the Windows all-users shell folders and the stable root launcher;
target, empty arguments, and the installation-root working directory are all
part of the ownership check. Upgrade and repair remove a legacy current-user
shortcut only when all three values match the Ligase-owned shape. A file at
the same path with any other target, arguments, or working directory is
preserved and treated as a typed conflict rather than overwritten or deleted.
Selecting the Desktop component creates the exact all-users shortcut; clearing
it removes only that exact owned shortcut. Uninstall applies the same ownership
test and never deletes an unknown `.lnk`.

Before changing any shortcut, the installer resolves and inspects all four
all-users/current-user Start Menu and Desktop locations. A conflict therefore
fails with zero shortcut mutation. The helper keeps a bounded, secured
transaction journal containing the exact pre-install shortcut byte set.
Shortcut writes are read back immediately; a later integration or final
readback failure restores that exact set. A restore conflict is reported as a
failed rollback with residue instead of overwriting an unknown file. Shortcut
preflight runs before the Ligase-owned firewall apply. If any later step fails,
rules created by this install transaction are removed and the shortcut journal
is restored; pre-existing exact rules and all legacy or broad rules remain
untouched.

The cross-process transaction journal is not public evidence and is never a
path authority. It lives under the physically separate machine root
`%ProgramData%\Ligase Host Admin\Transactions`; it never shares a writable
parent with `last-outcome.json`. Every created segment and the journal are
owned by Administrators, have protected DACLs, and grant FullControl only to
SYSTEM and Administrators; the interactive operator has no ACE. A manifest-
pinned, self-contained x64 transaction helper owns all journal I/O. It opens
every segment with `OPEN_REPARSE_POINT`, keeps directory/file handles while
checking final canonical path and volume/file identity, rejects reparse
points, and performs bounded same-directory atomic replacement. Load verifies
identity and ACL before and after reading, before any shortcut restore or
firewall compensation. The PowerShell integration helper never falls back to
direct journal path I/O.

The finalizer strictly rejects
missing, duplicate, unknown, stale, oversized, or malformed fields, binds the
journal to the current manifest hash/source identity and a CSPRNG correlation
identifier, and recomputes the launcher and all four shortcut
paths from the verified installation root and WTS interactive-user shell
folders. Serialized paths are consistency assertions only. Transaction IDs,
shortcut bytes, and raw paths are never copied into `last-outcome.json` or
logs. The identifier is correlation plus a two-hour stale gate; it is not
claimed as an independently bound anti-replay secret.

The install UI treats helper success as provisional. After all selected
sections run, the installed helper must independently read back manifest-owned
program assets, the bootstrap and secure DataRoot, ARP registration, the exact
Start Menu and selected Desktop shortcut state, selected virtual-display
outcome, and both owned firewall rules. Only that final exact readback permits
the Chinese success result and ordinary Finish page. Helper failure, migration
failure, contradictory output, selected-component failure, or final readback
failure instead terminates the shared GUI/silent final section with a Chinese
failure state on the install-progress page, records a non-success machine
outcome, and exits with a nonzero native process code without showing the
normal success Result or Finish page. Silent `/S` uses the same final section;
it cannot skip final readback or convert a failed typed outcome into process
exit code zero. Rollback is
reported as `notRequired`, `completed`, or `failed`; rollback failure is never
discarded. Any install-root or DataRoot residue that cannot be safely removed
is classified in the evidence rather than being presented as a successful
empty installation.

Final-readback evidence also records a closed `failedField` and component
states for `artifacts`, `bootstrap`, `dataRoot`, `arp`, `startMenu`, `desktop`,
`firewall`, and `virtualDisplay`. Components already verified remain marked
`verified`; unselected optional display support is `notSelected`; the first
failed component is `failed`; later components remain `pending`. The catch
path must not label an earlier shortcut or ARP failure as a firewall failure,
and it never serializes the underlying exception.

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
