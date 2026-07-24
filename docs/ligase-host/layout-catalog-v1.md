# Ligase Host local layout catalog H1

## Scope

H1 is a local metadata index over the already frozen
`layout-contract-v1` descriptor and explicit-binding types. It does not define,
store, transport, or claim availability of layout content.

The following remain outside H1:

- TouchKit element content and Android `SharedPreferences` keys;
- an executable/installed-content readiness claim;
- publication payloads, hashes, previews, downloads, Sync, or public routes;
- Desktop UI and game-identity mutation.

An `installedRevisions` item means that the exact immutable descriptor revision
is registered in this local index. It does not mean that a compatible content
artifact exists. No caller may use index presence as an artifact readiness
signal.

## Stored shape

The local file is strict JSON:

```json
{
  "schemaVersion": 1,
  "installedRevisions": [],
  "explicitBindings": []
}
```

`installedRevisions` contains exact `LayoutDescriptorV1` objects from
`layout-contract-v1`. `explicitBindings` items have this closed shape:

```json
{
  "instance": {
    "hostUniqueId": "canonical-lowercase-uuid-d",
    "appUuid": "canonical-lowercase-uuid-d"
  },
  "binding": {
    "layoutId": "canonical-lowercase-uuid-d",
    "revision": 1
  }
}
```

All objects reject unknown properties. Revisions use the existing safe integer
range `1..9007199254740991` and must be JSON integer tokens. Descriptor,
variant, compatibility, publication status, portable Steam identity, and UUID
rules are reused without modification from `layout-contract-v1`.

Duplicate `(layoutId, revision)` descriptors and duplicate exact instance
identities are invalid. A binding may intentionally point at a missing or
retired revision so that management UI can report `missing` or `retired`
instead of losing the Host-authored intent.

## Canonical persistence and queries

Writers sort descriptors by `(layoutId, revision)` and bindings by
`(hostUniqueId, appUuid)`. Descriptor identities and variants retain their
set semantics and are emitted in ordinal order.

Repository replacement uses a same-directory temporary file and atomic
replacement. The new file is parsed, strictly validated, and byte-compared
with the canonical requested snapshot. Any write or read-back failure restores
the previous file and returns `atomicWriteFailed`.

The application query returns:

- every registered descriptor revision with its frozen compatibility,
  variants, portable identities, and `draft|published|retired` status;
- every explicit binding with target status `available|missing|retired`.

The stable machine codes are:

`invalidJson`, `unknownField`, `invalidSchema`, `invalidDescriptor`,
`duplicateRevision`, `invalidBinding`, `duplicateBinding`, and
`atomicWriteFailed`.

Exception text and local paths are never machine results.

## Deferred content contract

Android TouchKit format version 2 remains a local legacy adapter. Its canvas,
dynamic descriptors, deleted-base list, element JSON, value types, and
preservation rules are not copied into this Host index.

The cross-platform content model, canonical numeric representation, ordering,
unknown-extension policy, size limits, runtime compatibility, and content hash
require a later docs-only Android/Host review. H1 deliberately has no content
field, opaque JSON escape hatch, schema fixture, importer, or publisher.
