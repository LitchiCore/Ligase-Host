# Virtual display setup architecture

Status: `ARCHITECTURE_FROZEN`. This document freezes the proposed replacement
boundary. It is not an implementation claim and does not authorize an
installer run, elevation, driver mutation, or reuse of a superseded candidate.

The machine contracts are
[`virtual-display-setup-request-v1.schema.json`](virtual-display-setup-request-v1.schema.json)
and
[`virtual-display-setup-result-v1.schema.json`](virtual-display-setup-result-v1.schema.json).
Those schemas, rather than NSIS strings or PowerShell objects, are the sole
wire authority for both provisioning and uninstall.

## Product boundary

Core Host installation and virtual-display provisioning are separate
transactions.

- Core installation owns payload, structured layout, bootstrap and identity,
  shortcuts, ARP registration, and firewall configuration.
- Virtual-display setup owns only exact SudoVDA PnP nodes, its package and
  certificate trust, and its ownership marker.
- A Core commit is not rolled back because virtual-display setup fails.
- Final UI reports both axes, for example `Host installed` and
  `Virtual display unavailable`. Physical-desktop streaming remains available.
- `Repair virtual display` reruns only the virtual-display transaction against
  the already installed, manifest-verified payload. It does not reinstall Core,
  rewrite bootstrap identity, reapply firewall, or replace user data.
- Uninstall is a separately authorized invocation of the same native owner; a
  failed repair never silently becomes uninstall or cleanup.

The installer may return an overall product state equivalent to
`hostInstalledWithOptionalComponentFailure`. That state is a completed Core
installation, not a generic installation rollback. A separate machine code
identifies the virtual-display failure and the UI offers a bounded repair
action.

## Single native owner

`Ligase.VirtualDisplay.Setup.exe` is one pinned, self-contained x64 native
helper. It may be implemented by extending the current SetupAPI helper, but its
shipping identity is new and its command surface is closed:

```text
Ligase.VirtualDisplay.Setup.exe <provision|uninstall> --request-handle <uint64> --result-handle <uint64>
```

Both operations require elevation. The argv verb must equal the immutable
request operation and the result operation; any cross-operation splice is
rejected. Result inspection belongs to the non-elevated
caller after the helper exits; the helper has no path-based `inspect` verb.
There is no arbitrary instance-ID command line, environment override, PATH
lookup, PowerShell callback, or text-tool success parser. The installer resolves
the helper only through the installed/staged manifest, verifies architecture,
size and SHA-256 immediately before launch, and passes only the two explicitly
inherited file handles through the typed invocation seam. There is no
path-based request/result fallback.

The request is a small versioned JSON object containing only:

- `schemaVersion=1` and `operation=provision|uninstall`;
- a caller-generated operation ID and creation time;
- the expected helper SHA-256, driver package SHA-256 and source head;
- the exact target hardware ID `ROOT\SUDOMAKER\SUDOVDA`;
- bounded run and settle budgets;
- booleans for certificate/package/create work. They are all true for
  `provision` and all false for `uninstall`;
- `legacyMarkerPolicy=v1CertificateOnly` and
  `packageOwnershipPolicy=provisionOperationProvenanceOnly`, so caller and
  helper cannot negotiate a weaker ownership inference policy.

The request also carries the closed transport assertions required by its
machine schema: inherited-handle mode, the protected operation-directory ACL,
non-reparse proof, CreateNew/exclusive access and canonical file-identity
hashes for both handles. It contains no raw certificate, secret, user data, command line, exception,
arbitrary device ID, or unrestricted path. Driver and certificate paths are
resolved beneath the manifest-verified deployment root inside the helper.

## Mandatory elevated request/result boundary

The caller creates a new operation directory under its protected installer
data root with an explicit protected DACL granting only SYSTEM,
Administrators and the initiating caller. It opens every directory segment
without following a reparse point and rejects any reparse attribute. It then
creates the request and result files with CreateNew. The request handle is
read-only to the helper; the result handle is exclusive and write-through.
After the caller finishes the request bytes it flushes them to disk and closes
every write-capable request handle. It then opens the one inherited request
handle for read access with write sharing denied. The native typed launcher
inherits exactly that immutable request handle and the exclusive result handle,
never a path.

