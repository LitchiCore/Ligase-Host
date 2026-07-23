"""Validate attended-pairing vectors beyond JSON Schema expressiveness."""
from __future__ import annotations

import base64
import ipaddress
import json
import re
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from attended_pairing_required_case_ids import REQUIRED_CASE_IDS

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "tests/fixtures/attended-pairing-v1-vectors.json"
ROOT_PROPERTY_ORDER = ["schemaVersion", "draft", "coverageManifest", "cases"]
RACE_ACTOR_ACTION_PHASE = {
    ("publicClient", "cancel", "underLock"),
    ("loopbackUi", "allow", "underLock"),
    ("loopbackUi", "reject", "underLock"),
    ("nvhttpAdapter", "bindHeldGetservercert", "underLock"),
    ("nvhttpAdapter", "commitPaired", "underLock"),
    ("coordinator", "materializeExpiry", "underLock"),
    ("coordinator", "transportFailure", "underLock"),
}
SECRET_FIELDS = (
    "pin", "bearerTokenBytes", "privateKey", "sharedSecret",
    "pairingKey", "nonce", "plaintext", "fullCiphertext",
)
OPERATION_FIELDS = ("poll", "heldOperation")
CANONICAL_UUID_RE = re.compile(
    r"^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-"
    r"[89ab][0-9a-f]{3}-[0-9a-f]{12}$"
)
RFC3339_UTC_RE = re.compile(
    r"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$"
)
SAFETY_CODE_RE = re.compile(r"^[A-Z2-7]{4}-[A-Z2-7]{4}$")
ERROR_CODES_BY_STATUS = {
    400: {"invalidRequest", "invalidEnvelope"},
    401: {"invalidRequestToken"},
    404: {"requestNotFound"},
    409: {
        "requestIdConflict", "clientCertificateBusy", "invalidState",
        "nonceReuse", "envelopeConflict",
    },
    410: {"requestExpired"},
    415: {"unsupportedMediaType"},
    429: {"rateLimited"},
    503: {"pairingUnavailable"},
}
INVALID_REQUEST_DETAILS = {
    "invalidJson", "duplicateField", "unknownField", "missingField",
    "invalidType", "invalidValue", "unexpectedBody",
}
INVALID_REQUEST_PATH_DETAILS = {
    "duplicateField", "unknownField", "missingField", "invalidType", "invalidValue",
}


def decode(value: str) -> bytes:
    return base64.urlsafe_b64decode(value + "=" * (-len(value) % 4))


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def expect_rejected(label: str, action) -> None:
    try:
        action()
    except ValueError:
        return
    raise ValueError(f"self-test accepted invalid {label}")


def validate_race_tuple(case_id: str, step: dict) -> None:
    require(
        (step["actor"], step["action"], step["phase"]) in RACE_ACTOR_ACTION_PHASE,
        f"{case_id}: invalid actor/action/phase tuple",
    )


def validate_owner_transition(case_id: str, previous: dict, current: dict) -> None:
    for field in SECRET_FIELDS:
        allowed = {
            "present": {"present", "cleared"},
            "cleared": {"cleared"},
            "notOwned": {"notOwned"},
        }[previous[field]]
        require(
            current[field] in allowed,
            f"{case_id}: illegal secret owner transition for {field}",
        )
    for field in OPERATION_FIELDS:
        allowed = {
            "active": {"active", "cancelled", "completed"},
            "cancelled": {"cancelled"},
            "completed": {"completed"},
            "notOwned": {"notOwned"},
        }[previous[field]]
        require(
            current[field] in allowed,
            f"{case_id}: illegal operation owner transition for {field}",
        )


def validate_utc_second(value: str) -> datetime:
    require(RFC3339_UTC_RE.fullmatch(value) is not None, "invalid UTC second shape")
    try:
        parsed = datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(
            tzinfo=timezone.utc
        )
    except ValueError as error:
        raise ValueError("invalid UTC Gregorian time") from error
    require(
        parsed.strftime("%Y-%m-%dT%H:%M:%SZ") == value,
        "UTC second does not round-trip",
    )
    return parsed


def validate_status_authority(case_id: str, value: dict, present_field: str) -> None:
    present = value[present_field] and value["state"] not in {"empty", "evicted"}
    expires_at = value["statusExpiresAt"]
    if present:
        validate_utc_second(expires_at)
    else:
        require(expires_at == "", f"{case_id}: absent request has expiresAt")
    failure = value["failure"]
    if value["state"] == "failed":
        require(
            failure in {
                "certificateMismatch", "pairSessionMissing",
                "cryptoFailure", "legacyPairingFailed",
            },
            f"{case_id}: failed state lacks failure",
        )
    else:
        require(failure is None, f"{case_id}: non-failed state has failure")


def validate_loopback_source(case_id: str, value: dict) -> None:
    require(isinstance(value, dict), f"{case_id}: loopback source is not object")
    require("address" in value, f"{case_id}: loopback source lacks address")
    address = value["address"]
    require(isinstance(address, str), f"{case_id}: loopback address is not string")
    try:
        parsed = ipaddress.ip_address(address)
    except ValueError as error:
        raise ValueError(f"{case_id}: invalid loopback source address") from error
    require(str(parsed) == address, f"{case_id}: source address is not canonical")
    require(
        not (
            isinstance(parsed, ipaddress.IPv6Address)
            and parsed.ipv4_mapped is not None
        ),
        f"{case_id}: mapped IPv6 must be normalized to IPv4",
    )
    if isinstance(parsed, ipaddress.IPv6Address) and parsed.is_link_local:
        require(
            set(value) == {"address", "scopeId"}
            and isinstance(value["scopeId"], int)
            and not isinstance(value["scopeId"], bool)
            and value["scopeId"] > 0,
            f"{case_id}: link-local source requires positive scopeId",
        )
    else:
        require(
            set(value) == {"address"},
            f"{case_id}: non-link-local source forbids scopeId",
        )


