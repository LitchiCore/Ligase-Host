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

Before the installer changes any Start Menu or desktop shortcut, applies a
firewall rule, or commits ARP integration, the pinned transaction helper runs
an explicit `preflight`. It creates or verifies the admin-only parent chain,
performs a bounded create/write/read/delete probe with the same handle,
reparse, ACL, identity, and atomic-replace rules used by the journal, and
leaves no pending transaction. A preflight failure therefore leaves all four
shortcut locations and the firewall unchanged.

New admin-only directory segments are created with `CreateDirectoryW` and a
final `SECURITY_ATTRIBUTES` descriptor. Owner `Administrators`, protected
DACL, and the exact `SYSTEM`/`Administrators` Full Control entries therefore
exist at creation time; the helper never creates a broadly inherited
directory and hardens it later. It immediately reopens the segment without
following reparse points and verifies owner, DACL, final path, and file
identity.

The one recovery exception is an exact
`%ProgramData%\Ligase Host Admin` directory left by an earlier failed Ligase
installation. The install confirmation discloses this recovery. The helper
accepts it only when the same trusted handle proves that it is the direct
canonical directory, non-reparse, owned by `Administrators`, completely empty
(including no named alternate data streams), and has no `Transactions` child,
marker, or journal. Recovery obtains that directory with zero share mode and
the minimum list/attribute/security rights. Child entries and streams are
enumerated through that same handle before and after the ACL transition; path
enumeration is not an authority. Windows may expose a zero-length unnamed
stream name, the canonical unnamed `::$DATA` spelling, and the directory's
canonical `:$I30:$INDEX_ALLOCATION`
system stream (with the unnamed index spelling accepted where Windows returns
it);
neither is user data. Any other stream name, duplicate canonical stream, or
malformed stream record fails closed as named or untrusted data. An existing
`ERROR_HANDLE_EOF` response from the documented handle stream query is the
closed no-stream state, not a failure or an alternate stream.
Every nonterminal `NextEntryOffset` must make aligned forward progress by at
least one complete header and fit within the bytes remaining in the bounded
buffer before any offset addition occurs. Near-maximum, truncated,
out-of-range, overflow, or loop-like record chains therefore map to the closed
`inspectEmptyRootStreamMetadata/20011/streamMetadataInvalid` tuple.
or concurrent handle that
prevents exclusivity fails with `busy`/Win32 `32` before ACL mutation. It
applies the final descriptor to that same file identity, repeats the
handle-based empty/stream/owner/DACL checks, reopens and verifies it, and reports
`recoverEmptyAdminRoot`. It never deletes, renames, or loosens the residue.
Nonempty, unknown-owner, replaced, or otherwise ambiguous directories remain
unchanged and fail closed. If a post-ACL identity or empty-state check fails,
the helper attempts to restore the captured descriptor on the same identity
and records `aclMutationOccurred` plus the closed `aclRollback` result; it does
not claim zero mutation.

