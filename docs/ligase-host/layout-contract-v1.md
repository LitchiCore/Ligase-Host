# Ligase game identity and layout contract v1

Status: proposed for Host and Android review. Do not change an API or persisted
data file until both sides record `ACCEPT`.

This contract separates the identity of an application entry on one Host from
the portable identity of a game. Its immediate purpose is deterministic layout
matching. It does not define a layout marketplace, account, rating, download,
input editor, display policy, or device-management UI.

## Current Host identity audit

### Authoritative library

`ApplicationLibrary` owns `%LOCALAPPDATA%\Ligase Host\library.json`.
`LibraryItem.Id` is a GUID generated when an item is first added and persisted
with that item. Steam discovery deduplicates by `SteamAppId`; executable
addition deduplicates by the current absolute executable path. Those checks
avoid duplicates on one Host but do not create a portable identity.

Consequences:

- a retained item keeps its UUID through name, path, sort, publication, and
  streaming-setting changes;
- deleting and re-adding an item creates a new UUID;
- copying or independently adding the same game on another Host does not
  promise the same UUID;
- moving a custom executable can result in a different item;
- name and path are mutable display/launch metadata, not identity.

The two system entries are exceptions with frozen transport UUIDs:

| Kind | UUID |
|---|---|
| `desktop` | `78a25216-f239-45bd-b4aa-f41c814066e9` |
| `virtualDesktop` | `8902cb19-674a-403d-a587-41b092e900ba` |

These fixed UUIDs make the GameStream launch projection stable. They do not
mean that desktop entries are portable games, and neither system entry receives
a portable game identity.

### Sync projection

`LigaseSyncDocumentWriter` copies `LibraryItem.Id` unchanged to
`library.items[].id` and currently exposes `kind`, `name`, `steamAppId`,
`system`, publication, and timestamps. It intentionally omits executable path,
working directory, command, and Steam installation path.

Current `steamAppId` is reliable Steam manifest metadata, but its presence by
itself is not yet a versioned portable-identity contract. All other current
fields remain display, classification, or Host-local metadata.

### Apollo `apps.json` and GameStream applist

`ApolloAppsWriter` copies the same library UUID into each generated
`apps.json` entry. Virtual desktop is omitted because Apollo injects its own
entry with the frozen virtual-display UUID. Apollo then exposes that UUID in
`applist/App/UUID`.

Apollo calculates the numeric `applist/App/ID` by CRC32 over the application
name plus image identity, with application order used to resolve collisions.
Name, image, order, or collision context can therefore change the number. The
numeric ID exists only for the GameStream launch ABI and is never a library or
layout identity.

Apollo can generate a UUID for unmanaged legacy `apps.json` entries that lack
one. Ligase-generated entries already contain the Host library UUID. Directly
managed legacy Apollo entries are outside the Ligase product library and do not
gain a portable identity implicitly.

## Identity model

### Instance identity

The canonical instance key is:

```text
(normalizeUuid(serverinfo.uniqueid), normalizeUuid(sync.library.items[].id))
```

Machine names:

- `hostUniqueId`: normalized lowercase Host UUID;
- `appUuid`: normalized lowercase library item UUID.

This key identifies one application entry on one Host. It is authoritative for
launch mapping, Android local hide state, per-app streaming settings, and
Android device-local layout selection. A bare `appUuid` is not globally unique
product identity even when a system UUID happens to repeat across Hosts.

### Portable game identity

Machine form:

```json
{
  "provider": "steam",
  "id": "123"
}
```

Rules:

- `provider` is a lowercase ASCII value from a contract allowlist;
- `id` is a provider-defined canonical string, not a display name;
- equality is exact equality after provider-specific canonical validation;
- v1 allows only `steam`;
- a Steam ID is the unsigned decimal App ID with no sign, whitespace, or
  leading zeroes, and must be in `1..4294967295`;
- Epic, GOG, Microsoft Store, and other providers require a later contract
  revision backed by reliable Host metadata before use;
- `custom`, `executable`, filenames, product names, and paths are not portable
  providers.

Unknown and custom applications remain instance-only unless the Host user
explicitly binds that instance to a layout. An explicit binding does not invent
or publish a portable game identity.

The following are forbidden matching inputs:

- localized or fuzzy name matching;
- executable or working-directory string matching;
- GameStream numeric `appid`;
- Host `appUuid` across different `hostUniqueId` values;
- an inferred executable, directory, icon, or file hash;
- an unversioned store guess.

## Additive Sync v1 fields

The proposed fields are optional additions to each existing
`library.items[]` object:

```json
{
  "id": "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91",
  "kind": "steam",
  "name": "Example",
  "portableIdentity": {
    "provider": "steam",
    "id": "123"
  },
  "layoutBinding": {
    "layoutId": "0b7cd40f-64ae-4eac-845a-fb41dfed80d0",
    "revision": 7
  }
}
```

`portableIdentity` is Host-authored from verified provider metadata.
`layoutBinding` is a Host-authored explicit binding for this exact application
instance. Its absence means there is no Host override.

Compatibility defaults:

- missing or `null` `portableIdentity` means instance-only; clients must not
  derive one from `kind`, `name`, `steamAppId`, path, applist, or asset data;
- missing or `null` `layoutBinding` means no explicit binding;
- an unknown provider, malformed ID, or unknown identity shape on a Sync item
  makes only that item's portable identity unusable; an otherwise valid
  explicit `layoutBinding` can still resolve;
- old Android versions may ignore both fields and continue launch behavior;
- new Android versions must not treat missing fields from an older Sync v1
  snapshot as evidence that two games match.

Adding or changing either field is a Host library mutation: it increments the
library revision and item `updatedAt`. Host remains the sole writer.

## Layout descriptor v1

A layout revision is immutable. Updating content creates a higher `revision`
under the same stable `layoutId`.

```json
{
  "schemaVersion": 1,
  "layoutId": "0b7cd40f-64ae-4eac-845a-fb41dfed80d0",
  "revision": 7,
  "portableIdentities": [
    {
      "provider": "steam",
      "id": "123"
    }
  ],
  "compatibility": {
    "minClientContractVersion": 1,
    "minLayoutRuntimeVersion": 1
  },
  "publicationStatus": "published",
  "variants": [
    {
      "variantId": "1f648eb4-a4ba-4dfa-af21-b06a86f17423",
      "inputProfile": "touch",
      "deviceClasses": ["phone"],
      "orientations": ["portrait"]
    },
    {
      "variantId": "b59e6aa9-ebcd-4b2f-9962-b1a978aac136",
      "inputProfile": "touch",
      "deviceClasses": ["tablet"],
      "orientations": ["landscape"]
    }
  ]
}
```

Frozen v1 values:

- `layoutId`: lowercase UUID; stable across revisions;
- `revision`: positive integer, monotonically increasing per `layoutId`;
- `portableIdentities`: zero or more validated identities; an empty list is
  valid for a layout used only through explicit instance binding;
- `minClientContractVersion`: minimum supported game/layout contract;
- `minLayoutRuntimeVersion`: minimum renderer/input runtime required;
- `publicationStatus`: `draft`, `published`, or `retired`;
- `variants`: a non-empty array of device/input-specific variants;
- `variantId`: lowercase UUID, unique within a layout and stable across
  revisions while the variant retains the same device/input role;
- `inputProfile`: `touch`, `gamepad`, `keyboardMouse`, or `none`;
- `deviceClasses`: non-empty subset of `phone`, `tablet`;
- `orientations`: non-empty subset of `portrait`, `landscape`.

Variant content is uniquely addressed by
`(layoutId, revision, variantId)`. The binary/JSON artifact transport is outside
v1, but no producer or cache may address content by layout name, array index,
device dimensions, or `variantId` alone.

Arrays are sets: duplicates are invalid. Unknown enum values and incompatible
versions are unsupported and must not be silently coerced. A published layout
must not change content without increasing `revision`.

A duplicate `variantId` within one descriptor makes the entire descriptor
invalid, even if the duplicate entries otherwise contain identical fields.

For deterministic serialization and future signatures/caches, producers sort
`portableIdentities` by `(provider, id)`, `variants` by `variantId`,
`deviceClasses` in `phone, tablet` order, and `orientations` in
`portrait, landscape` order. Consumers compare these arrays as sets after
rejecting duplicates; input array order never changes matching.

Every required descriptor field must be present and valid. If any entry in
`portableIdentities` has an unknown provider, malformed canonical ID, or
duplicate canonical pair, the entire descriptor is invalid. Clients must not
interpret only the subset of identities they understand because that could
produce different matches on different clients.

`minClientContractVersion` refers to the parser and matching semantics in this
document. `minLayoutRuntimeVersion` refers to the monotonically increasing
TouchKit layout renderer/input runtime capability; it is not the existing
private layout `formatVersion=2`. A runtime version increase is backward
compatible with every lower runtime version. A breaking payload or matching
change requires a new `schemaVersion` and cross-platform review rather than
reusing the minimum field.

### Input profile semantics

The product input selection and layout resolver share these exact values:

| Value | User intent | Touch layout behavior |
|---|---|---|
| `touch` | No external input device; use the touchscreen | May automatically match and render an eligible touch layout |
| `gamepad` | Use an external/Bluetooth gamepad | Hide virtual controls by default; do not automatically match a touch layout |
| `keyboardMouse` | Use an external/Bluetooth keyboard and mouse | Do not render or automatically match a touch layout |
| `none` | Advanced/reserved state that disables client input | Do not render or match a layout |

Android displays the selected input mode as the primary state. For `gamepad`
and `keyboardMouse`, the subordinate state is the relevant attached-device
status. For `touch`, the subordinate state is the resolved layout and its
future edit action. The contract freezes those semantics, not the page layout.

The ordinary Android v1 UI has exactly three choices: external gamepad maps to
`gamepad`, external keyboard and mouse maps to `keyboardMouse`, and the
user-facing “no external device” choice maps to `touch`. The existing stored
value `touch` has that same meaning. `none` is not shown in the ordinary v1 UI,
must never represent “no external device”, and must never be chosen as a
fallback for a missing or unknown value. A descriptor variant's
`inputProfile` is required; missing or unknown values invalidate the
descriptor.