def validate_loopback_item(case_id: str, item: dict) -> None:
    require(
        set(item) == {
            "requestId", "device", "state", "readyForApproval",
            "safetyCode", "createdAt", "expiresAt", "sourceAddress",
        },
        f"{case_id}: loopback item shape mismatch",
    )
    require(
        isinstance(item["requestId"], str)
        and CANONICAL_UUID_RE.fullmatch(item["requestId"]) is not None,
        f"{case_id}: loopback item requestId invalid",
    )
    require(item["state"] in {"pending", "approved"},
            f"{case_id}: loopback item not live")
    require(isinstance(item["readyForApproval"], bool),
            f"{case_id}: loopback readiness not boolean")
    if item["state"] == "approved":
        require(item["readyForApproval"], f"{case_id}: approved item not ready")
    device = item["device"]
    require(
        isinstance(device, dict) and set(device) == {"name", "platform"},
        f"{case_id}: loopback device shape mismatch",
    )
    name = device["name"]
    require(isinstance(name, str), f"{case_id}: device name is not string")
    require(name == name.strip(), f"{case_id}: device name is not trimmed")
    require(1 <= len(name) <= 80, f"{case_id}: device name length invalid")
    require(
        all(not 0xD800 <= ord(char) <= 0xDFFF for char in name),
        f"{case_id}: device name contains non-scalar code point",
    )
    require(device["platform"] == "android", f"{case_id}: platform is not android")
    require(
        isinstance(item["safetyCode"], str)
        and SAFETY_CODE_RE.fullmatch(item["safetyCode"]) is not None,
        f"{case_id}: invalid safety code",
    )
    validate_loopback_source(case_id, item["sourceAddress"])
    created = validate_utc_second(item["createdAt"])
    expires = validate_utc_second(item["expiresAt"])
    require(created <= expires, f"{case_id}: createdAt is after expiresAt")


def validate_cleanup_checkpoints(case: dict) -> None:
    previous = case["initialOwnerState"]
    previous_step = -1
    checkpoints = case["cleanupCheckpoints"]
    require(checkpoints[0]["afterStep"] == 0, f"{case['id']}: missing owner baseline")
    require(
        checkpoints[0]["expectedOwnerState"] == previous,
        f"{case['id']}: owner baseline mismatch",
    )
    max_step = len(case["schedule"]) if case["operation"] == "race" else 1
    for checkpoint in checkpoints:
        require(
            checkpoint["afterStep"] > previous_step,
            f"{case['id']}: cleanup checkpoints not strictly ordered",
        )
        require(
            checkpoint["afterStep"] <= max_step,
            f"{case['id']}: cleanup checkpoint references nonexistent step",
        )
        current = checkpoint["expectedOwnerState"]
        validate_owner_transition(case["id"], previous, current)
        previous = current
        previous_step = checkpoint["afterStep"]
    require(previous_step == max_step, f"{case['id']}: missing final owner checkpoint")


def validate_snapshot(case_id: str, value: dict) -> None:
    state = value["state"]
    validate_status_authority(case_id, value, "requestPresent")
    require(
        value["readyForApproval"] == value["readyEventEmitted"],
        f"{case_id}: ready/event history mismatch",
    )
    replay = bool(value["replayNonceBase64url"])
    replay_hash = bool(value["replayCanonicalEnvelopeHashBase64url"])
    require(replay == replay_hash, f"{case_id}: partial replay verifier")
    require(
        replay == value["validatedEnvelope"],
        f"{case_id}: envelope verifier/validation mismatch",
    )
    if not value["validatedEnvelope"]:
        require(
            not value["fullEncryptedEnvelopePresent"],
            f"{case_id}: ciphertext without accepted envelope",
        )
    elif state == "pending":
        require(
            value["fullEncryptedEnvelopePresent"],
            f"{case_id}: live accepted envelope lacks ciphertext",
        )
    else:
        require(
            not value["fullEncryptedEnvelopePresent"],
            f"{case_id}: non-pending state retained full ciphertext",
        )
    if state in {"pending", "approved"} and value["readyForApproval"]:
        require(value["validatedEnvelope"], f"{case_id}: ready without envelope")
        require(value["heldGetservercert"], f"{case_id}: ready without held session")
        require(
            value["certificateFingerprintMatched"],
            f"{case_id}: ready without certificate binding",
        )
        require(value["readyEventEmitted"], f"{case_id}: ready without event")
    if state == "approved":
        require(value["readyForApproval"], f"{case_id}: approved must project ready")
    if state in {"paired", "rejected", "cancelled", "expired", "failed"} and value["readyForApproval"]:
        require(value["validatedEnvelope"], f"{case_id}: terminal ready without envelope")
        require(
            value["certificateFingerprintMatched"],
            f"{case_id}: terminal ready without certificate binding",
        )
        require(value["readyEventEmitted"], f"{case_id}: terminal ready without event")
    if state == "paired":
        require(value["readyForApproval"], f"{case_id}: paired must retain ready")
        require(not value["heldGetservercert"], f"{case_id}: paired retained held session")
        require(value["pairedDeviceCommitted"], f"{case_id}: paired not committed")
        require(value["validatedEnvelope"], f"{case_id}: paired without envelope")
        require(
            value["certificateFingerprintMatched"],
            f"{case_id}: paired without certificate binding",
        )
        require(value["readyEventEmitted"], f"{case_id}: paired without ready event")
    if state in {"paired", "rejected", "cancelled", "expired", "failed"}:
        require(not value["tokenBytesPresent"], f"{case_id}: terminal token bytes retained")


def validate_post(case_id: str, value: dict) -> None:
    validate_status_authority(case_id, value, "requestExists")
    require(
        value["readyForApproval"] == (value["domainEventCount"] == 1),
        f"{case_id}: ready/event-count history mismatch",
    )
    accepted = value["validatedEnvelope"]
    expected_storage = (
        "fullCiphertext" if accepted and value["state"] == "pending"
        else "digestOnly" if accepted
        else "none"
    )
    require(value["envelopeStorage"] == expected_storage,
            f"{case_id}: post envelope storage mismatch")
    if value["state"] in {"pending", "approved"} and value["readyForApproval"]:
        require(value["readyForApproval"], f"{case_id}: approved post not ready")
        require(value["validatedEnvelope"], f"{case_id}: ready post lacks envelope")
        require(value["heldPair"], f"{case_id}: ready post lacks held pair")
        require(
            value["certificateFingerprintMatched"],
            f"{case_id}: ready post lacks certificate binding",
        )
        require(value["domainEventCount"] == 1, f"{case_id}: ready event count mismatch")
    if value["state"] == "approved":
        require(value["readyForApproval"], f"{case_id}: approved post not ready")
    if value["state"] in {"paired", "rejected", "cancelled", "expired", "failed"} and value["readyForApproval"]:
        require(value["validatedEnvelope"], f"{case_id}: terminal post lacks envelope")
        require(
            value["certificateFingerprintMatched"],
            f"{case_id}: terminal post lacks certificate binding",
        )
        require(value["domainEventCount"] == 1, f"{case_id}: terminal post lacks event")
    if value["state"] == "paired":
        require(value["readyForApproval"], f"{case_id}: paired post not ready")
        require(not value["heldPair"], f"{case_id}: paired post retained held pair")
        require(value["validatedEnvelope"], f"{case_id}: paired post lacks envelope")
        require(
            value["certificateFingerprintMatched"],
            f"{case_id}: paired post lacks certificate binding",
        )
        require(value["domainEventCount"] == 1, f"{case_id}: paired event count mismatch")
        require(value["pairedDeviceCommitted"], f"{case_id}: paired post not committed")


