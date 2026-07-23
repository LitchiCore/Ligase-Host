"""Deterministically expand the frozen positive attended-pairing vector.

The cryptographic positive case remains hand-auditable in the fixture. This
script adds closed behavior/race cases without free-form references.
"""
from __future__ import annotations

import base64
import copy
import json
from pathlib import Path

from cryptography.hazmat.primitives.ciphers.aead import ChaCha20Poly1305

from attended_pairing_required_case_ids import REQUIRED_CASE_IDS

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "tests/fixtures/attended-pairing-v1-vectors.json"
REQUEST_ID = "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4"
SOURCE = {"address": "192.0.2.10", "scopeId": 0}
EMPTY_SOURCE = {"present": False, "address": "", "scopeId": 0}
STORED_SOURCE = {"present": True, **SOURCE}

CREATE_RULES = [
    "strictHttpOrder", "strictJsonFirstError", "canonicalUuid",
    "canonicalBase64url", "sourceNormalization",
]
AUTH_RULES = [
    "strictHttpOrder", "canonicalUuid", "canonicalBase64url",
    "sourceNormalization", "tokenSha256Authentication",
    "monotonicExpiryBeforeState", "requestLockLinearization",
    "generationGuard", "terminalCleanup",
]
ENVELOPE_RULES = [
    "strictHttpOrder", "strictJsonFirstError", "canonicalUuid",
    "canonicalBase64url", "sourceNormalization", "tokenSha256Authentication",
    "monotonicExpiryBeforeState", "requestLockLinearization",
    "envelopeReplayBeforeFirstUploadState", "generationGuard",
    "terminalCleanup",
]
LOOPBACK_LIST_RULES = ["strictHttpOrder", "sourceNormalization"]
LOOPBACK_ACTION_RULES = [
    "strictHttpOrder", "canonicalUuid", "monotonicExpiryBeforeState",
    "requestLockLinearization", "generationGuard", "terminalCleanup",
]
RACE_RULES = [
    "canonicalUuid", "canonicalBase64url", "sourceNormalization",
    "tokenSha256Authentication", "monotonicExpiryBeforeState",
    "requestLockLinearization", "envelopeReplayBeforeFirstUploadState",
    "generationGuard", "terminalCleanup",
]