Transaction-helper failures expose only a closed machine code, one closed
stage, and a safe native category:
`fileNotFound`, `pathNotFound`, `accessDenied`, `invalidHandle`, `busy`,
`invalidParameter`, `privilegeNotHeld`, `invalidOwner`, `invalidAcl`,
`notSupported`, `identityChanged`, `bindingMismatch`, `managedFailure`, or
`unknown`. Named native categories use
the corresponding allowlisted Win32 values (`2`, `3`, `5`, `6`, `32`, `87`,
`1314`, `1307`, `1336`, `50`);
ACL inspection uses closed managed codes `20001` through `20007`; empty-root
owner/child/stream metadata inspection uses `20008` through `20011`; the
separate empty-root stream query uses `20015` only when a failed query has no
native code. Other positive 16-bit Win32 values retain their numeric code with
category `unknown`. Zero or out-of-range values are not accepted as a native
failure.
Stages are `resolveProgramData`, `rejectReparse`, `createSegment`, `openHandle`,
`verifyIdentity`, `resolveFinalPath`, `canonicalRoot`, `inspectAcl`,
`readSecurityDescriptor`, `descriptorLength`, `descriptorCopy`,
`descriptorParse`, `buildSecurityDescriptor`, `compareSecurityDescriptor`,
`inspectEmptyRootOwner`, `inspectEmptyRootChildren`,
`queryEmptyRootStreams`, `inspectEmptyRootStreams`,
`inspectEmptyRootStreamMetadata`,
`applyAcl`, `assertAcl`, `createTemp`,
`atomicReplace`, `finalReadback`,
`read`, `delete`, `inputValidation`, or `processTimeout`. A write payload is
rejected before process creation when its strict UTF-8 byte length exceeds the
helper protocol limit. Helper stdout and stderr are drained
concurrently with bounded buffers while asynchronous stdin writes, both output
drains, and process termination share one 15-second monotonic deadline. No
synchronous stdin write occurs before that deadline is active. A timeout uses
the Windows process-tree termination boundary and waits
only for a bounded shutdown interval, with a direct parent termination as the
last local fallback. Partial, unclosed, or oversized pipe content is
untrusted and is never parsed as the helper protocol. Persistent evidence
records the native helper exit, stage, safe category/code, and the closed
recovery action without recording a raw path, exception, journal bytes,
nonce, or secret. The `installTransaction` component and `failedField`
distinguish this boundary from firewall and shortcut failures.
Final transaction loading additionally records a closed `readbackStage` and
`readbackReason`. The stages are `rawRead`, `rawShape`, `jsonParse`,
`schemaValidation`, `freshnessValidation`, `identityValidation`,
`shortcutValidation`, `cleanup`, and `completed`. Reasons distinguish a
missing journal, duplicate or missing property, malformed JSON, unknown
property, wrong scalar type, stale journal, invocation-identity mismatch,
invalid shortcut snapshot, helper failure, and cleanup failure. These values
never include raw journal bytes, paths, nonces, shortcut bytes, or exception
text. Installer section identity includes both desktop and virtual-display
selection; the same two flags are frozen when the journal is written and when
final readback loads it.

`resolveFinalPath` does not compare a DOS path supplied by the caller with a
device or volume path returned by Windows. The helper first opens the trusted
ProgramData root without following reparse points, captures its volume/file
identity and handle-derived NT final path, and then binds each closed
`Ligase Host Admin` / `Transactions` segment beneath that handle-derived
prefix. Every child must remain on the same volume, have the expected segment
depth and name, and retain the same file identity before and after final-path
resolution. DOS-drive, device, and volume aliases therefore cannot create a
false rejection or become a path authority. A mismatch fails before ACL,
shortcut, firewall, or ARP mutation with `bindingMismatch` and one closed
reason: `trustedRootInvalid`, `volumeMismatch`, `segmentMismatch`, or
`fileIdentityMismatch`.

Persistent evidence may include only the closed root kind, segment count, and
boolean prefix/volume/file-identity results. It never includes the trusted or
resolved raw path, volume name, file identifier, exception, or user profile.
Each handle verification creates a fresh binding snapshot; checks that have
not run for the failing object remain false and can never inherit success from
an earlier Admin-root, Transactions, probe, or journal handle.

After handle binding succeeds, canonical-root and ACL inspection have separate
closed stages. Reading, sizing, copying, parsing, building, and semantically
comparing the security descriptor cannot inherit `resolveFinalPath`.
Persistent evidence records only a closed ACL-inspection reason; it never
records the descriptor, SDDL, account path, or managed exception. An inherited
or otherwise non-exact descriptor is the closed `notExact` state that enters
the existing empty-root recovery boundary. Malformed or unreadable descriptors
fail before ACL, shortcut, firewall, or ARP mutation. Exactness still requires
the Administrators owner, a protected DACL, and the exact SYSTEM and
Administrators allow ACE multiset (type, SID, mask, inheritance flags, and
count). Comparison is semantic rather than serialized-SDDL equality: Windows
may retain the `DiscretionaryAclAutoInherited` bookkeeping flag when an
existing directory receives a protected replacement DACL. That flag alone is
accepted only when every authoritative owner/DACL/ACE field is exact;
DACL `Defaulted`, `Untrusted`, or `AutoInheritRequired`,
inherited/extra/deny/object ACEs, or any SID/mask/flag drift remain non-exact.