def validate_http(case_id: str, operation: str, value: dict) -> None:
    status = value["status"]
    body = decode(value["body"])
    if status == 204:
        require(not body, f"{case_id}: 204 body must be empty")
        return
    require(body, f"{case_id}: non-204 body is empty")
    require(
        value["headers"] == [{"name": "Content-Type", "value": "application/json"}],
        f"{case_id}: JSON Content-Type mismatch",
    )
    text = body.decode("utf-8", errors="strict")
    parsed = json.loads(text)
    require(
        json.dumps(parsed, ensure_ascii=False, sort_keys=True, separators=(",", ":")) == text,
        f"{case_id}: response is not canonical compact JSON",
    )
    if status >= 400:
        require(
            isinstance(parsed, dict) and isinstance(parsed.get("code"), str),
            f"{case_id}: error body lacks machine code",
        )
        require(
            set(parsed).issubset({"code", "detail", "path", "currentState"}),
            f"{case_id}: error body has unknown field",
        )
        code = parsed["code"]
        require(
            code in ERROR_CODES_BY_STATUS.get(status, set()),
            f"{case_id}: HTTP status/error code mismatch",
        )
        detail = parsed.get("detail")
        if code == "invalidRequest":
            require(detail in INVALID_REQUEST_DETAILS, f"{case_id}: invalid request detail")
            if detail in INVALID_REQUEST_PATH_DETAILS:
                require("path" in parsed, f"{case_id}: schema error lacks path")
                require(
                    set(parsed) == {"code", "detail", "path"},
                    f"{case_id}: schema error field mismatch",
                )
            else:
                require("path" not in parsed, f"{case_id}: non-schema error has path")
                require(
                    set(parsed) == {"code", "detail"},
                    f"{case_id}: invalid request field mismatch",
                )
            require("currentState" not in parsed, f"{case_id}: invalid request has state")
        elif code == "invalidEnvelope":
            require(
                detail in {"authenticationFailed", "invalidPlaintext"},
                f"{case_id}: invalid envelope detail",
            )
            require(
                set(parsed) == {"code", "detail"},
                f"{case_id}: invalid envelope optional field mismatch",
            )
        elif code == "unsupportedMediaType":
            require(
                detail in {None, "contentEncodingNotSupported"},
                f"{case_id}: unsupported media detail",
            )
            require("path" not in parsed and "currentState" not in parsed,
                    f"{case_id}: unsupported media optional field mismatch")
        elif code == "invalidState":
            require("currentState" in parsed, f"{case_id}: invalid state lacks state")
            require(
                parsed["currentState"] in {
                    "pending", "approved", "paired", "rejected",
                    "cancelled", "expired", "failed",
                },
                f"{case_id}: invalid current state",
            )
            require(
                detail in {None, "notReadyForApproval"},
                f"{case_id}: invalid state detail",
            )
            require("path" not in parsed, f"{case_id}: invalid state has path")
        else:
            require(
                set(parsed) == {"code"},
                f"{case_id}: error code has forbidden optional field",
            )
    elif operation == "create":
        require(
            set(parsed) == {
                "expiresAt", "hostCertificateSha256", "hostEphemeralKey",
                "hostNonce", "hostUniqueId", "requestId", "requestToken", "version",
            },
            f"{case_id}: create response shape mismatch",
        )
    elif operation in {"status", "loopbackAllow", "loopbackReject", "androidCancel"}:
        require(
            set(parsed) == {"requestId", "state", "expiresAt", "failure"},
            f"{case_id}: status response shape mismatch",
        )
        require(
            isinstance(parsed["requestId"], str)
            and CANONICAL_UUID_RE.fullmatch(parsed["requestId"]) is not None,
            f"{case_id}: invalid status requestId",
        )
        require(
            isinstance(parsed["expiresAt"], str),
            f"{case_id}: status expiresAt is not string",
        )
        validate_utc_second(parsed["expiresAt"])
        require(
            isinstance(parsed["state"], str)
            and parsed["state"] in {
                "pending", "approved", "paired", "rejected",
                "cancelled", "expired", "failed",
            },
            f"{case_id}: invalid status state",
        )
        if parsed["state"] == "failed":
            require(
                parsed["failure"] in {
                    "certificateMismatch", "pairSessionMissing",
                    "cryptoFailure", "legacyPairingFailed",
                },
                f"{case_id}: invalid failed reason",
            )
        else:
            require(parsed["failure"] is None, f"{case_id}: non-failed has failure")
    elif operation == "loopbackList":
        require(
            set(parsed) == {"version", "requests"} and isinstance(parsed["requests"], list),
            f"{case_id}: loopback list shape mismatch",
        )
        require(parsed["version"] == 1, f"{case_id}: loopback list version mismatch")
        sort_keys = []
        for item in parsed["requests"]:
            validate_loopback_item(case_id, item)
            sort_keys.append((
                validate_utc_second(item["createdAt"]),
                item["requestId"].encode("utf-8"),
            ))
        require(sort_keys == sorted(sort_keys), f"{case_id}: loopback list not sorted")


def parse_http_json(value: dict) -> dict:
    return json.loads(decode(value["body"]).decode("utf-8"))