Before launch, the caller obtains each handle's canonical final path, volume
serial and file ID and hashes that tuple into the request transport fields.
The helper independently checks regular-file type, access mode and the same
canonical identity on the inherited handles before reading the request once.
It also requires the request handle to deny write sharing; a surviving writer
or a handle that permits a new writer is a request rejection. It never reopens
either name. The helper writes, flushes and rereads the result
through the supplied result handle and is the only result-byte writer while it
runs. After a clean exit and closed Job/pipe cleanup, the caller reopens the
operation directory without following reparse points, opens the result once,
and requires the same volume/file ID and exact verified bytes. Any ACL,
reparse, identity, replacement, handle-mode, readback or hash uncertainty is
`setupResultUnavailable`; an unverified file is never accepted as a domain
result. This handle protocol is mandatory, not one interchangeable option.

## Provisioning state machine

The helper is the sole owner of domain sequencing and correlation. NSIS and
`Manage-LigaseInstallation.ps1` do not reconstruct this state.

| State | Required authority | Success transition | Closed failure |
| --- | --- | --- | --- |
| `validateRequest` | strict schema, manifest identity, source head, hashes, budgets | `inventoryBefore` | `requestRejected` |
| `inventoryBefore` | SetupAPI `DIGCF_ALLCLASSES`, present and non-present devnodes; full ordinal-ignore-case hardware-ID match | `readOwnership` | `inventoryUnavailable` |
| `readOwnership` | fresh proven inventory; strict v1/v2 journal read with no inferred ownership | healthy exact-one+present+bound → `completed/alreadyInstalled`; zero → `stableZero`; otherwise `remove` | `ownershipReadFailed` |
| `remove` | one selected exact instance from the fresh inventory epoch | `inventoryAfterRemove` | `removeNativeFailed` or `removeRebootRequired` |
| `inventoryAfterRemove` | fresh all-devnode inventory | `remove` or `stableZero` | `removeReadbackFailed` or `removeNoProgress` |
| `stableZero` | at least three zero samples, one identity epoch, bounded settle window | `trustPackage` | `zeroProofFailed` |
| `trustPackage` | pinned certificate and driver package; ownership recorded only after verified operations | `create` | `trustFailed` or `packageFailed` |
| `create` | zero proof still current; nefcon is permitted only for create if no supported direct API replaces it | `readbackAfterCreate` | `createFailed` |
| `readbackAfterCreate` | fresh all-devnode exact inventory | `commitMarker` | `finalReadbackFailed` |
| `commitMarker` | exact-one, present, expected bound INF, package/certificate ownership | `completed` | `markerCommitFailed` |
| `completed` | atomic result readback | terminal | none |
| `failed` | first failure frozen; compensation separately reported | terminal | none |

For every removal, the helper obtains the instance identity from its own fresh
inventory and binds it to an operation nonce, inventory epoch, full InstanceId
hash, and authority hash. `DiUninstallDevice` is the primary removal API. The
helper never calls `SetupDiRemoveDevice` directly. If a future OS-gated
`PnPUtil /remove-device <exact instance>` fallback is retained, its System32
binary identity and typed exit are verified; localized output is never success
authority. Fresh strict count decrease remains mandatory.

All present, phantom, unbound, stopped and class-unknown exact-HWID nodes count
toward duplicate prevention. Class, friendly-name, presence and instance-text
prefilters are forbidden. A property or enumeration uncertainty is unknown,
not zero.

## Result ownership and atomicity

Each invocation writes exactly one result object conforming to the linked
schema through the caller-created exclusive result handle. The helper flushes
and rereads the same handle and the caller independently performs the identity
and exact-byte verification described above. A fresh invocation receives new
CreateNew files; existing results are immutable and never overwritten.

The result's `code` and `stage` are the frozen first domain failure authority.
The schema's top-level `oneOf` binds every accepted code to exactly one stage
and to its complete inventory, removal, zero-proof, component, compensation
and execution shape. Cleanup and compensation are
secondary fields and cannot change `code`, `stage`, or the last proven device
authority. `completed` requires exact-one, present and bound. `failed` may
report a last proven inventory or `unknown`; it never fabricates zero.

`alreadyInstalled` is a distinct direct success transition from the initial
fresh inventory. It requires exact-one, present and expected-bound INF;
removal is not required with zero attempts, zero proof is not attempted, and
trust/package/marker must be verified as already owned while create is not
required. Only the repair/create path may claim completed stable-zero proof.