Managed ACL inspection diagnostics are one exact tuple, not three independent
closed fields: `canonicalRoot/20001/canonicalRootInspectionFailed`,
`readSecurityDescriptor/20002/securityDescriptorReadFailed`,
`descriptorLength/20003/descriptorLengthInvalid`,
`descriptorCopy/20004/descriptorCopyFailed`,
`descriptorParse/20005/descriptorParseFailed`,
`buildSecurityDescriptor/20006/expectedDescriptorBuildFailed`, or
`compareSecurityDescriptor/20007/descriptorCompareFailed`. PowerShell rejects
any crossed stage, code, or reason before machine or persistent evidence is
accepted.

The existing-empty-root checks likewise publish one exact tuple:
`inspectEmptyRootOwner/20008/ownerNotAdministrators`,
`inspectEmptyRootChildren/20009/childEntryPresent`,
`inspectEmptyRootStreams/20010/namedDataStreamPresent`, or
`inspectEmptyRootStreamMetadata/20011/streamMetadataInvalid`. A native owner,
directory-query, or stream-query failure retains its allowlisted Win32
category/code at the corresponding stage and does not inherit a completed
`resolveFinalPath` diagnostic. Persistent evidence records only this closed
reason, never a stream name or directory entry.
The validation-only real-ProgramData alias probe remains read-only. When an
existing exact admin-only root denies ACL or child enumeration to the
non-elevated build harness, that single real-system observation is explicitly
`Inconclusive`, never `PASS`; the isolated binding fixtures remain
authoritative. If the initial snapshot is readable, any later readback failure
or drift remains fail closed.
The stream query itself is a separate `queryEmptyRootStreams` stage: exact
`ERROR_HANDLE_EOF` means no stream records exist, another Win32 error is
captured once and preserved, and a failed query with no native code is the
closed `managedFailure/20015/streamQueryFailed` tuple. A managed query-stage
setup or invocation failure before a native return uses the same
no-native-code tuple rather than leaving `none/0`. Metadata parsing begins only
after a successful query.
If store establishment fails after a known-residue ACL mutation, the helper
freezes the complete original failure diagnostic before rollback proof runs.
Rollback may update only the authoritative ACL-mutation, ACL-rollback, and
recovery-action fields; its identity, ACL, and empty-root proof stages cannot
replace the original failure tuple in IPC or persistent evidence. Every
rollback entry point uses the same snapshot-preserving wrapper, including the
inner ACL-apply/readback catch and the outer store-establishment catch.

Before PowerShell materializes helper failure JSON, a strict recursive raw
parser rejects duplicate property names at every object depth. This includes
same-value and conflicting duplicates of the machine code, native code, and
all binding and ACL-inspection fields; `ConvertFrom-Json` last-wins behavior is never used as a
duplicate-property authority. The materialized object must then have the exact
closed field set and types before any value reaches persistent evidence.

Directory discovery uses only list-directory, read-attributes, read-control,
and synchronize rights on its shared identification handle. It does not ask
for ACL or owner mutation rights before proving that the object is the
canonical directory and deciding whether recovery is required. Only the
exclusive recovery handle requests `WRITE_DAC` and `WRITE_OWNER`. Native
handle open, handle metadata verification, and final-path resolution are
separate closed stages so a failure cannot collapse back to an ambiguous
`openSegment` with `none`/`0`.