There is no `hybrid` value in v1. A future combined external-device and touch
overlay mode requires explicit product opt-in and contract review; clients must
not infer it from current TouchKit switches or profile settings. Existing
global profile/layout preferences are not game-identity or matching inputs.

## Matching and variant selection

For an application instance:

1. If Host Sync contains `layoutBinding`, resolve that exact
   `(layoutId, revision)`. If it is missing, malformed, incompatible, retired,
   or ambiguous, fail closed and report that the Host binding needs attention.
   Do not fall through to portable matching.
2. Otherwise, if the item has a valid `portableIdentity`, select only layouts
   containing the exact same provider and canonical ID.
3. If there is no valid identity or no exact candidate, return no match.

Before a candidate can be used, filter by client contract/runtime
compatibility and publication status. Automatic matching consumes only
`published` layouts and runs only when the selected input profile is `touch`.
An explicit Host binding may use a `draft` revision only when that exact
revision is already installed and compatible on the current Android device.
It never triggers a network download. A client without that local draft fails
closed and reports that the Host binding needs attention. Neither automatic nor
explicit resolution may use an unknown or `retired` revision.

If exact portable matching produces multiple unrelated `layoutId` values, no
automatic winner is chosen. The Host user must select one, creating an explicit
instance binding. A device-local preference must not resolve this cross-layout
conflict.

When portable matching leaves exactly one `layoutId`, evaluate its published
revisions from highest to lowest. A revision is compatible for the current
device only when the descriptor and runtime versions are supported and
filtering its variants by selected input profile, current device class, and
current orientation leaves at least one candidate. Select the highest such
revision. This permits a tablet or orientation to keep using an older published
revision when a newer revision contains no variant for that context; a client
must not select a revision whose content it cannot use. Android cannot pin an
older revision to override this rule. A Host `layoutBinding`, by contrast,
pins the exact `(layoutId, revision)` and changes only through a Host write that
also increments the library revision.

After `(layoutId, revision)` is resolved, variant selection is deterministic:

1. zero eligible variants means no eligible layout;
2. one eligible variant is selected;
3. with more than one eligible variant, use it only when the device-local
   `preferredVariantId` exactly identifies one candidate;
4. otherwise stop and ask the user to choose on that Android device; never
   choose by array order, name, dimensions, or an arbitrary score.

An ambiguous highest compatible revision does not cause a silent downgrade to
an older revision. The user selects a variant for the current revision, or the
Host publishes an unambiguous revision.

Android may store only a device-local preferred `variantId` and local
user-adjustments, together with the resolved `layoutId` and `revision`, under
`(hostUniqueId, appUuid)`. It cannot choose a different `layoutId`, pin an
older automatic revision, create or alter `portableIdentity`, override a Host
binding, or make an incompatible/unpublished variant eligible. The preference
participates only in step 3 above for that exact resolved tuple. If the Host
binding changes, automatic revision changes, or the variant becomes
ineligible, Android ignores or clears the preference and resolves again without
guessing.

## Ownership and migration

Host owns:

- the game collection and instance UUID;
- provider metadata and portable identity;
- explicit instance-to-layout binding;
- conflict resolution that changes shared identity/binding state.

Android owns only device-local presentation and eligible layout preference.
Android never writes game identity to Host.

Migration is additive:

1. Continue reading current library schema without either new field.
2. On a future Host implementation, populate `portableIdentity` only for
   verified Steam items; leave system and executable items null.
3. Do not auto-bind layouts during migration.
4. Preserve all current app UUIDs, GameStream launch mapping, streaming
   settings, visibility, sort state, and timestamps.
5. Introduce Host binding UI and Sync writes only after this contract is
   accepted and separately implemented/tested.

Existing Android TouchKit data migrates without identity guessing:

- preserve every old layout payload unchanged and assign a new device-local
  layout UUID mapping; the built-in/timestamp ID remains only a legacy source
  reference;
- only an association containing both a valid, normalizable
  `ComputerDetails.uuid` Host UUID and a valid app UUID may become an
  instance-only, pending-confirmation candidate;
- `managerBinder.uniqueId` is the Android GameStream client ID and must never be
  used or named as `hostUniqueId`;
- any association using numeric `appid`, `unknown_pc`, a missing UUID, an
  imported `sourceLayoutId`, a layout name, or a filename enters an unbound
  device-local library and never auto-attaches to a new instance;
- imported and built-in layouts have no game identity unless the Host later
  supplies a valid binding;
- the first migration copies data and does not delete the old SharedPreferences
  store. Cleanup requires a later, separately verified migration stage.

## Implementation boundary

This document freezes semantics only. The current stage must not:

- add the Sync fields or change `library.json` / `apps.json`;
- implement a layout lobby, downloader, editor, input surface, or account;
- change GameStream launch or numeric app ID behavior;
- modify device management, display policy, or desktop split layout.