## Uninstall state machine

`uninstall` uses the same helper identity, transport, inventory and removal
authority. It never enters trust installation, package installation, create or
post-create readback.

| State | Required authority | Success transition | Closed failure |
| --- | --- | --- | --- |
| `validateRequest` | request operation and argv both exact `uninstall`; provisioning selectors false | `inventoryBefore` | `requestRejected` |
| `inventoryBefore` | fresh all-devnode exact inventory | zero → `stableZero`; nonzero → `remove` | `inventoryUnavailable` |
| `remove` | one exact instance from the current fresh epoch | `inventoryAfterRemove` | `removeNativeFailed` or `removeRebootRequired` |
| `inventoryAfterRemove` | fresh strict count decrease | `remove` or `stableZero` | `removeReadbackFailed` or `removeNoProgress` |
| `stableZero` | three consistent zero samples within the single deadline | `cleanupOwnership` | `zeroProofFailed` |
| `cleanupOwnership` | strict journal read, then package → trust → marker only with the unchanged marker's Ligase ownership and fresh readback | `completed` | `ownershipReadFailed`, `packageCleanupFailed`, `trustCleanupFailed` or `markerCleanupFailed` |
| `completed` | zero inventory and verified ownership absence | `uninstalled` or `alreadyAbsent` | none |

`alreadyAbsent` requires initial zero, stable-zero proof, removal not required,
and marker/package/trust independently proven not owned. `uninstalled` requires
the same terminal zero plus at least one removal or owned-resource cleanup. An
unowned certificate or driver package is never deleted. The marker remains
byte-for-byte unchanged as the sole ownership journal until package and trust
are freshly proven clean; it is removed atomically only as the final step.
Ownership uncertainty fails closed before mutation; cleanup failure freezes
its first failure and never claims that virtual display was fully removed.

Ownership cleanup is intentionally forward-only. Removing an owned package or
certificate is not compensated by silently reinstalling or
retrusting it during uninstall. Therefore uninstall results require
`compensation=notRequired/none`; this is an explicit closed branch, not a claim
that cleanup completed. The first failed cleanup component and the last proven
zero inventory remain authoritative. While the unchanged marker exists, a
resource that it proves owned but fresh readback finds absent is
`absentOwned`, never `notOwned` and never falsely `removed` by this invocation.
A crash after package or trust mutation therefore reenters with the marker
intact, projects the missing resource as `absentOwned`, and continues. Package
failure requires marker `verified/owned`, package failed and trust not
attempted. Trust failure requires marker `verified/owned`, package
`removed|absentOwned` and trust failed. Marker failure requires package and
trust `removed|absentOwned`, while marker remains failed with owned-or-unknown
authority. A conflicting or unreadable marker fails before package or trust
deletion. After final marker removal, a crash reenters only as fresh zero with
all resources absent and may finish idempotently as `alreadyAbsent`; no second
journal is introduced.

Strict ownership-journal reading is its own pre-mutation failure boundary.
Provision performs this read immediately after its first proven inventory and
before exact-node removal, trust, package, create or marker mutation. A failed
provision read is the distinct `ownershipReadFailed/readOwnership` branch: its
fresh inventory remains proven, removal and zero proof are not attempted,
trust/package/create are not attempted, marker ownership is unknown, the
current operation is the sole identity, and `resourceMutationCount=0`.
Unknown/unreadable/duplicate/schema/type/version/identity-change failures keep
`source=unknown`; only a strictly identified v1 or v2 journal may report a
later conflict or readback failure. It cannot be projected as request,
inventory, package or marker-commit failure.

After exact-node stable zero, `ownershipReadFailed/cleanupOwnership` freezes
terminal-zero inventory, completed zero proof, zero resource mutations, and
package/trust not attempted. IO, unreadable, duplicate-property, schema, type,
version, and identity-change/TOCTOU failures retain `source=unknown`; only a
strictly identified v1 or v2 journal that subsequently conflicts or fails
readback may name that version. Package ownership remains `unknown` unless a
non-owned or legacy-unknown state was independently proven before the later
failure; certificate ownership remains `unknown`, acquisition remains `none`,
and the marker is failed with unknown
ownership. This branch stops Core uninstall before package, trust, marker, or
Core mutation and preserves the retry payload; it cannot masquerade as a
package/trust/marker cleanup failure.