Rollback evidence keeps separate `shortcut`, `firewall`, and
`transactionCleanup` outcomes. If the journal was never created and the
in-memory shortcut snapshot is restored and read back exactly, shortcut
rollback is `completed` while transaction cleanup is `notCreated`; a missing
journal must not turn an exact shortcut restoration into an ambiguous
shortcut failure. The overall install result remains fail closed.

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
failure result. The successful helper projection is emitted from an explicitly
ordered dictionary because the NSIS consumer deliberately compares the
single-line JSON bytes exactly after trimming terminal newlines. This keeps
Windows PowerShell hashtable enumeration from turning a semantically successful
final readback into a UI false negative. Extra output, malformed JSON, reversed
or unknown fields, and a nonzero helper exit remain fail closed. A failure
state remains on the install-progress page, records a non-success machine
outcome, and exits with a nonzero native process code without showing the
normal success Result or Finish page. Silent `/S` uses the same final section;
it cannot skip final readback or convert a failed typed outcome into process
exit code zero. Rollback is
reported as `notRequired`, `completed`, or `failed`; rollback failure is never
discarded. Any install-root or DataRoot residue that cannot be safely removed
is classified in the evidence rather than being presented as a successful
empty installation.

Final-readback evidence also records a closed `failedField` and component
states for `artifacts`, `bootstrap`, `dataRoot`, `installTransaction`, `arp`,
`startMenu`, `desktop`, `firewall`, and `virtualDisplay`. Components already verified remain marked
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

### Unsigned development secure-store preflight

The standalone preflight is launched only by the sibling purpose-built
`Ligase.SecureStore.Preflight.Launcher.exe`. The non-elevated launcher accepts
no arguments, creates a CSPRNG-named one-shot pipe and nonce with an ACL
limited to the current user, SYSTEM, and Administrators, and performs one
manual `RunAs` of the fixed sibling `Ligase.SecureStore.Preflight.exe`. The
elevated child accepts only the closed pipe-name, nonce, and parent-PID tuple.
Before opening a pipe it enters the closed
`diagnosticChannelValidation` stage. A missing channel is reported as
`diagnosticChannelRequired`; malformed, extra, unknown, or out-of-range IPC
arguments are `invalidArguments`. Both return native 18 without opening a pipe
or resolving ProgramData. Only a parsed tuple may open the pipe, and only a
completed peer handshake advances to `securityInitialization`.
Both endpoints verify peer PID, session, and user SID before the bounded,
length-prefixed handshake. The IPC accepts no path, script, payload root,
manifest, security descriptor, or secret. The parent writes only a bounded
review observation beside the frozen D-drive artifacts; it is not a product or
secure-store authority.

If launch, peer validation, framing, or timeout fails after a child exists,
the launcher owns a single monotonic bounded cleanup. It snapshots the child
process tree, requests whole-tree termination, checks the bounded wait result,
then verifies the child and every observed descendant PID are absent. Only
that exact result may report `diagnosticChannelFailed` with cleanup completed.
Any kill, wait, exit-state, PID, or tree verification failure reports the
closed `childCleanupFailed` result with cleanup failed; an outer validation
harness may contain a test accident but is not cleanup authority for the
launcher. Process-tree enumeration is proof input, not a prerequisite for
containment: if the snapshot fails, the launcher still attempts whole-tree
termination, root fallback, bounded wait, and root-PID absence verification,
records those closed cleanup facts, and returns `childCleanupFailed` because
complete descendant absence could not be proven.

Artifact validation uses separately compiled development-only surfaces. A
`LAUNCHER_VALIDATION` launcher accepts only fixed test tokens, starts only the
fixed sibling validation executable without `RunAs`, and drives the real pipe
ACL, nonce, PID/session/SID, framing, timeout, disconnect, and cleanup code
against D-drive fixtures. The sibling is compiled with
`PREFLIGHT_ONLY;PREFLIGHT_VALIDATION` and returns before ProgramData access.
Neither symbol is present in the production launcher project, and production
binary gates reject every validation token or seam.