def validate_transition_authority(case: dict) -> None:
    if "context" not in case or "expectedPostState" not in case:
        return
    context = case["context"]
    post = case["expectedPostState"]
    had_request = context["requestPresent"] and context["state"] not in {"empty", "evicted"}
    has_request = post["requestExists"] and post["state"] not in {"empty", "evicted"}
    if had_request and has_request:
        require(
            post["statusExpiresAt"] == context["statusExpiresAt"],
            f"{case['id']}: existing request expiresAt changed",
        )
    elif had_request:
        require(
            post["state"] == "evicted" and post["statusExpiresAt"] == "",
            f"{case['id']}: existing request disappeared without eviction",
        )
    elif has_request:
        require(case["operation"] == "create", f"{case['id']}: non-create made request")
    if context["state"] == "failed" and post["state"] == "failed":
        require(
            post["failure"] == context["failure"],
            f"{case['id']}: existing failure changed",
        )


def validate_http_authority(case: dict, value: dict) -> None:
    if not (200 <= value["status"] < 300) or value["status"] == 204:
        return
    operation = case["operation"]
    body = parse_http_json(value)
    post = case["expectedPostState"]
    if operation == "create":
        request_body = parse_http_json(case["input"])
        require(
            body["requestId"] == request_body["requestId"],
            f"{case['id']}: create response requestId mismatch",
        )
        require(
            body["expiresAt"] == post["statusExpiresAt"],
            f"{case['id']}: create response expiresAt mismatch",
        )
    elif operation in {"status", "loopbackAllow", "loopbackReject"}:
        require(
            body["requestId"] == case["context"]["requestId"],
            f"{case['id']}: status requestId differs from context",
        )
        require(
            body["state"] == post["state"]
            and body["expiresAt"] == post["statusExpiresAt"]
            and body["failure"] == post["failure"],
            f"{case['id']}: status body differs from Host authority",
        )
        if operation == "status":
            require(
                post["state"] == case["context"]["state"]
                and post["statusExpiresAt"] == case["context"]["statusExpiresAt"]
                and post["failure"] == case["context"]["failure"],
                f"{case['id']}: pure status mutated authority",
            )


def next_state(state: str, generation: int, action: str) -> tuple[str, int]:
    if action == "allow" and state == "pending":
        return "approved", generation + 1
    if action == "reject" and state in {"pending", "approved"}:
        return "rejected", generation + 1
    if action == "cancel" and state in {"pending", "approved"}:
        return "cancelled", generation + 1
    if action == "commitPaired" and state == "approved":
        return "paired", generation + 1
    if action == "transportFailure" and state in {"pending", "approved"}:
        return "failed", generation + 1
    return state, generation


def validate_race(case: dict) -> None:
    state = case["context"]["state"]
    generation = case["context"]["generation"]
    for expected_step, step in enumerate(case["schedule"], 1):
        require(step["step"] == expected_step, f"{case['id']}: non-contiguous schedule")
        validate_race_tuple(case["id"], step)
        state, generation = next_state(state, generation, step["action"])
        post = step["expectedPostState"]
        require(post["state"] == state, f"{case['id']}: step state mismatch")
        require(
            post["generation"] == generation,
            f"{case['id']}: step generation mismatch",
        )
    require(
        case["expectedPostState"]["state"] == state
        and case["expectedPostState"]["generation"] == generation,
        f"{case['id']}: final race state/generation mismatch",
    )


def derive_android_outcome(probe: dict) -> str:
    if probe["kind"] == "transportError":
        return "unknown"
    if probe["status"] == 200:
        probe_body = json.loads(decode(probe["body"]).decode("utf-8"))
        state = probe_body["state"]
        return (
            state if state in {"paired", "cancelled", "rejected", "expired", "failed"}
            else "protocolError"
        )
    if probe["status"] in {401, 404, 410}:
        return "unknown"
    return "protocolError"