### Legacy marker migration

The shipping v1 marker authorizes only its exact certificate thumbprint and
listed stores. A matching package hash, bound INF or functional device does
not prove Ligase added or exclusively owns a driver-store package. The v2
marker records package ownership separately as `addedByLigase`, `notOwned` or
`legacyUnknown`; only an absent→publish→fresh-readback transition performed by
this helper may create `addedByLigase`. The result and v2 marker carry the same
closed `ownershipAcquisition` provenance. `addedByLigase` is valid if and only
if its values are exactly `preState=absent`,
`mutation=publishedByLigaseProvision`, and `readback=presentPinned`. A package
identity/hash, matching INF, current binding, `alreadyOwned`, or a successful
functional readback cannot substitute for any member of that tuple.

Certificate ownership is never an aggregate inference. The result and v2
marker carry one canonical ordered `certificateStores` authority with exactly
`LocalMachine\\Root` followed by
`LocalMachine\\TrustedPublisher`. Each entry independently freezes
`ownership`, its authority source, pre-state, mutation, fresh readback and
uninstall cleanup state. Only `absent -> addedByThisOperation -> present` in
the current provision may be `addedByLigase`; a preexisting/already-trusted
store is `notOwned`. A strict v1 marker maps only its exact listed stores to
historical `legacyOwned`; each unlisted store is independently classified by
the current operation as `addedByLigase` or `notOwned`. A strict v2 read copies
every entry from the marker and labels
the authority historical. Presence, thumbprint, catalog/package hash or a
bound device cannot upgrade a store to owned.

A schema-v1 marker with an empty `certificateStores` array is a valid legacy
journal. It asserts only that v1 declared no certificate store as owned. Both
canonical stores are projected from fresh observation as `notOwned`, with
owned count zero and the exact ordered-pair hash; even a matching certificate
already present in either store remains non-owned. The empty list does not
authorize certificate deletion and does not prove package acquisition. Its
package authority remains `legacyUnknown`, except that a fresh absent package
may be represented by the existing non-owned/absent component without being
promoted to `addedByLigase`.

`ownedCount` and `ownershipSetSha256` are derived from the two ordered
ownership values. The hash input is strict UTF-8 with no BOM or trailing
newline:
`LocalMachine\\Root=<ownership>\nLocalMachine\\TrustedPublisher=<ownership>`.
The schema enumerates every valid pair, so store order,
count, hash and aggregate `certificateOwnership` cannot drift. Zero owned
stores is valid only when both entries are `notOwned`. Partial provisioning
records only the store actually added. Pending, committed and failed marker
states retain the same frozen store tuple byte-for-byte; commit failure cannot
splice current and historical provenance. During uninstall, only
`addedByLigase|legacyOwned` entries may become `removed|absentOwned`;
`notOwned` entries are retained without mutation. Aggregate trust cleanup is
completed only when every entry satisfies that rule, and any failed owned
store remains a closed trust-cleanup failure while the marker journal stays
intact.

An uninstall of a v2 shared package has two mutually exclusive certificate
success variants under the same `uninstalledSharedPackageRetained` code. If
at least one store is owned, every owned/legacy entry is
`removed|absentOwned`, every non-owned entry is retained, and aggregate trust
is owned-clean. If both stores are `notOwned`, owned count is zero, both
cleanup states are retained, and aggregate trust is only
`retainedNotOwned` for fresh presence or `absentNotOwned` for fresh absence.
That all-non-owned branch never reports certificate removal. Package
retention/absence, marker-last removal, terminal zero and the Core-uninstall
success gate are otherwise unchanged.

The same all-non-owned closure applies to a strict empty-store v1 journal under
`uninstalledLegacyPackageRetained`: exact-node removal and stable zero precede
cleanup, both stores are retained or freshly absent without mutation, the
legacy-unknown package is retained or freshly absent, and the marker is
removed last. This verified terminal may allow Core uninstall to continue;
it cannot be cross-spliced with `legacyOwned`, certificate removal, package
acquisition, or a v2 source.