The child is a purpose-built, self-contained x64 development diagnostic. After
the IPC handshake it enables and reads back the no-child process mitigation
before ProgramData access. Its compiled entry point calls only the closed secure-store
recover/probe/readback path: the canonical
`%ProgramData%\Ligase Host Admin\Transactions` chain, an exact empty-root
recovery when eligible, and a marker-owned write/read/atomic-replace/delete
probe. It does not consume an installer manifest or caller-supplied path, invoke
PowerShell or `Manage-LigaseInstallation.ps1`, start a child process, copy
product files, or access bootstrap, DataRoot, shortcuts, firewall, ARP,
registry, driver, certificate, Query User, or product runtime state.

Its closed evidence is a fixed-name, exact-ACL file inside the same verified
admin-only Transactions chain. It is written through the SecureStore root
identity, reparse, ACL, same-directory temporary-file, and atomic-replace gates;
the elevated executable creates no separate public diagnostics path. Evidence
can be persisted only after that secure chain is established. An earlier
fail-closed root/open failure returns a closed nonzero outcome without creating
a weaker elevated evidence path; its bounded stage/category/code and ACL
mutation/rollback state is sent through the authenticated one-shot pipe and
recorded by the non-elevated parent in D-drive review evidence. An
evidence-write failure is never retried through another path
and cannot turn the original failure into success. After the root is verified,
the tool removes only its exact-owned prior evidence and writes a closed
`secureStorePreflightPending` record. Probe and cleanup success are not terminal
until a final atomic evidence write and its byte, file-identity, ACL, and root
readback commit `success=true` with
`resultCode=secureStorePreflightReady`. A final evidence-write failure leaves
at most the current pending record and returns native exit 18 with no ready
claim. All byte, ACL, file-identity, and root checks occur on the protected
same-directory temporary file before commit. The write-through atomic replace
is the last operation allowed to fail; after it succeeds the tool performs no
target reopen, console write, or other fallible I/O and returns native zero.
Native exit and current persistent evidence are the terminal authority;
console output is advisory and a broken console cannot reverse a committed
success or create a contradictory failure record. The evidence declares
`releaseKind=UnsignedDev` and
`trustBoundary=localManualExactSha`, plus result/stage, ACL, recovery, probe,
cleanup, and bounded native diagnostics. It never records a raw path, security
descriptor, nonce, secret, exception, or probe bytes.

This is deliberately a local manual development gate, not a release-safe trust
anchor. Coordination freezes the source commit and the exact artifact size and
SHA-256, checks those bytes once immediately before a single `RunAs`, and the
Windows UAC prompt is expected to show **Unknown publisher**. This boundary
does not defend against a malicious local standard user replacing the image
between the hash check and UAC image load. A `PublicRelease` preflight requires
an allowlisted Authenticode publisher, RFC 3161 timestamp, protected staging
that prevents replacement before execution, and a new security review.

The development recovery gate includes one exact semantic ACL shape for the
known empty partial-ACL residue left by the earlier failed gate: Administrators
owner, protected and auto-inherited DACL control flags, and the exact multiset
of SYSTEM, Administrators, Creator Owner, and Users allow ACEs with their
frozen masks and inheritance flags. ACE serialization order is not authority;
missing, extra, deny, SID, mask, inheritance, owner, or control-flag drift is
rejected. A semantic mismatch reports the closed
`knownResidue/managedFailure/20014` tuple rather than `none/0`. No raw SDDL,
descriptor, SID list, or path is emitted. While holding the exclusive directory
handle, recovery freezes the
original descriptor bytes/hash and FileId. Any ACL apply/readback failure
compares the live descriptor with those bytes and, if changed, restores and
rereads the original descriptor on the same handle. That compensation handle
remains owned until Transactions creation, its ACL/identity/reparse/empty
checks, and the final `SecureStore.OpenVerified` handle/readback all succeed.
Only then does an explicit commit release rollback authority. Any earlier
failure removes only a transaction-created empty Transactions directory,
restores the original Admin descriptor on the still-bound FileId, and verifies
the original empty/no-ADS fingerprint. A failed restore is a closed
`rollbackFailed` result, never `notAttempted` or `notRequired`. An unknown
non-exact ACL, FileId drift, child, named ADS, or reparse point is never
adopted. This previously observed residue shape is not a general installer
recovery policy and is not widened by adding machine-specific SDDL hashes.