def b64(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def unb64(value: str) -> bytes:
    return base64.urlsafe_b64decode(value + "=" * (-len(value) % 4))


def context(
    positive: dict,
    state: str = "pending",
    *,
    envelope: bool = False,
    held: bool = False,
    generation: int = 1,
    **changes,
) -> dict:
    live = state not in {"empty", "evicted"}
    ready = state == "approved" or (state == "pending" and envelope and held)
    paired = state == "paired"
    accepted_envelope = envelope or state in {"approved", "paired"}
    value = {
        "nowMonotonicMs": 1000,
        "deadlineMonotonicMs": 121000,
        "terminalCacheEvictsAtMs": 721000,
        "rateWindowStartMs": 0,
        "rateCount": 1,
        "rateLimit": 5,
        "liveFingerprintBusy": False,
        "requestPresent": live,
        "requestId": REQUEST_ID if live else "",
        "state": state,
        "readyForApproval": ready,
        "generation": generation,
        "canonicalCreateJcsBase64url": positive["createRequestJcsBase64url"] if live else "",
        "incomingSource": copy.deepcopy(SOURCE),
        "storedExactSource": copy.deepcopy(STORED_SOURCE if live else EMPTY_SOURCE),
        "incomingBearerBase64url": positive["requestTokenBase64url"] if live else "",
        "storedTokenHashBase64url": positive["requestTokenSha256Base64url"] if live else "",
        "tokenBytesPresent": state in {"pending", "approved"},
        "replayNonceBase64url": positive["envelopeNonceBase64url"] if accepted_envelope else "",
        "replayCanonicalEnvelopeHashBase64url": positive["envelopeCanonicalHashBase64url"] if accepted_envelope else "",
        "fullEncryptedEnvelopePresent": accepted_envelope and state == "pending",
        "validatedEnvelope": accepted_envelope,
        "heldGetservercert": held or state in {"approved", "paired"},
        "certificateFingerprintMatched": held or state in {"approved", "paired"},
        "readyEventEmitted": ready or paired,
        "pairedDeviceCommitted": paired,
    }
    if state in {"paired", "rejected", "cancelled", "expired", "failed"}:
        value["tokenBytesPresent"] = False
    value.update(changes)
    return value


def post(state: str, *, envelope: bool = False, generation: int = 1, **changes) -> dict:
    terminal = state in {"paired", "rejected", "cancelled", "expired", "failed"}
    ready = state == "approved"
    accepted_envelope = envelope or state in {"approved", "paired"}
    value = {
        "requestExists": state not in {"empty", "evicted"},
        "state": state,
        "readyForApproval": ready,
        "generation": generation,
        "heldPair": state == "approved",
        "tokenBytesPresent": state in {"pending", "approved"},
        "tokenHashPresent": state not in {"empty", "evicted"},
        "sourceVerifierPresent": state not in {"empty", "evicted"},
        "envelopeStorage": (
            "fullCiphertext" if accepted_envelope and state == "pending"
            else "digestOnly" if accepted_envelope
            else "none"
        ),
        "validatedEnvelope": accepted_envelope,
        "certificateFingerprintMatched": state in {"approved", "paired"},
        "domainEventCount": 1 if state in {"approved", "paired"} else 0,
        "pairedDeviceCommitted": state == "paired",
    }
    value.update(changes)
    return value


def cleanup(value: bool) -> dict:
    return {
        "pinCleared": value,
        "bearerTokenBytesCleared": value,
        "privateKeyCleared": value,
        "sharedSecretCleared": value,
        "pairingKeyCleared": value,
        "plaintextCleared": value,
        "fullCiphertextCleared": value,
        "pollCancelled": value,
        "heldOperationCancelled": value,
    }


def http(method: str, path: str, body: bytes = b"", headers=None) -> dict:
    return {
        "method": method,
        "path": path,
        "headers": headers or [],
        "bodyEncoding": "base64url",
        "body": b64(body),
    }


def compact_json(value: dict) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def error(code: str, **fields) -> bytes:
    return compact_json({"code": code, **fields})


def status_body(state: str, failure=None) -> bytes:
    return compact_json({
        "expiresAt": "2026-07-23T07:30:00Z",
        "failure": failure,
        "requestId": REQUEST_ID,
        "state": state,
    })


def expected(status: int, body: bytes = b"") -> dict:
    headers = [] if status == 204 else [{"name": "Content-Type", "value": "application/json"}]
    if status != 204 and not body:
        raise ValueError(f"HTTP {status} requires a JSON body")
    return {"status": status, "headers": headers, "bodyEncoding": "base64url", "body": b64(body)}


def behavior(
    case_id, operation, rules, ctx, request, status, state, response_body=b"",
    cleaned=False, **post_changes
):
    return {
        "id": case_id,
        "operation": operation,
        "validationRules": rules,
        "context": ctx,
        "input": request,
        "expectedHttp": expected(status, response_body),
        "expectedPostState": post(state, **post_changes),
        "expectedCleanupMetadata": cleanup(cleaned),
    }


def race_step(step, actor, action, phase, state, generation):
    return {
        "step": step, "actor": actor, "action": action, "phase": phase,
        "input": http("POST", "/internal/action"),
        "expectedPostState": post(
            state,
            envelope=state in {"approved", "paired", "cancelled"},
            generation=generation,
        ),
    }


def main() -> None:
    root = json.loads(FIXTURE.read_text(encoding="utf-8"))
    positive = next(case for case in root["cases"] if case["operation"] == "ligasePositive")
    token_header = [{"name": "Authorization", "value": f"Bearer {positive['requestTokenBase64url']}"}]
    envelope_path = f"/ligase/v1/pairing/requests/{REQUEST_ID}/envelope"
    status_path = f"/ligase/v1/pairing/requests/{REQUEST_ID}"

    envelope_ctx = context(
        positive,
        replayNonceBase64url=positive["envelopeNonceBase64url"],
        replayCanonicalEnvelopeHashBase64url=positive["envelopeCanonicalHashBase64url"],
        fullEncryptedEnvelopePresent=False,
        validatedEnvelope=True,
    )
    primitive_rules = ["canonicalBase64url"]
    cases = [
        {
            "id": "rfc7748-x25519-alice",
            "operation": "primitiveX25519",
            "validationRules": primitive_rules,
            "source": "RFC 7748 section 6.1",
            "privateKeyBase64url": b64(bytes.fromhex(
                "77076d0a7318a57d3c16c17251b26645"
                "df4c2f87ebc0992ab177fba51db92c2a")),
            "peerPublicKeyBase64url": b64(bytes.fromhex(
                "de9edb7d7b7dc1b4d35b61c2ece43537"
                "3f8343c85b78674dadfc7e146f882b4f")),
            "expectedPublicKeyBase64url": b64(bytes.fromhex(
                "8520f0098930a754748b7ddcb43ef75a"
                "0dbf3a0d26381af4eba4a98eaa9b4e6a")),
            "expectedSharedSecretBase64url": b64(bytes.fromhex(
                "4a5d9d5ba4ce2de1728e3bf480350f25"
                "e07e21c947d19e3376f09b3c1e161742")),
        },
        {
            "id": "rfc5869-hkdf-sha256-case-1",
            "operation": "primitiveHkdfSha256",
            "validationRules": primitive_rules,
            "source": "RFC 5869 appendix A.1 first 32 output bytes",
            "ikmBase64url": b64(bytes([0x0B]) * 22),
            "saltBase64url": b64(bytes.fromhex("000102030405060708090a0b0c")),
            "infoBase64url": b64(bytes.fromhex("f0f1f2f3f4f5f6f7f8f9")),
            "length": 32,
            "expectedOkmBase64url": b64(bytes.fromhex(
                "3cb25f25faacd57a90434f64d0362f2a"
                "2d2d0a90cf1a5a4c5db02d56ecc4c5bf")),
        },
        {
            "id": "ligase-envelope-chacha20-poly1305",
            "operation": "primitiveChaCha20Poly1305",
            "validationRules": primitive_rules,
            "source": "Ligase attended-pairing positive vector",
            "keyBase64url": positive["pairingKeyBase64url"],
            "nonceBase64url": positive["envelopeNonceBase64url"],
            "aadBase64url": positive["transcriptHashBase64url"],
            "plaintextBase64url": positive["envelopePlaintextJcsBase64url"],
            "expectedCiphertextAndTagBase64url": positive["envelopeCiphertextAndTagBase64url"],
        },
        {
            "id": "rfc8785-property-order",
            "operation": "primitiveJcs",
            "validationRules": ["strictJsonFirstError", "canonicalBase64url"],
            "source": "RFC 8785 deterministic property ordering subset",
            "inputUtf8Base64url": b64(
                b'{"response":{"version":1},"request":{"version":1}}'),
            "expectedJcsUtf8Base64url": b64(
                b'{"request":{"version":1},"response":{"version":1}}'),
        },
        positive,
    ]
    create_request = base64.urlsafe_b64decode(
        positive["createRequestJcsBase64url"] + "==")
    create_response = json.loads(base64.urlsafe_b64decode(
        positive["createResponseWithoutTokenJcsBase64url"] + "=="))
    create_response["requestToken"] = positive["requestTokenBase64url"]
    create_headers = [{"name": "Content-Type", "value": "application/json"}]
    envelope_bytes = base64.urlsafe_b64decode(
        positive["envelopeJcsBase64url"] + "==")

    # Create coverage: success/idempotency, identity/source/rate, and every
    # strict HTTP/JSON validation class frozen by the wire contract.
    create_specs = [
        ("create-new-201", context(positive, "empty"),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         201, "pending", compact_json(create_response)),
        ("create-exact-live-200", context(positive),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         200, "pending", compact_json(create_response)),
        ("create-different-source-conflict", context(
            positive, incomingSource={"address": "192.0.2.11", "scopeId": 0}),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         409, "pending", error("requestIdConflict")),
        ("create-different-object-conflict", context(positive),
         http("POST", "/ligase/v1/pairing/requests",
              create_request.replace(b'"Phone"', b'"Tablet"'), create_headers),
         409, "pending", error("requestIdConflict")),
        ("create-expired-410", context(positive, "expired"),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         410, "expired", error("requestExpired")),
        ("create-terminal-409", context(positive, "rejected"),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         409, "rejected", error("requestIdConflict")),
        ("create-rate-limited", context(positive, "empty", rateCount=5),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         429, "empty", error("rateLimited")),
        ("create-live-fingerprint-busy", context(
            positive, "empty", liveFingerprintBusy=True),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         409, "empty", error("clientCertificateBusy")),
        ("create-ipv4-mapped-equivalent", context(
            positive, incomingSource={"address": "::ffff:192.0.2.10", "scopeId": 0}),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         200, "pending", compact_json(create_response)),
        ("create-ipv6-source-mismatch", context(
            positive, incomingSource={"address": "2001:db8::2", "scopeId": 0}),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         409, "pending", error("requestIdConflict")),
        ("create-link-local-scope-mismatch", context(
            positive,
            incomingSource={"address": "fe80::1", "scopeId": 4},
            storedExactSource={"present": True, "address": "fe80::1", "scopeId": 3}),
         http("POST", "/ligase/v1/pairing/requests", create_request, create_headers),
         409, "pending", error("requestIdConflict")),
    ]
    validation_specs = [
        ("content-type", [], 415, error("unsupportedMediaType")),
        ("content-encoding", create_headers + [{"name": "Content-Encoding", "value": "identity"}],
         415, error("unsupportedMediaType", detail="contentEncodingNotSupported")),
        ("empty-body", create_headers, 400,
         error("invalidRequest", detail="invalidValue", path="")),
        ("oversize-body", create_headers, 400,
         error("invalidRequest", detail="invalidValue", path="")),
        ("invalid-utf8", create_headers, 400,
         error("invalidRequest", detail="invalidJson")),
        ("invalid-json", create_headers, 400,
         error("invalidRequest", detail="invalidJson")),
        ("duplicate-field", create_headers, 400,
         error("invalidRequest", detail="duplicateField", path="/version")),
        ("unknown-field", create_headers, 400,
         error("invalidRequest", detail="unknownField", path="/extra")),
        ("missing-field", create_headers, 400,
         error("invalidRequest", detail="missingField", path="/version")),
        ("invalid-type", create_headers, 400,
         error("invalidRequest", detail="invalidType", path="/version")),
        ("invalid-value", create_headers, 400,
         error("invalidRequest", detail="invalidValue", path="/requestId")),
        ("pointer-first-error", create_headers, 400,
         error("invalidRequest", detail="unknownField", path="/a")),
    ]
    bodies = {
        "content-type": create_request,
        "content-encoding": create_request,
        "empty-body": b"",
        "oversize-body": b"x" * 4097,
        "invalid-utf8": b"\xff",
        "invalid-json": b'{"version":',
        "duplicate-field": b'{"version":1,"version":1}',
        "unknown-field": b'{"extra":1}',
        "missing-field": b"{}",
        "invalid-type": b'{"version":"1"}',
        "invalid-value": b'{"requestId":"BAD","version":1}',
        "pointer-first-error": b'{"z":1,"a":1}',
    }
    for suffix, headers, status_code, response in validation_specs:
        create_specs.append((
            f"create-{suffix}", context(positive, "empty"),
            http("POST", "/ligase/v1/pairing/requests", bodies[suffix], headers),
            status_code, "empty", response,
        ))
    for case_id, ctx, request, status_code, state, response in create_specs:
        cases.append(behavior(
            case_id, "create", CREATE_RULES, ctx, request, status_code, state,
            response, cleaned=status_code >= 400,
            generation=ctx["generation"],
            envelope=ctx["validatedEnvelope"],
        ))

    # Envelope replay/auth/state matrix.
    ready_ctx = context(positive, envelope=True, held=True)
    invalid_plaintext_ciphertext = ChaCha20Poly1305(
        unb64(positive["pairingKeyBase64url"])
    ).encrypt(
        unb64(positive["envelopeNonceBase64url"]),
        b'{"legacyPin":"12x4"}',
        unb64(positive["transcriptHashBase64url"]),
    )
    invalid_plaintext_envelope = compact_json({
        "ciphertext": b64(invalid_plaintext_ciphertext),
        "nonce": positive["envelopeNonceBase64url"],
    })
    envelope_cases = [
        ("envelope-first-204", context(positive), 204, "pending", envelope_bytes, b""),
        ("envelope-exact-replay-pending-204", context(positive, envelope=True), 204, "pending", envelope_bytes, b""),
        ("envelope-nonce-reuse-409", context(positive, envelope=True), 409, "pending",
         invalid_plaintext_envelope,
         error("nonceReuse")),
        ("envelope-conflict-409", context(positive, envelope=True), 409, "pending",
         compact_json({
             "ciphertext": positive["envelopeCiphertextAndTagBase64url"],
             "nonce": "AQECAwQFBgcICQoL",
         }),
         error("envelopeConflict")),
        ("envelope-expired-priority-410", context(positive, "expired", envelope=True), 410, "expired",
         b"not-json", error("requestExpired")),
        ("envelope-bad-token-401", context(positive, incomingBearerBase64url=b64(bytes(32))), 401,
         "pending", envelope_bytes, error("invalidRequestToken")),
        ("envelope-source-mismatch-401", context(
            positive, incomingSource={"address": "192.0.2.99", "scopeId": 0}), 401,
         "pending", envelope_bytes, error("invalidRequestToken")),
        ("envelope-invalid-aead-400", context(positive), 400, "pending",
         b'{"ciphertext":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","nonce":"AAECAwQFBgcICQoL"}',
         error("invalidEnvelope", detail="authenticationFailed")),
        ("envelope-invalid-plaintext-400", context(positive), 400, "pending",
         invalid_plaintext_envelope, error("invalidEnvelope", detail="invalidPlaintext")),
        ("envelope-terminal-no-metadata-409", context(positive, "rejected"), 409, "rejected",
         envelope_bytes, error("invalidState", currentState="rejected")),
        ("envelope-media-415", context(positive), 415, "pending", envelope_bytes,
         error("unsupportedMediaType")),
        ("envelope-empty-body-400", context(positive), 400, "pending", b"",
         error("invalidRequest", detail="invalidValue", path="")),
        ("envelope-content-encoding-415", context(positive), 415, "pending",
         envelope_bytes,
         error("unsupportedMediaType", detail="contentEncodingNotSupported")),
        ("envelope-oversize-body-400", context(positive), 400, "pending",
         b"x" * 1025, error("invalidRequest", detail="invalidValue", path="")),
        ("envelope-unknown-field-400", context(positive), 400, "pending",
         b'{"extra":1}', error("invalidRequest", detail="unknownField", path="/extra")),
    ]
    for terminal in ["approved", "paired", "rejected", "cancelled", "failed"]:
        envelope_cases.append((
            f"envelope-exact-replay-{terminal}-204",
            context(positive, terminal, envelope=True, held=terminal == "approved"),
            204, terminal, envelope_bytes, b"",
        ))
    for case_id, ctx, status_code, state, body, response in envelope_cases:
        headers = token_header + (
            create_headers if case_id != "envelope-media-415" else [])
        if case_id == "envelope-content-encoding-415":
            headers += [{"name": "Content-Encoding", "value": "identity"}]
        cases.append(behavior(
            case_id, "envelope", ENVELOPE_RULES, ctx,
            http("PUT", envelope_path, body, headers),
            status_code, state, response,
            cleaned=status_code in {400, 401, 410},
            generation=ctx["generation"],
            envelope=ctx["validatedEnvelope"] or status_code == 204,
        ))

    # Public status and DELETE matrix.
    for state in ["pending", "approved", "paired", "rejected", "cancelled", "expired", "failed"]:
        ctx = context(
            positive, state,
            envelope=state in {"approved", "paired"},
            held=state == "approved",
            generation=2 if state != "pending" else 1,
        )
        cases.append(behavior(
            f"status-{state}-200", "status", AUTH_RULES, ctx,
            http("GET", status_path, headers=token_header), 200, state,
            status_body(state, "legacyPairingFailed" if state == "failed" else None),
            generation=ctx["generation"], envelope=ctx["validatedEnvelope"],
        ))
    cases.append(behavior(
        "status-materializes-expiry-200", "status", AUTH_RULES,
        context(positive, nowMonotonicMs=121000),
        http("GET", status_path, headers=token_header), 200, "expired",
        status_body("expired"), cleaned=True, generation=2))
    cases += [
        behavior("status-unknown-id-404", "status", AUTH_RULES, context(positive, "empty"),
                 http("GET", status_path, headers=token_header), 404, "empty",
                 error("requestNotFound"), cleaned=True),
        behavior("status-noncanonical-id-404", "status", AUTH_RULES, context(positive, "empty"),
                 http("GET", "/ligase/v1/pairing/requests/BAD", headers=token_header),
                 404, "empty", error("requestNotFound"), cleaned=True),
        behavior("status-bad-token-401", "status", AUTH_RULES,
                 context(positive, incomingBearerBase64url=b64(bytes(32))),
                 http("GET", status_path, headers=token_header), 401, "pending",
                 error("invalidRequestToken"), cleaned=True),
        behavior("status-source-mismatch-401", "status", AUTH_RULES,
                 context(positive, incomingSource={"address": "192.0.2.88", "scopeId": 0}),
                 http("GET", status_path, headers=token_header), 401, "pending",
                 error("invalidRequestToken"), cleaned=True),
        behavior("status-cache-evicted-404", "status", AUTH_RULES, context(positive, "evicted"),
                 http("GET", status_path, headers=token_header), 404, "evicted",
                 error("requestNotFound"), cleaned=True),
    ]
    for state in ["pending", "approved", "cancelled", "rejected", "failed", "paired", "expired"]:
        initial_generation = 2 if state != "pending" else 1
        ctx = context(
            positive, state,
            envelope=state in {"approved", "paired"},
            held=state == "approved",
            generation=initial_generation,
        )
        target = "cancelled" if state in {"pending", "approved"} else state
        target_generation = initial_generation + 1 if target != state else initial_generation
        cases.append(behavior(
            f"delete-{state}-204", "cancel", AUTH_RULES, ctx,
            http("DELETE", status_path, headers=token_header), 204, target, b"",
            cleaned=target == "cancelled",
            generation=target_generation,
            envelope=ctx["validatedEnvelope"],
        ))
    delete_errors = [
        ("delete-evicted-404", context(positive, "evicted"), 404, error("requestNotFound")),
        ("delete-bad-token-401", context(positive, incomingBearerBase64url=b64(bytes(32))),
         401, error("invalidRequestToken")),
        ("delete-source-mismatch-401", context(
            positive, incomingSource={"address": "192.0.2.77", "scopeId": 0}),
         401, error("invalidRequestToken")),
        ("delete-noncanonical-id-404", context(positive, "empty"), 404, error("requestNotFound")),
        ("delete-unknown-id-404", context(positive, "empty"), 404, error("requestNotFound")),
        ("delete-nonempty-body-400", context(positive), 400,
         error("invalidRequest", detail="unexpectedBody")),
    ]
    for case_id, ctx, status_code, response in delete_errors:
        path = "/ligase/v1/pairing/requests/BAD" if "noncanonical" in case_id else status_path
        body = b"x" if "nonempty" in case_id else b""
        cases.append(behavior(
            case_id, "cancel", AUTH_RULES, ctx,
            http("DELETE", path, body, token_header), status_code, ctx["state"],
            response, cleaned=status_code in {401, 404},
            generation=ctx["generation"], envelope=ctx["validatedEnvelope"],
        ))

    # Loopback list/actions with exact JSON.
    pending_item = {
        "createdAt": "2026-07-23T07:28:00Z",
        "device": {"name": "Phone", "platform": "android"},
        "expiresAt": "2026-07-23T07:30:00Z",
        "readyForApproval": False,
        "requestId": REQUEST_ID,
        "safetyCode": positive["safetyCode"],
        "sourceAddress": SOURCE,
        "state": "pending",
    }
    list_path = "/ligase/v1/pairing/requests"
    for suffix, requests in [
        ("empty", []),
        ("ready-false", [pending_item]),
        ("ready-true", [{**pending_item, "readyForApproval": True}]),
        ("multi-sorted", [
            pending_item,
            {**pending_item, "requestId": "adbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4"},
        ]),
    ]:
        cases.append(behavior(
            f"loopback-list-{suffix}-200", "loopbackList", LOOPBACK_LIST_RULES,
            context(positive), http("GET", list_path), 200, "pending",
            compact_json({"requests": requests, "version": 1}),
        ))
    cases.append(behavior(
        "loopback-list-nonloopback-404", "loopbackList", LOOPBACK_LIST_RULES,
        context(positive), http("GET", list_path), 404, "pending",
        error("requestNotFound")))
    cases.append(behavior(
        "loopback-list-nonempty-body-400", "loopbackList", LOOPBACK_LIST_RULES,
        context(positive), http("GET", list_path, b"x"), 400, "pending",
        error("invalidRequest", detail="unexpectedBody")))
    allow_cases = [
        ("allow-not-ready-409", context(positive), 409, "pending",
         error("invalidState", currentState="pending", detail="notReadyForApproval"), 1),
        ("allow-ready-202", ready_ctx, 202, "approved", status_body("approved"), 2),
        ("allow-repeat-approved-200", context(positive, "approved", envelope=True, held=True, generation=2),
         200, "approved", status_body("approved"), 2),
        ("allow-paired-200", context(positive, "paired", envelope=True, generation=3),
         200, "paired", status_body("paired"), 3),
        ("allow-terminal-409", context(positive, "rejected", generation=2),
         409, "rejected", error("invalidState", currentState="rejected"), 2),
    ]
    for case_id, ctx, status_code, state, response, generation in allow_cases:
        cases.append(behavior(
            case_id, "loopbackAllow", LOOPBACK_ACTION_RULES, ctx,
            http("POST", f"{status_path}/allow"), status_code, state, response,
            generation=generation, envelope=ctx["validatedEnvelope"],
        ))
    reject_cases = [
        ("reject-pending-200", context(positive), "rejected", 2),
        ("reject-approved-200", context(positive, "approved", envelope=True, held=True, generation=2),
         "rejected", 3),
        ("reject-repeat-200", context(positive, "rejected", generation=2), "rejected", 2),
    ]
    for case_id, ctx, state, generation in reject_cases:
        cases.append(behavior(
            case_id, "loopbackReject", LOOPBACK_ACTION_RULES, ctx,
            http("POST", f"{status_path}/reject"), 200, state, status_body(state),
            cleaned=True, generation=generation, envelope=ctx["validatedEnvelope"],
        ))
    cases.append(behavior(
        "reject-invalid-terminal-409", "loopbackReject", LOOPBACK_ACTION_RULES,
        context(positive, "paired", envelope=True, generation=3),
        http("POST", f"{status_path}/reject"), 409, "paired",
        error("invalidState", currentState="paired"), generation=3, envelope=True))
    cases.append(behavior(
        "allow-nonempty-body-400", "loopbackAllow", LOOPBACK_ACTION_RULES,
        ready_ctx, http("POST", f"{status_path}/allow", b"x"),
        400, "pending", error("invalidRequest", detail="unexpectedBody"),
        generation=ready_ctx["generation"], envelope=True))
    cases.append(behavior(
        "reject-nonempty-body-400", "loopbackReject", LOOPBACK_ACTION_RULES,
        context(positive), http("POST", f"{status_path}/reject", b"x"),
        400, "pending", error("invalidRequest", detail="unexpectedBody")))

    # Deterministic races and getservercert binding/generation seams.
    race_specs = [
        ("race-cancel-wins-pair", "cancel", "commitPaired", "cancelled", 3, 3),
        ("race-pair-wins-cancel", "commitPaired", "cancel", "paired", 3, 3),
        ("race-reject-wins-pair", "reject", "commitPaired", "rejected", 3, 3),
        ("race-pair-wins-reject", "commitPaired", "reject", "paired", 3, 3),
        ("race-allow-wins-reject", "allow", "reject", "rejected", 2, 3),
        ("race-reject-wins-allow", "reject", "allow", "rejected", 2, 2),
        ("race-allow-wins-cancel", "allow", "cancel", "cancelled", 2, 3),
        ("race-cancel-wins-allow", "cancel", "allow", "cancelled", 2, 2),
    ]
    for case_id, first, second, final_state, first_gen, final_gen in race_specs:
        initial = context(
            positive, "approved", envelope=True, held=True, generation=2
        ) if "pair" in case_id else ready_ctx
        cases.append({
            "id": case_id,
            "operation": "race",
            "validationRules": RACE_RULES,
            "context": initial,
            "schedule": [
                race_step(1, "publicClient" if first == "cancel" else "loopbackUi",
                          first, "underLock",
                          "paired" if first == "commitPaired" else
                          "approved" if first == "allow" else
                          "cancelled" if first == "cancel" else "rejected",
                          first_gen),
                race_step(2, "nvhttpAdapter" if second == "commitPaired" else "loopbackUi",
                          second, "underLock", final_state, final_gen),
            ],
            "expectedPostState": post(
                final_state, envelope=initial["validatedEnvelope"], generation=final_gen),
            "expectedCleanupMetadata": cleanup(
                final_state in {"cancelled", "rejected"}),
        })
    for suffix, action, final_state in [
        ("success", "bindHeldGetservercert", "pending"),
        ("family-mismatch", "transportFailure", "failed"),
        ("source-mismatch", "transportFailure", "failed"),
        ("certificate-mismatch", "transportFailure", "failed"),
        ("request-id-mismatch", "transportFailure", "failed"),
        ("terminal-stale", "bindHeldGetservercert", "cancelled"),
        ("generation-stale", "bindHeldGetservercert", "cancelled"),
    ]:
        initial = context(positive, envelope=True)
        if "stale" in suffix:
            initial = context(positive, "cancelled", envelope=True, generation=2)
        final_generation = initial["generation"] + (1 if final_state == "failed" else 0)
        cases.append({
            "id": f"getservercert-{suffix}",
            "operation": "race",
            "validationRules": RACE_RULES,
            "context": initial,
            "schedule": [
                race_step(1, "nvhttpAdapter", action, "underLock",
                          final_state, final_generation),
                race_step(2, "coordinator", "materializeExpiry", "afterCommit",
                          final_state, final_generation),
            ],
            "expectedPostState": post(
                final_state,
                envelope=True,
                generation=final_generation,
                **({
                    "readyForApproval": True,
                    "heldPair": True,
                    "certificateFingerprintMatched": True,
                    "domainEventCount": 1,
                } if suffix == "success" else {})),
            "expectedCleanupMetadata": cleanup(final_state != "pending"),
        })

    # Android DELETE + exactly-one authenticated status probe outcomes.
    android_outcomes = [
        ("cancelled", "cancelled", 200, None),
        ("paired", "paired", 200, None),
        ("rejected", "rejected", 200, None),
        ("expired", "expired", 200, None),
        ("failed", "failed", 200, None),
        ("unauthorized", "unknown", 401, None),
        ("not-found", "unknown", 404, None),
        ("gone", "unknown", 410, None),
        ("transport-timeout", "unknown", 0, "timeout"),
    ]
    for suffix, outcome, status_code, transport_error in android_outcomes:
        terminal = outcome if outcome != "unknown" else "approved"
        generation = 3 if outcome in {
            "cancelled", "paired", "rejected", "expired", "failed"
        } else 2
        if outcome == "cancelled":
            initial = context(
                positive, "approved", envelope=True, held=True, generation=2)
        elif outcome in {"paired", "rejected", "expired", "failed"}:
            initial = context(
                positive,
                outcome,
                envelope=outcome == "paired",
                held=False,
                generation=3,
            )
        elif status_code == 404:
            initial = context(positive, "evicted", generation=0)
            terminal = "evicted"
            generation = 0
        elif status_code == 410:
            initial = context(positive, "expired", generation=3)
            terminal = "expired"
            generation = 3
        else:
            initial = context(
                positive, "approved", envelope=True, held=True, generation=2)
        probe = (
            {"kind": "transportError", "transportError": transport_error}
            if transport_error else
            {
                "kind": "http",
                **expected(
                    status_code,
                    status_body(
                        terminal,
                        "legacyPairingFailed" if terminal == "failed" else None,
                    ) if status_code == 200 else
                    error(
                        "invalidRequestToken" if status_code == 401 else
                        "requestNotFound" if status_code == 404 else
                        "requestExpired")),
            }
        )
        cases.append({
            "id": f"android-cancel-probe-{suffix}",
            "operation": "androidCancel",
            "validationRules": RACE_RULES,
            "context": initial,
            "deleteInput": http("DELETE", status_path, headers=token_header),
            "deleteExpectedHttp": expected(204),
            "statusProbeInput": http("GET", status_path, headers=token_header),
            "statusProbeExpected": probe,
            "expectedAndroidOutcome": outcome,
            "expectedPostState": post(
                terminal,
                envelope=initial["validatedEnvelope"],
                generation=generation),
            "expectedCleanupMetadata": cleanup(True),
        })

    cases.sort(key=lambda item: item["id"].encode("utf-8"))
    output = {
        "schemaVersion": root["schemaVersion"],
        "draft": root["draft"],
        "coverageManifest": {
            "version": 1,
            "requiredIds": list(REQUIRED_CASE_IDS),
        },
        "cases": cases,
    }
    FIXTURE.write_text(
        json.dumps(output, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


if __name__ == "__main__":
    main()