The result has one non-duplicated ordered `operationIdsSha256` identity list.
Index 0 is always the current request/result operation. A first acquisition
uses `source=currentOperation`, `acquisitionOperationIndex=0`, and a one-item
list; the helper atomically writes that index-0 identity into v2 only after the
closed acquisition transition. A later `alreadyInstalled` or `uninstall`
reads the marker and uses `source=historicalOperation`,
`acquisitionOperationIndex=1`, and a two-item `uniqueItems` list
`[current, acquisition]`. Thus the historical identity is mechanically
different from the current invocation without duplicating two fields that a
Draft 2020 schema could not compare. The consumer also requires index 1 to be
the marker's exact stored acquisition identity and records
`markerIdentityReadback=exact`; missing, changed, or unreadable provenance
fails before ownership-dependent mutation. `notOwned|legacyUnknown` uses
`state=none/source=none` and a one-item current-operation list, so it cannot
carry provenance.

The provenance source is also stage-bound in both directions. `currentOperation`
is valid if and only if this is `operation=provision` and the package component
is exactly `completed/installed`. `historicalOperation` is valid only for a
provision result whose package is `verified/alreadyOwned`, or for uninstall of
a v2 `addedByLigase` marker. Request, initial inventory, removal, zero-proof,
trust and package failure branches carry `none` and one current identity;
they cannot claim ownership obtained before the package stage. Create,
post-create readback and marker-commit branches select current versus historical
solely from `installed` versus `alreadyOwned` package state.

Current acquisition does not pretend that a durable marker existed before
this invocation. After absent→publish→fresh pinned readback, migration is
`v2Pending/source=none` when no prior marker exists, or
`v1UpgradePending/source=v1` after a strict v1 read. Create and post-create
readback failures retain that pending authority without claiming a marker
commit. Only atomic v2 write, flush, replace, identity-preserving readback and
exact content verification advances fresh state to
`v2Committed/source=none`, or a strict v1 migration to
`v1Upgraded/source=v1`; both require `marker=completed/committed`. A failure
in any marker commit stage is respectively `v2CommitFailed` or
`v1UpgradeFailed`, remains current acquisition with frozen first failure, and
requires a failed marker plus the actual compensation tuple. `v2Read` is
reserved for exact preexisting-v2 historical provenance and cannot be current.
After a crash between package publication and durable marker commit, a fresh
invocation has no ownership journal: package identity or presence cannot
reconstruct ownership, so it is retained as `notOwned|legacyUnknown` and is
never deleted. When that reentry later commits a new v2 journal it uses
`v2RetainedCommitted/source=none`, package `verified/alreadyOwned`, acquisition
`none`, and a committed marker; it cannot be spliced into package deletion or
current acquisition.
For a retained package, the equivalent terminal failures are
`v2RetainedCommitFailed/source=none` or
`v1RetainedUpgradeFailed/source=v1`; both require acquisition `none`, package
`verified/alreadyOwned`, a failed marker, and the actual compensation. Every
`markerCommitFailed` result must select exactly one of these four terminal
migration states. Pending, committed, read, or historical migration cannot be
cross-spliced into a finished marker failure. Because create has completed in
every marker-commit failure, compensation is mandatory evidence: its state is
`completed|failed` and its code is `trust|package|marker|multiple`;
`notRequired/none` is invalid for all four terminal states.

Provision reads v1 strictly and preserves its certificate ownership. It keeps
the original bytes until an atomic v2 replacement has been flushed, replaced
and read back. Preexisting/already-bound packages remain
`notOwned|legacyUnknown`; successful function never promotes ownership by
inference. Uninstall may consume v1 directly as a certificate-only journal:
after stable zero it retains the package, cleans only listed certificate
stores, and removes the marker last. Verified
`uninstalledLegacyPackageRetained` reports
`package=retainedLegacyUnknown`; it permits Core uninstall because exact nodes
are zero and the unknown/shared package was not mutated, but never claims the
package was removed.

V2 deletes a package only for `addedByLigase`; `notOwned|legacyUnknown` is
retained. Parse/conflict/readback uncertainty fails before related mutation.
For v2 `notOwned`, fresh package presence is
`verified/retainedNotOwned/notOwned`; fresh absence is
`verified/absentNotOwned/notOwned`. These component codes are the single fresh
presence authority rather than a second independently spliceable boolean.
After owned certificate cleanup and marker-last
removal, either form can complete as verified
`uninstalledSharedPackageRetained` and allow Core uninstall to continue. The
present form is explicitly disclosed as a shared package retained without
mutation. It is distinct from `uninstalledLegacyPackageRetained`, whose
package ownership remains `legacyUnknown`; neither branch can reach package
deletion or be cross-spliced into `addedByLigase`.
Crash/reentry covers every v1→v2 write/flush/replace/readback stage, device
removal, certificate cleanup and final marker removal. No package identity,
binding or INF field can be cross-spliced into owned authority.