def validate_android_cancel(case: dict) -> None:
    expected_phases = [
        "afterDelete",
        "afterPreProbeCleanup",
        "afterStatusProbe",
        "afterFinalCleanup",
    ]
    initial_owner = case["initialOwnerState"]
    require(
        initial_owner["bearerTokenBytes"] == "present",
        f"{case['id']}: cancel begins without bearer",
    )
    require(initial_owner["poll"] == "active", f"{case['id']}: cancel begins without poll")
    previous_owner = initial_owner
    for index, step in enumerate(case["schedule"], 1):
        require(step["step"] == index, f"{case['id']}: invalid Android cancel step")
        require(
            step["phase"] == expected_phases[index - 1],
            f"{case['id']}: invalid Android cancel phase",
        )
        validate_post(f"{case['id']}:step{index}", step["expectedPostState"])
        owner = step["expectedOwnerState"]
        validate_owner_transition(case["id"], previous_owner, owner)
        previous_owner = owner
    initial_state = case["context"]["state"]
    initial_generation = case["context"]["generation"]
    after_delete = case["schedule"][0]["expectedPostState"]
    if initial_state in {"pending", "approved"}:
        allowed_after_delete = {"cancelled"}
        if initial_state == "approved":
            allowed_after_delete.add("paired")
        require(
            after_delete["state"] in allowed_after_delete
            and after_delete["generation"] == initial_generation + 1,
            f"{case['id']}: DELETE winner trajectory mismatch",
        )
    else:
        require(
            after_delete["state"] == initial_state
            and after_delete["generation"] == initial_generation,
            f"{case['id']}: terminal DELETE was not a no-op",
        )
    require(
        case["deleteExpectedHttp"]["status"] == 204,
        f"{case['id']}: Android cancel trajectory requires DELETE 204",
    )
    for schedule_index in (1, 3):
        post = case["schedule"][schedule_index]["expectedPostState"]
        require(
            post["state"] == case["schedule"][schedule_index - 1]["expectedPostState"]["state"]
            and post["generation"] == case["schedule"][schedule_index - 1]["expectedPostState"]["generation"],
            f"{case['id']}: local cleanup changed Host state",
        )
    step1_owner = case["schedule"][0]["expectedOwnerState"]
    for field in SECRET_FIELDS:
        require(
            step1_owner[field] == initial_owner[field],
            f"{case['id']}: DELETE changed local secret {field}",
        )
    require(
        step1_owner["poll"] == initial_owner["poll"],
        f"{case['id']}: DELETE changed local poll",
    )
    if after_delete["state"] == "paired":
        require(
            initial_owner["heldOperation"] in {"active", "completed"}
            and step1_owner["heldOperation"] == "completed",
            f"{case['id']}: paired winner did not complete held operation",
        )
    else:
        require(
            step1_owner["heldOperation"] == initial_owner["heldOperation"],
            f"{case['id']}: DELETE changed held operation",
        )
    before_probe = case["schedule"][1]["expectedOwnerState"]
    after_probe = case["schedule"][2]["expectedOwnerState"]
    final = case["schedule"][3]["expectedOwnerState"]
    for field in SECRET_FIELDS:
        if field == "bearerTokenBytes":
            expected = "present"
        else:
            expected = (
                "cleared"
                if initial_owner[field] in {"present", "cleared"}
                else "notOwned"
            )
        require(
            before_probe[field] == expected,
            f"{case['id']}: pre-probe secret state mismatch for {field}",
        )
    require(before_probe["poll"] == "cancelled", f"{case['id']}: poll not cancelled")
    expected_held = (
        "cancelled" if step1_owner["heldOperation"] == "active"
        else step1_owner["heldOperation"]
    )
    require(
        before_probe["heldOperation"] == expected_held,
        f"{case['id']}: pre-probe held operation mismatch",
    )
    require(after_probe == before_probe, f"{case['id']}: probe changed owner state")
    require(
        before_probe["bearerTokenBytes"] == "present"
        and after_probe["bearerTokenBytes"] == "present"
        and final["bearerTokenBytes"] == "cleared",
        f"{case['id']}: one-shot probe bearer lifecycle mismatch",
    )
    require(before_probe["poll"] == "cancelled", f"{case['id']}: poll not cancelled")
    expected_final = dict(after_probe)
    expected_final["bearerTokenBytes"] = "cleared"
    require(final == expected_final, f"{case['id']}: final owner cleanup mismatch")
    fault = case["schedule"][2]["probeFault"]
    probe = case["statusProbeExpected"]
    transport_faults = {"timeout", "connectionReset", "tlsFailure"}
    if fault in transport_faults:
        require(
            probe == {"kind": "transportError", "transportError": fault},
            f"{case['id']}: transport fault/result mismatch",
        )
    else:
        require(probe["kind"] == "http", f"{case['id']}: HTTP fault lacks response")
    probe_post = case["schedule"][2]["expectedPostState"]
    pre_probe_post = case["schedule"][1]["expectedPostState"]
    if fault == "terminalCacheEvicted":
        require(
            probe["status"] == 404
            and not probe_post["requestExists"]
            and probe_post["state"] == "evicted"
            and probe_post["generation"] == pre_probe_post["generation"],
            f"{case['id']}: terminal cache eviction trajectory mismatch",
        )
    else:
        require(
            probe_post["state"] == pre_probe_post["state"]
            and probe_post["generation"] == pre_probe_post["generation"],
            f"{case['id']}: status probe changed Host state",
        )
    if fault == "invalidBearer":
        require(probe["status"] == 401, f"{case['id']}: invalid bearer not 401")
    if fault == "none":
        require(probe["status"] == 200, f"{case['id']}: conforming probe not 200")
    if fault == "terminalCacheEvicted":
        require(probe["status"] == 404, f"{case['id']}: evicted probe not 404")
    if fault == "unexpectedHttp410":
        require(probe["status"] == 410, f"{case['id']}: injected violation not 410")
    if fault == "unexpectedLiveStatus":
        require(probe["status"] == 200, f"{case['id']}: live-status violation not 200")
        live_body = json.loads(decode(probe["body"]).decode("utf-8"))
        require(
            live_body["state"] in {"pending", "approved"},
            f"{case['id']}: injected live status is not live",
        )
    if probe["kind"] == "http" and probe["status"] == 200:
        probe_body = json.loads(decode(probe["body"]).decode("utf-8"))
        require(
            probe_body["requestId"] == case["context"]["requestId"],
            f"{case['id']}: status probe requestId mismatch",
        )
        require(
            probe_body["expiresAt"] == probe_post["statusExpiresAt"],
            f"{case['id']}: status probe expiresAt mismatch",
        )
        if fault == "none":
            require(
                probe_body["state"] == probe_post["state"]
                and probe_body["failure"] == probe_post["failure"],
                f"{case['id']}: conforming status/Host state mismatch",
            )
    derived_outcome = derive_android_outcome(probe)
    require(
        case["expectedAndroidOutcome"] == derived_outcome,
        f"{case['id']}: Android outcome does not match probe",
    )
    injected_faults = {"unexpectedHttp410", "unexpectedLiveStatus"}
    require(
        (fault in injected_faults)
        == (case["hostConformance"] == "injectedProtocolViolation"),
        f"{case['id']}: host conformance/fault mismatch",
    )
    if case["expectedAndroidOutcome"] == "paired":
        require(
            before_probe["heldOperation"] == "completed",
            f"{case['id']}: paired winner cancelled held operation",
        )
    require(
        case["expectedPostState"] == case["schedule"][3]["expectedPostState"],
        f"{case['id']}: final Android cancel state differs from schedule",
    )


