# Layout contract v1 normative vectors

The normative cross-platform vector file is:

```text
tests/fixtures/layout-contract-v1-vectors.json
```

Its SHA-256 over the exact file bytes is:

```text
b8022f21d37481bc54a869a6be8c70b994cb94266796634356808c8dbe859fcd
```

Line endings, whitespace, object property order, fixture order, and case order
are part of those bytes. Android must copy the file without reformatting and
verify the same SHA before using it as a parity gate.

## Vector file schema

Top-level object:

```text
schemaVersion: integer, exactly 1
descriptorFixtures: object<string, LayoutDescriptorV1>
normalizationCases: NormalizationCase[]
resolutionCases: ResolutionCase[]
```

`descriptorFixtures` keys are unique fixture names. A resolution case has
exactly one of these forms:

```text
{
  id: string,
  input: LayoutResolutionRequestJson,
  expected: CanonicalResult
}
```

or:

```text
{
  id: string,
  descriptorRefs: string[],
  request: {
    hostUniqueId: string,
    appUuid: string,
    portableIdentity?: PortableGameIdentityV1,
    layoutBinding?: LayoutBindingV1,
    context: LayoutResolutionContextJson
  },
  expected: CanonicalResult
}
```

The second form expands to `LayoutResolutionRequestJson` by adding
`schemaVersion: 1`, nesting the two UUIDs under `instance`, and replacing
`descriptorRefs` with deep copies of the referenced fixtures in the listed
order. Fixture references must exist. Repeated references intentionally create
repeated descriptors.

Normalization cases have:

```text
id: string
kind: "uuid" | "portable"
input: string | PortableGameIdentityV1
expected: { valid: boolean, normalized?: string, provider?: string, id?: string }
```

The production request JSON shape is:

```text
schemaVersion: integer, exactly 1
instance: {
  hostUniqueId: string,
  appUuid: string
}
portableIdentity?: PortableGameIdentityV1 | null
layoutBinding?: LayoutBindingV1 | null
context: {
  clientContractVersion: positive integer
  layoutRuntimeVersion: positive integer
  inputProfile: "touch" | "gamepad" | "keyboardMouse" | "none"
  deviceClass: "phone" | "tablet"
  orientation: "portrait" | "landscape"
  installedDrafts?: Array<{ layoutId: string, revision: SafeRevision }>
  preferredVariant?: {
    layoutId: string,
    revision: SafeRevision,
    variantId: string
  } | null
}
descriptors: LayoutDescriptorV1[]
```

Every object in the production request is closed: unknown properties return
`unknownField`. The detail is the lexicographically smallest RFC 6901 JSON
Pointer by Unicode code point. Structure/unknown-field validation precedes
semantic validation.

`SafeRevision` is a JSON integer token in
`1..9007199254740991`. Decimal and exponent spellings are invalid even if their
mathematical value is integral.

`installedDrafts` has set semantics. Duplicate exact pairs are accepted and
deduplicated. Contract UUIDs in descriptors, bindings, preferences, and draft
pairs must already be lowercase canonical D-format UUIDs. Runtime instance
UUIDs accept either case and normalize to lowercase.

## Canonical result

JSON properties occur in this order when present:

```text
code, source, layoutId, revision, variantId, detail
```

Only `resolved` contains `source`, `layoutId`, `revision`, and `variantId`.
`source` is `binding` or `portable`. It has no `detail`.

Errors contain only `code`, except:

- `unknownField` requires `detail` containing the RFC 6901 pointer;
- duplicate `(layoutId, revision)` descriptors return `invalidDescriptor`
  with `detail: "duplicateRevision"`.

Localized messages, exception text, catalog IDs, and warnings never enter the
canonical result.

Stable result-code allowlist:

```text
resolved
invalidJson
invalidSchema
unknownField
invalidRevision
invalidInstanceIdentity
invalidContext
invalidDescriptor
invalidBinding
bindingNotFound
bindingRetired
bindingDraftNotInstalled
incompatibleBinding
invalidSyncPortableIdentity
inputProfileDoesNotAutoMatch
noMatch
noCompatibleRevision
noEligibleVariant
layoutConflict
needsVariantSelection
```

## Stable validation order

1. JSON syntax, top-level schema, unknown fields, and revision token/range;
2. instance Host/app UUIDs;
3. the complete descriptor catalog, sorted by raw `(layoutId, revision)`;
4. context validity;
5. explicit binding path when a binding exists;
6. otherwise portable identity validity;
7. non-touch automatic-matching gate;
8. portable catalog resolution.

Any invalid descriptor fails the complete catalog even if the active binding
or input profile would not use it. A valid explicit binding ignores malformed
or unsupported portable identity without adding a warning to the result.

The binding path order is:

```text
invalidBinding
bindingNotFound
bindingRetired
bindingDraftNotInstalled
incompatibleBinding
noEligibleVariant | needsVariantSelection | resolved
```

The portable path keeps these outcomes mutually exclusive:

- `noMatch`: no portable identity, or no exact published identity match;
- `layoutConflict`: exact published matches contain multiple layout IDs;
- `noCompatibleRevision`: one layout ID matches, but no published revision
  passes descriptor/client/runtime compatibility;
- `noEligibleVariant`: compatible published revisions exist, but none has a
  variant for the current input/device/orientation;
- `needsVariantSelection`: the selected highest eligible revision has multiple
  variants and no exact local preference;
- `resolved`: one variant is selected.