Core uninstall invokes this operation before deleting the pinned helper,
schemas, driver payload or ownership marker. If it fails, Core payload removal
stops before payload, ACL, firewall, bootstrap identity or other Core mutation.
This includes any failed result, reboot-required result, ownership-cleanup
failure, missing/unverified result, or caller `persistenceUnavailable`. UI
reports `Host remains installed / Virtual display removal not confirmed / You
can retry uninstall`, preserves the Setup helper, schemas and verified payload
needed for retry, and claims neither Host nor virtual display removed. Only a
verified completed `alreadyAbsent`, `uninstalled`,
`uninstalledLegacyPackageRetained`, or `uninstalledSharedPackageRetained`
result permits Core uninstall to continue. The retained results are safe
owned-only completions, not a silent or forced `leave component installed`
bypass.

The result records only closed metadata:

- current operation identity and, only for historical owned provenance, the
  marker's distinct acquisition-operation identity in one ordered unique list;
- package acquisition pre-state, this-operation mutation, and fresh pinned
  readback, correlated bidirectionally with package ownership;
- result code, state and stage;
- an anonymous bounded node-state array (`present`/`bound`) and unique-set
  hash; total/present/bound counts are derived from that array so a separate
  contradictory count cannot be spliced into the result;
- native removal code and `rebootRequired` when a legal API result exists;
- a bounded array of successful removals, each carrying strict-decrease proof,
  plus one terminal removal state; attempt/progress authority is derived from
  those records rather than copied into independent counters;
- a bounded zero-sample array plus window/epoch consistency; sample count is
  the array length rather than a second field;
- certificate, package, create and marker states;
- compensation state and residual authority;
- elapsed and hard-cap budget plus atomic result-file state.

Raw InstanceIds, raw stdout/stderr, paths, argv, certificate bytes, exception
text, environment and unrelated PnP data are forbidden.

## Caller contract

NSIS and the managed installation seam have four responsibilities only:

1. verify the pinned helper and create a fresh operation directory;
2. launch one helper through a bounded native argv/Job/pipe owner;
3. require a clean helper exit, prove process/Job and pipe cleanup, then read one
   strict result JSON and validate it against the manifest-pinned result-schema
   bytes, including the complete top-level branch and all cross-field rules;
4. project the result code and the independent Core/virtual-display product
   states into UI and the ordinary installation outcome.

The caller does not enumerate PnP, remove a node, prove zero, import trust,
install the package, create a devnode, correlate nested tuples, encode a
diagnostic token, or merge competing outcome files. If no valid helper result
exists, the caller reports `setupResultUnavailable` with process cleanup
authority in the ordinary installer execution outcome. It does not create a
second virtual-display result or infer a domain stage. Process/Job/pipe cleanup
is caller-owned because the helper cannot truthfully attest that its own
process and pipes are closed before it exits.

A result file is authoritative only when the pinned helper exits cleanly and
the caller's cleanup proof is closed. A file left by a crash, timeout, killed
process, overflow, or unclosed pipe is ignored even if it parses.
The caller starts one absolute deadline before native process creation. Its
bounded capture accepts at most 4096 bytes from either diagnostic pipe. Normal
and exceptional paths share the same terminal proof: root exit, zero active
Job processes, and EOF on both pipes. Timeout, overflow, pipe fault and retained
start authority first terminate the owned tree and continue draining within
the remaining deadline; an incomplete proof is a closed cleanup failure and
must never be reported as process zero. Schema validation is accept/reject
only: the caller never fills a missing field or constructs a replacement VD
tuple.

Repair uses the same helper and result schema. The UI must require an explicit
user action and elevation for repair. It shows the previous safe machine code,
not raw diagnostics. A repair result updates only virtual-display readiness.

## Legacy retirement inventory

Implementation is incomplete until each old authority below is physically
removed from production paths and source gates prove zero references:

- PowerShell virtual-display reconciliation, stable-zero and terminal PnP
  inventory logic;
- removal request/response token encoding and nested `removalRunner` tuple;
- `virtual-display-outcome.json`, finalize handoff, existing-primary selection,
  secondary readback/writer, last-resort virtual-display projection, and their
  cross-field schema validators;
- virtual-display diagnostic file/token transfer through NSIS;
- nefcon removal and PnPUtil text fallback (nefcon create may remain only if
  justified and pinned);
- installer rollback that treats optional virtual-display failure as failure of
  the already committed Core Host transaction;
- D-only fixtures that exist solely to validate retired token/file/writer
  authorities.

Migration must be staged so there is never dual production authority:

1. add helper, request/result schema and native D-only state-machine tests;
2. package and pin the helper without invoking it from production;
3. switch provision/repair to the helper and new UI product-state projection in
   one source snapshot;
4. remove all legacy production authorities and obsolete fixtures in that same
   snapshot;
5. independently verify packaged bytes before any elevated acceptance.

Historical diagnostic files may be read only for migration telemetry and then
ignored. They are not input to the new helper and are not rewritten or deleted
during install/repair.

## Test matrix

### Native unit and D-only integration

- strict request/result schema: missing, extra, duplicate, wrong type, enum,
  count/hash correlation and version mismatch;
- helper manifest/hash/architecture mismatch and reparse/path substitution;
- inventory 0, 1 and 2 exact nodes; present, phantom, unbound, stopped and
  class-unknown; unrelated devices never appear in the result;
- authority nonce/epoch/InstanceId-hash drift and arbitrary-ID rejection;
- `DiUninstallDevice` success, native failure and reboot-required;
- 2→1→0 progress, no progress, reappearance, post-remove readback failure and
  settle/total deadline crossing;
- three-sample stable zero, transient zero, epoch drift and late reappearance;
- certificate already-owned/newly-added/conflict, package install/readback,
  create failure, post-create 0/1/2 and wrong/unbound INF;
- compensation success/failure with first failure unchanged and residual
  inventory exact or unknown;
- timeout, overflow, malformed UTF-8/JSON, stdout/stderr/dual pending, Job start
  and cleanup failure; root/descendant/job zero and pipes closed before safe
  caller completion;
- request/result DACL, inherited-handle list, CreateNew, per-segment reparse,
  regular-file/access-mode, canonical final-path/volume/file-ID mismatch,
  replacement between launch/readback, request read-once and result exclusive
  writer faults;
- result handle write/flush/readback/hash faults and immutable previous result;
- code/stage, inventory node/count/hash/residual, removal/native/reboot/decrease,
  zero-proof, component, compensation and elapsed/hard-cap cross-splices; every
  accepted result has `resultFileState=verified`;
- `alreadyInstalled` exact-one present+bound direct success with removal and
  zero proof not attempted, plus contradictions that try to attach removal,
  zero proof, create or newly-created ownership.
- provision/uninstall result cross-splices in both directions;
- uninstall zero and already-absent idempotency, 2→1→0 removal, native failure,
  reboot, readback/no-progress and zero-proof failure;
- package/trust/marker cleanup ownership absent, owned, `absentOwned`, conflicting and
  readback failure; cleanup-order violations and unowned-resource deletion;
- ownership-journal read IO/unreadable, duplicate property, schema/type/version,
  conflict, identity-change/TOCTOU and readback failures before every resource
  mutation, with source/reason/marker-code correlation;
- crash/reentry at each uninstall transition, always restarting from fresh
  inventory and immutable ownership rather than a prior partial result.
- v1 certificate-only migration with preexisting same-SHA/shared-bound package,
  package absent, partial certificate absence, replacement crash and uninstall
  retry; identity/binding must never promote package ownership.
- v2 `notOwned` package `retainedNotOwned`/`absentNotOwned`, owned certificate cleanup and
  marker-last crash/reentry; `retainedNotOwned`, `retainedLegacyUnknown`, and
  `addedByLigase` are mutually exclusive, and only the last can reach package
  deletion.
- fresh current v2 commit and strict-v1 current upgrade, every temp/write/flush/
  replace/readback failure, plus crash after publish and before durable marker
  commit; reentry must retain an unjournaled package as non-owned.