def contract_self_test() -> None:
    present = {
        **{field: "present" for field in SECRET_FIELDS},
        "poll": "active",
        "heldOperation": "active",
    }
    cleared = {
        **{field: "cleared" for field in SECRET_FIELDS},
        "poll": "cancelled",
        "heldOperation": "cancelled",
    }
    validate_owner_transition("self-valid-owner", present, cleared)
    invalid_owner = dict(cleared)
    invalid_owner["pin"] = "present"
    expect_rejected(
        "owner rollback",
        lambda: validate_owner_transition("self-invalid-owner", cleared, invalid_owner),
    )
    validate_race_tuple(
        "self-valid-race",
        {"actor": "publicClient", "action": "cancel", "phase": "underLock"},
    )
    expect_rejected(
        "actor/action pair",
        lambda: validate_race_tuple(
            "self-invalid-race",
            {"actor": "loopbackUi", "action": "cancel", "phase": "underLock"},
        ),
    )
    authority = list(REQUIRED_CASE_IDS)
    expect_rejected(
        "manifest omission",
        lambda: require(authority[:-1] == authority, "manifest mismatch"),
    )
    expect_rejected(
        "root order",
        lambda: require(
            ["schemaVersion", "draft", "cases", "coverageManifest"]
            == ROOT_PROPERTY_ORDER,
            "root property order mismatch",
        ),
    )
    paired_owner = dict(present)
    paired_owner["heldOperation"] = "completed"
    require(
        paired_owner["heldOperation"] == "completed",
        "paired self-test owner must be completed",
    )
    fake_behavior = {
        "id": "self-invalid-cleanup",
        "operation": "status",
        "initialOwnerState": present,
        "cleanupCheckpoints": [
            {"afterStep": 64, "expectedOwnerState": cleared},
        ],
    }
    expect_rejected(
        "cleanup without baseline and real step",
        lambda: validate_cleanup_checkpoints(fake_behavior),
    )
    wrong_code = {
        "status": 401,
        "headers": [{"name": "Content-Type", "value": "application/json"}],
        "bodyEncoding": "base64url",
        "body": base64.urlsafe_b64encode(
            b'{"code":"rateLimited"}'
        ).rstrip(b"=").decode("ascii"),
    }
    expect_rejected(
        "HTTP error matrix mismatch",
        lambda: validate_http("self-invalid-http", "status", wrong_code),
    )
    unknown_status = {
        "status": 200,
        "headers": [{"name": "Content-Type", "value": "application/json"}],
        "bodyEncoding": "base64url",
        "body": base64.urlsafe_b64encode(
            b'{"expiresAt":"2026-07-24T00:00:00Z","failure":null,"requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","state":"mystery"}'
        ).rstrip(b"=").decode("ascii"),
    }
    expect_rejected(
        "unknown successful status state",
        lambda: validate_http("self-invalid-status", "status", unknown_status),
    )
    invalid_paired_post = {
        "requestExists": True,
        "state": "paired",
        "statusExpiresAt": "2026-07-24T00:00:00Z",
        "failure": None,
        "readyForApproval": True,
        "generation": 2,
        "heldPair": False,
        "tokenBytesPresent": False,
        "tokenHashPresent": True,
        "sourceVerifierPresent": True,
        "envelopeStorage": "digestOnly",
        "validatedEnvelope": True,
        "certificateFingerprintMatched": False,
        "domainEventCount": 0,
        "pairedDeviceCommitted": True,
    }
    expect_rejected(
        "paired without certificate and event",
        lambda: validate_post("self-invalid-paired", invalid_paired_post),
    )
    invalid_paired_held = dict(invalid_paired_post)
    invalid_paired_held.update({
        "certificateFingerprintMatched": True,
        "domainEventCount": 1,
        "heldPair": True,
    })
    expect_rejected(
        "paired post retained held pair",
        lambda: validate_post("self-invalid-paired-held", invalid_paired_held),
    )
    cancelled_post = dict(invalid_paired_post)
    cancelled_post.update({
        "state": "cancelled",
        "readyForApproval": True,
        "pairedDeviceCommitted": False,
        "certificateFingerprintMatched": True,
        "domainEventCount": 1,
    })
    probe_paired = {
        "kind": "http",
        "status": 200,
        "headers": [{"name": "Content-Type", "value": "application/json"}],
        "bodyEncoding": "base64url",
        "body": base64.urlsafe_b64encode(
            b'{"expiresAt":"2026-07-24T00:00:00Z","failure":null,"requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","state":"paired"}'
        ).rstrip(b"=").decode("ascii"),
    }
    expect_rejected(
        "paired probe with cancelled outcome",
        lambda: require(
            derive_android_outcome(probe_paired) == "cancelled",
            "Android outcome mismatch",
        ),
    )
    initial_cancel_owner = dict(present)
    after_pre_probe = dict(cleared)
    after_pre_probe["bearerTokenBytes"] = "present"
    after_probe = dict(after_pre_probe)
    after_final = dict(after_probe)
    after_final["bearerTokenBytes"] = "cleared"
    paired_post = dict(invalid_paired_post)
    paired_post.update({
        "certificateFingerprintMatched": True,
        "domainEventCount": 1,
    })
    invalid_paired_schedule_owner = {
        "id": "self-invalid-paired-owner",
        "context": {
            "state": "approved", "generation": 1,
            "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
        },
        "initialOwnerState": initial_cancel_owner,
        "deleteExpectedHttp": {"status": 204},
        "statusProbeExpected": probe_paired,
        "hostConformance": "conformant",
        "expectedAndroidOutcome": "paired",
        "schedule": [
            {"step": 1, "phase": "afterDelete", "expectedPostState": paired_post,
             "expectedOwnerState": initial_cancel_owner},
            {"step": 2, "phase": "afterPreProbeCleanup", "expectedPostState": paired_post,
             "expectedOwnerState": after_pre_probe},
            {"step": 3, "phase": "afterStatusProbe", "probeFault": "none",
             "expectedPostState": paired_post, "expectedOwnerState": after_probe},
            {"step": 4, "phase": "afterFinalCleanup", "expectedPostState": paired_post,
             "expectedOwnerState": after_final},
        ],
        "expectedPostState": paired_post,
    }
    expect_rejected(
        "paired winner held operation not completed at step1",
        lambda: validate_android_cancel(invalid_paired_schedule_owner),
    )
    probe_cancelled = dict(probe_paired)
    probe_cancelled["body"] = base64.urlsafe_b64encode(
        b'{"expiresAt":"2026-07-24T00:00:00Z","failure":null,"requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","state":"cancelled"}'
    ).rstrip(b"=").decode("ascii")
    bad_pre_probe = dict(after_pre_probe)
    bad_pre_probe["privateKey"] = "present"
    bad_pre_probe["heldOperation"] = "active"
    invalid_cleanup_case = {
        "id": "self-invalid-cancel-cleanup",
        "context": {
            "state": "approved", "generation": 1,
            "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
        },
        "initialOwnerState": initial_cancel_owner,
        "deleteExpectedHttp": {"status": 204},
        "statusProbeExpected": probe_cancelled,
        "hostConformance": "conformant",
        "expectedAndroidOutcome": "cancelled",
        "schedule": [
            {"step": 1, "phase": "afterDelete", "expectedPostState": cancelled_post,
             "expectedOwnerState": initial_cancel_owner},
            {"step": 2, "phase": "afterPreProbeCleanup", "expectedPostState": cancelled_post,
             "expectedOwnerState": bad_pre_probe},
            {"step": 3, "phase": "afterStatusProbe", "probeFault": "none",
             "expectedPostState": cancelled_post, "expectedOwnerState": bad_pre_probe},
            {"step": 4, "phase": "afterFinalCleanup", "expectedPostState": cancelled_post,
             "expectedOwnerState": after_final},
        ],
        "expectedPostState": cancelled_post,
    }
    expect_rejected(
        "pre-probe secret and held cleanup",
        lambda: validate_android_cancel(invalid_cleanup_case),
    )
    legal_paired_snapshot = {
        "requestPresent": True,
        "state": "paired",
        "statusExpiresAt": "2026-07-24T00:00:00Z",
        "failure": None,
        "replayNonceBase64url": "AAAAAAAAAAAAAAAA",
        "replayCanonicalEnvelopeHashBase64url": "A" * 43,
        "validatedEnvelope": True,
        "fullEncryptedEnvelopePresent": False,
        "readyForApproval": True,
        "heldGetservercert": False,
        "certificateFingerprintMatched": True,
        "readyEventEmitted": True,
        "pairedDeviceCommitted": True,
        "tokenBytesPresent": False,
    }
    validate_snapshot("self-valid-paired", legal_paired_snapshot)
    terminal_history = dict(legal_paired_snapshot)
    terminal_history.update({"state": "cancelled", "pairedDeviceCommitted": False})
    event_without_ready = dict(terminal_history)
    event_without_ready["readyForApproval"] = False
    expect_rejected(
        "terminal event without ready history",
        lambda: validate_snapshot("self-invalid-event-history", event_without_ready),
    )
    ready_without_event = dict(terminal_history)
    ready_without_event["readyEventEmitted"] = False
    expect_rejected(
        "terminal ready without event history",
        lambda: validate_snapshot("self-invalid-ready-history", ready_without_event),
    )
    mixed_initial = dict(initial_cancel_owner)
    mixed_initial["plaintext"] = "cleared"
    mixed_initial["fullCiphertext"] = "notOwned"
    mixed_step2 = dict(after_pre_probe)
    mixed_step2["plaintext"] = "cleared"
    mixed_step2["fullCiphertext"] = "notOwned"
    mixed_step3 = dict(mixed_step2)
    mixed_step4 = dict(mixed_step3)
    mixed_step4["bearerTokenBytes"] = "cleared"
    mixed_case = {
        **invalid_cleanup_case,
        "id": "self-valid-mixed-owner",
        "initialOwnerState": mixed_initial,
        "schedule": [
            {"step": 1, "phase": "afterDelete", "expectedPostState": cancelled_post,
             "expectedOwnerState": mixed_initial},
            {"step": 2, "phase": "afterPreProbeCleanup", "expectedPostState": cancelled_post,
             "expectedOwnerState": mixed_step2},
            {"step": 3, "phase": "afterStatusProbe", "probeFault": "none",
             "expectedPostState": cancelled_post, "expectedOwnerState": mixed_step3},
            {"step": 4, "phase": "afterFinalCleanup", "expectedPostState": cancelled_post,
             "expectedOwnerState": mixed_step4},
        ],
    }
    validate_android_cancel(mixed_case)
    cleared_to_not_owned = json.loads(json.dumps(mixed_case))
    cleared_to_not_owned["schedule"][1]["expectedOwnerState"]["plaintext"] = "notOwned"
    cleared_to_not_owned["schedule"][2]["expectedOwnerState"]["plaintext"] = "notOwned"
    cleared_to_not_owned["schedule"][3]["expectedOwnerState"]["plaintext"] = "notOwned"
    expect_rejected(
        "cleared secret changed to notOwned",
        lambda: validate_android_cancel(cleared_to_not_owned),
    )
    probe_rejected = dict(probe_paired)
    probe_rejected["body"] = base64.urlsafe_b64encode(
        b'{"expiresAt":"2026-07-24T00:00:00Z","failure":null,"requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","state":"rejected"}'
    ).rstrip(b"=").decode("ascii")
    mismatched_status_case = json.loads(json.dumps(mixed_case))
    mismatched_status_case["id"] = "self-invalid-host-status-mismatch"
    mismatched_status_case["statusProbeExpected"] = probe_rejected
    mismatched_status_case["expectedAndroidOutcome"] = "rejected"
    expect_rejected(
        "conforming status body differs from Host snapshot",
        lambda: validate_android_cancel(mismatched_status_case),
    )
    cancelled_held_initial = json.loads(json.dumps(mixed_case))
    cancelled_held_initial["id"] = "self-valid-cancelled-held-owner"
    cancelled_held_initial["initialOwnerState"]["heldOperation"] = "cancelled"
    for step in cancelled_held_initial["schedule"]:
        step["expectedOwnerState"]["heldOperation"] = "cancelled"
    validate_android_cancel(cancelled_held_initial)
    cancelled_to_not_owned = json.loads(json.dumps(cancelled_held_initial))
    cancelled_to_not_owned["schedule"][1]["expectedOwnerState"]["heldOperation"] = "notOwned"
    cancelled_to_not_owned["schedule"][2]["expectedOwnerState"]["heldOperation"] = "notOwned"
    cancelled_to_not_owned["schedule"][3]["expectedOwnerState"]["heldOperation"] = "notOwned"
    expect_rejected(
        "cancelled held operation changed to notOwned",
        lambda: validate_android_cancel(cancelled_to_not_owned),
    )
    expect_rejected(
        "invalid Gregorian status time",
        lambda: validate_utc_second("2026-99-99T99:99:99Z"),
    )
    validate_utc_second("2024-02-29T23:59:59Z")
    expect_rejected(
        "invalid non-leap date",
        lambda: validate_utc_second("2023-02-29T23:59:59Z"),
    )
    status_authority_case = {
        "id": "self-status-authority",
        "operation": "status",
        "context": {
            "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
            "state": "cancelled",
            "statusExpiresAt": "2026-07-24T00:00:00Z",
            "failure": None,
        },
        "expectedPostState": cancelled_post,
    }
    wrong_request_body = dict(probe_cancelled)
    wrong_request_body["body"] = base64.urlsafe_b64encode(
        b'{"expiresAt":"2026-07-24T00:00:00Z","failure":null,"requestId":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa","state":"cancelled"}'
    ).rstrip(b"=").decode("ascii")
    expect_rejected(
        "status requestId authority mismatch",
        lambda: validate_http_authority(status_authority_case, wrong_request_body),
    )
    wrong_expiry_body = dict(probe_cancelled)
    wrong_expiry_body["body"] = base64.urlsafe_b64encode(
        b'{"expiresAt":"2026-07-25T00:00:00Z","failure":null,"requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","state":"cancelled"}'
    ).rstrip(b"=").decode("ascii")
    expect_rejected(
        "status expiresAt authority mismatch",
        lambda: validate_http_authority(status_authority_case, wrong_expiry_body),
    )
    failed_post = dict(cancelled_post)
    failed_post.update({"state": "failed", "failure": "cryptoFailure"})
    failed_case = dict(status_authority_case)
    failed_case["expectedPostState"] = failed_post
    wrong_failure_body = dict(probe_cancelled)
    wrong_failure_body["body"] = base64.urlsafe_b64encode(
        b'{"expiresAt":"2026-07-24T00:00:00Z","failure":"pairSessionMissing","requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","state":"failed"}'
    ).rstrip(b"=").decode("ascii")
    expect_rejected(
        "failed reason authority mismatch",
        lambda: validate_http_authority(failed_case, wrong_failure_body),
    )
    nonfailed_with_failure = dict(cancelled_post)
    nonfailed_with_failure["failure"] = "cryptoFailure"
    expect_rejected(
        "non-failed post has failure",
        lambda: validate_post("self-invalid-nonfailed-failure", nonfailed_with_failure),
    )
    def loopback_http(item: dict) -> dict:
        body = json.dumps(
            {"requests": [item], "version": 1},
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
        return {
            "status": 200,
            "headers": [{"name": "Content-Type", "value": "application/json"}],
            "bodyEncoding": "base64url",
            "body": base64.urlsafe_b64encode(body).rstrip(b"=").decode("ascii"),
        }

    valid_loopback = {
        "requestId": "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
        "device": {"name": "Android Tablet", "platform": "android"},
        "state": "approved",
        "readyForApproval": True,
        "safetyCode": "ABCD-2345",
        "createdAt": "2026-07-24T00:00:00Z",
        "expiresAt": "2026-07-24T00:02:00Z",
        "sourceAddress": {"address": "192.0.2.10"},
    }
    validate_http("self-valid-loopback-ipv4", "loopbackList", loopback_http(valid_loopback))
    valid_link_local = json.loads(json.dumps(valid_loopback))
    valid_link_local["sourceAddress"] = {"address": "fe80::1", "scopeId": 7}
    validate_http(
        "self-valid-loopback-link-local",
        "loopbackList",
        loopback_http(valid_link_local),
    )

    def reject_loopback(label: str, mutate) -> None:
        item = json.loads(json.dumps(valid_loopback))
        mutate(item)
        expect_rejected(
            label,
            lambda: validate_http(f"self-{label}", "loopbackList", loopback_http(item)),
        )

    reject_loopback("empty-name", lambda item: item["device"].update(name=""))
    reject_loopback("untrimmed-name", lambda item: item["device"].update(name=" name "))
    reject_loopback("non-android-platform", lambda item: item["device"].update(platform="ios"))
    reject_loopback("invalid-safety-code", lambda item: item.update(safetyCode="NOT-A-SAS"))
    reject_loopback("integer-source", lambda item: item.update(sourceAddress={"address": 12345}))
    reject_loopback(
        "noncanonical-ipv4",
        lambda item: item.update(sourceAddress={"address": "192.000.002.010"}),
    )
    reject_loopback(
        "noncanonical-ipv6",
        lambda item: item.update(sourceAddress={"address": "2001:0db8::1"}),
    )
    reject_loopback(
        "mapped-ipv6",
        lambda item: item.update(sourceAddress={"address": "::ffff:c000:201"}),
    )
    reject_loopback(
        "link-local-missing-scope",
        lambda item: item.update(sourceAddress={"address": "fe80::1"}),
    )
    reject_loopback(
        "link-local-zero-scope",
        lambda item: item.update(sourceAddress={"address": "fe80::1", "scopeId": 0}),
    )
    reject_loopback(
        "link-local-boolean-scope",
        lambda item: item.update(sourceAddress={"address": "fe80::1", "scopeId": True}),
    )
    reject_loopback(
        "global-ipv6-with-scope",
        lambda item: item.update(sourceAddress={"address": "2001:db8::1", "scopeId": 4}),
    )
    reject_loopback(
        "approved-not-ready",
        lambda item: item.update(readyForApproval=False),
    )
    reject_loopback(
        "invalid-created-at",
        lambda item: item.update(createdAt="2026-02-30T00:00:00Z"),
    )
    reject_loopback(
        "created-after-expiry",
        lambda item: item.update(createdAt="2026-07-24T00:03:00Z"),
    )
    print("VECTOR_CONTRACT_SELF_TEST_OK")


def main() -> None:
    root = json.loads(FIXTURE.read_text(encoding="utf-8"))
    require(list(root) == ROOT_PROPERTY_ORDER, "root property order mismatch")
    cases = root["cases"]
    ids = [case["id"] for case in cases]
    required = root["coverageManifest"]["requiredIds"]
    authority = list(REQUIRED_CASE_IDS)
    require(
        authority == sorted(authority, key=lambda item: item.encode("utf-8")),
        "required-ID authority not sorted",
    )
    require(len(authority) == len(set(authority)), "required-ID authority duplicates")
    require(ids == sorted(ids, key=lambda item: item.encode("utf-8")), "cases not sorted")
    require(required == authority, "coverage manifest differs from authority")
    require(ids == authority, "actual cases differ from coverage authority")
    require(len(ids) == len(set(ids)), "duplicate case id")

    for case in cases:
        case_id = case["id"]
        if "context" in case:
            validate_snapshot(case_id, case["context"])
        if "expectedPostState" in case:
            validate_post(case_id, case["expectedPostState"])
            validate_transition_authority(case)
        if "expectedHttp" in case:
            validate_http(case_id, case["operation"], case["expectedHttp"])
            validate_http_authority(case, case["expectedHttp"])
        if "deleteExpectedHttp" in case:
            validate_http(case_id, "cancel", case["deleteExpectedHttp"])
            probe = case["statusProbeExpected"]
            if probe["kind"] == "http":
                validate_http(case_id, "androidCancel", probe)
        if case["operation"] == "race":
            validate_race(case)
        if case["operation"] == "androidCancel":
            validate_android_cancel(case)
        elif "cleanupCheckpoints" in case:
            validate_cleanup_checkpoints(case)

    counts = Counter(case["operation"] for case in cases)
    print(f"VECTOR_INVARIANTS_OK cases={len(cases)} operations={dict(sorted(counts.items()))}")


if __name__ == "__main__":
    if sys.argv[1:] == ["--contract-self-test"]:
        contract_self_test()
    else:
        main()