The compensation lease has a closed state machine:
`Unarmed -> Frozen -> Mutated -> Committed`. Before `FreezeOriginal` succeeds,
the lease owns only a handle/identity context and has no rollback authority;
identity, child, named-ADS, descriptor-read, or known-fingerprint rejection
therefore preserves the original diagnostic and reports no mutation,
`aclRollback=notRequired`, and `recovery=notAttempted`. `Frozen` stores the
original descriptor and empty/no-ADS fingerprint but remains non-mutating.
Observed or possible ACL change advances to `Mutated`, where rollback is
mandatory. Only complete store establishment advances to `Committed`.

The self-contained .NET runtime may import general Windows child-process
functions even though the preflight application contains no
`Process.Start`, shell execution, or child-process call site. Those runtime
imports are expected and are not the security authority. At the first managed
entry point, before argument validation, environment access, ProgramData
resolution, or secure-store access, the process sets
`ProcessChildProcessPolicy.NoChildProcessCreation` and immediately reads the
policy back from its own process. The exact readback must contain only the
requested no-child flag. Set, readback, or mismatch failure returns a closed
`securityInitialization` failure and native exit 18 without accessing the
secure root. This mandatory OS mitigation, plus the source prohibition on
child-process APIs, is the development preflight's child-process boundary.

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
An `install.bat` exit code of zero is provisional rather than success
authority. Repair removes matching device nodes with a bounded loop before
creating a replacement. Each removal is followed by a bounded enumeration
readback, and creation is permitted only after that readback proves the
matching device count is zero. A nefcon removal exit of 6, or any other
nonzero exit, is a typed removal failure and is never interpreted as “no
devices remain.” Failure to read the count is closed
`virtualDisplayDeviceRemoveReadbackFailed`; failure to observe monotonic
progress within the settle or total deadline is
`virtualDisplayDeviceRemoveSettleFailed`. Before writing the ownership marker or
returning `virtualDisplayInstalled`, the helper requires exactly one present
device with the complete SudoVDA hardware ID, compared using Windows'
ordinal-ignore-case identity semantics without prefix, suffix, or substring
matching, and a bound Windows OEM INF. Zero or duplicate matching devices,
missing driver binding, or readback failure is closed. The readback projects
only the count and a SHA-256 of the sorted unique instance identities, never the
raw identities.

The child action atomically persists a strict, safe virtual-display diagnostic
containing the install stage, readback code, child/remove exits, removal count,
process cleanup state, marker stage, observed count, identity-set hash, and
binding result. A new child action first removes any previous diagnostic; the
strict readback binds the replacement to the current manifest source and a
bounded UTC freshness window. It contains no stdout, stderr, path, device identity, or
exception text. NSIS accepts success only when the child exit is zero and its
entire stdout is the exact terminal success JSON. On failure, the child also
returns the same closed diagnostic as a bounded ASCII, SHA-bound token. NSIS
passes that token without interpreting its contents; `FinalizeInstall` accepts
it only after strict base64url, hash, unique-property, schema, source-head, and
freshness validation. This secondary transport preserves the original failure
tuple when the primary diagnostic ACL or atomic file write is unavailable.
`FinalizeInstall` includes the validated tuple in `last-outcome.json`, so a
duplicate-device count or other failed UI retains the first closed
virtual-display authority for later readback. A secondary persistence failure
never replaces that original result code.