The schema review fixture must exercise this minimum negative matrix through
the actual request/result consumers:

| Splice | Required rejection |
| --- | --- |
| any failure code with a different stage, or `firstFailureFrozen=false` | code/stage/first-failure branch |
| zero inventory with non-empty hash, or bound node that is not present | node-array/hash/residual branch |
| removal not required with records/native success/reboot, or native failure with reboot | removal terminal branch |
| completed zero proof with fewer than three samples, unstable epoch or zero window | zero-proof branch |
| later component completed before its prerequisite, or `alreadyInstalled` with create/removal | per-code component branch |
| trust/package/create/final-readback/marker failure carrying native-failure, reboot, readback-failure or no-progress removal | late-stage removal branch, tested in both splice directions |
| pre-trust failure with compensation, or compensation code outside reached ownership | compensation branch |
| elapsed greater than the fixed hard cap, or any result-file state except verified | execution branch |
| path mode, weak ACL, reparse, non-CreateNew, unflushed request, surviving writer, write-sharing request handle, replaceable identity or inheritable extra handle | request transport branch |
| provision request with uninstall result, uninstall request with provision result, or argv verb mismatch | operation branch |
| uninstall with create/install state, cleanup out of package→trust→marker order, `absentOwned` without an intact owned marker, or unowned-resource deletion | uninstall ownership branch |
| v1 or legacyUnknown package projected addedByLigase/removed, retained package reported removed, or migration source/state/ownership splice | legacy migration branch |
| addedByLigase without exact absent→publishedByLigaseProvision→presentPinned provenance, or that provenance attached to notOwned/legacyUnknown | ownership acquisition iff branch |
| uninstall carrying currentOperation acquisition, historical provenance with missing/index-0/duplicate/drifted marker identity, new provision acquisition not at current index 0, or alreadyInstalled without historical index 1 | operation provenance branch |
| request/inventory/removal/zero/trust/package failure carrying current or historical provenance, packageAlreadyOwned carrying current, or packageInstalled carrying historical | provenance stage branch |
| v2 notOwned package deleted, present package reported absent, absent package reported retained, or notOwned/legacyUnknown/addedByLigase retained-result splice | shared-package retention branch |
| provision ownership-read failure carrying cleanupOwnership, removal/zero/resource activity, unknown inventory, historical acquisition/ID, mismatched source/reason/marker code, or request/inventory/package/marker-commit code; uninstall ownership-read failure carrying readOwnership or nonzero terminal inventory | operation-specific ownership-read pre-mutation branch |
| fresh current acquisition reported v1Upgraded/v2Read, v2Committed on uninstall/historical/failed marker, pending state with committed marker, failed commit without markerCommitFailed, or crash reentry promoting an unjournaled package to owned | current marker commit branch |
| markerCommitFailed carrying pending/committed/read/historical migration, retained failure carrying added/current acquisition, or current failure carrying retained package/acquisition-none | marker terminal failure branch |

The D-only success path must run the shipping helper binary through the exact
production runner and return a legal result. Device-mutating APIs are replaced
only behind a compile-time or handle-injected test backend that production
cannot select by environment variables alone.

### Installer and product

- Core install success plus virtual-display success;
- Core install success plus every virtual-display failure category, with Core
  retained and UI `Host installed / Virtual display unavailable`;
- virtual display not selected;
- repair success/failure without payload, ACL, firewall, bootstrap or UUID
  rewrite;
- interrupted helper and missing result show `setupResultUnavailable` without
  guessing a domain failure;
- uninstall success, already absent and every closed failure; Core payload is
  retained on VD cleanup failure and UI never claims removal;
- missing/unverified result and `persistenceUnavailable` stop before every Core
  payload/ACL/firewall/identity mutation and retain the native retry payload;
- production source scan proves zero legacy token/file/secondary-writer and
  removal-authority references;
- packaged manifest independently pins the setup helper and both schema bytes.

### Acceptance layers

1. source review: API ownership, state machine, schema and deletion gates;
2. offline artifact review: helper/package hashes, payload closure and all
   non-mutating tests;
3. one explicitly authorized elevated repair acceptance on the real residual
   exact node; uninstall remains separately authorized and is not exercised by
   that repair acceptance;
4. installed-product readback of independent Core and virtual-display states.

No earlier layer substitutes for the later one.