The ownership marker then uses a same-directory
write-through temporary file, byte and ACL readback, and an atomic replace or
move. A write, replace, or final marker readback failure is closed
`virtualDisplayMarkerCommitFailed`. On either path, the helper restores the
exact pre-existing marker bytes or its prior absence, removes transaction temp
files, and may remove only certificate-store entries which were absent before
this exact invocation and have no dependent device. Failure to prove marker,
temp-file, or certificate rollback is `virtualDisplayRollbackFailed`.
Pre-existing certificates and markers are not claimed or deleted.
Candidate construction requires an explicitly supplied official nefcon v1.8.0
x64 console tool. Before producing the payload, the build verifies its fixed
size, SHA-256, Authenticode publisher and timestamp, copies only those exact
bytes, and records the tool identity in the manifest. It never searches
`PATH` or downloads an installer tool.
The SudoVDA UMDF binary is a second explicit external build input rather than a
repository binary. Its authority is the official Apollo v0.4.6 driver package
whose INF and catalog bytes match the canonical package in this repository.
Before any build or staging work, the installer build requires the exact
83,216-byte x64 `SudoVDA.dll`, SHA-256
`47EE263CB5DE9382C6630A2D7F3DAFEC4A49419F953BEEC869CA5DD0C460FF63`,
embedded signer thumbprint
`3C918FC73525AD8B1521B6DB26B71F694277CC49`, and INF driver version
`1.10.9.289`. The INF `SourceDisksFiles`, `CopyFiles`, and service-binary
references must form the exact `SudoVDA.dll` closure. The build copies that
validated binary beside the INF/CAT/CER files and records its size, hash,
architecture, version, and signer in the package manifest. Missing, extra,
wrong-hash, wrong-architecture, wrong-signer, or version-mismatched driver
input fails before candidate creation. Installed-tree bytes, `PATH`, and
network fallback are never package authority.
The batch wrapper emits one bounded
closed tuple and preserves the exit of each certificate, device-create and
driver-package step; its final `popd` cannot replace a failed child exit with
success. The managed owner independently binds that tuple to the process exit
and distinguishes missing/wrong tools, certificate Root/TrustedPublisher
failure, device creation, driver package installation, timeout/output failure,
reboot-required state, and final present-device plus bound-OEM-INF readback.
The process owner reads stdout and stderr incrementally as raw bytes into
separate fixed 512-byte buffers and rejects invalid UTF-8. It creates the
command interpreter suspended, binds it to a kill-on-close Job Object, and
only then resumes its first thread. A `STARTUPINFOEX` handle list permits only
the stdout and stderr write handles to cross into the child; unrelated
inheritable handles from the elevated owner are excluded. Overflow, timeout,
or pipe failure uses
that retained Job authority even if the root process has already exited; the
original typed failure is returned only after root wait, zero active Job
processes, and bounded closure of both pipe reads. Assignment, resume,
termination, wait, Job accounting, or pipe-cleanup uncertainty is the distinct
closed `virtualDisplayInstallerCleanupFailed`. Before Job assignment, the
native owner retains the root handle until termination and a signaled wait are
proved. After assignment it additionally requires Job termination and zero
active Job processes. If its first bounded cleanup cannot prove those facts,
it retains the handles and exposes only a numeric PID to the D-only caller for
a second bounded containment pass; it never drops the last termination
authority while claiming zero residue. This retained authority exists only
inside the current helper process; it is not persistent across PowerShell
exit. If the second pass also fails, the closed result reports failed cleanup,
the retained nonzero PID, and the incomplete secondary state. The validation
harness then performs a separate bounded accident cleanup and proves PID zero;
production reports the residual PID/user-action boundary and never calls the
failed cleanup completed.
A D-only process seam requires both the validation-harness environment and an
exact explicit D-root. It exercises exact success, every closed child exit,
malformed and cross-spliced tuples, both stream overflows, timeout, inherited
pipe and descendant cleanup, including stdout/stderr overflow after the root
exits while a descendant retains a pipe, plus assignment/resume/termination/
wait/Job-accounting/pipe cleanup faults and second-pass containment, without
opening `Cert:`, PnP or ProgramData.
The D-only validation seam for this transaction requires both the repository
validation-harness switch and an exact explicit D-root binding; production
installer flows cannot select it. It exercises every marker write/replace/
readback failure and certificate-compensation branch without opening
`Cert:`, PnP, or ProgramData.

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
