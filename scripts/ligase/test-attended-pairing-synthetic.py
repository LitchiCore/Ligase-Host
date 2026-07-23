#!/usr/bin/env python3
"""Synthetic attended-pairing v1 client for an isolated Ligase core."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa, x25519
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives.ciphers.aead import ChaCha20Poly1305
from cryptography.x509.oid import NameOID

OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def b64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).decode("ascii").rstrip("=")


def b64url_decode(value: str) -> bytes:
    return base64.urlsafe_b64decode(value + "=" * ((4 - len(value) % 4) % 4))


def canonical(value: object) -> bytes:
    return json.dumps(
        value, ensure_ascii=False, separators=(",", ":"), sort_keys=True
    ).encode("utf-8")


def http(
    base: str,
    method: str,
    path: str,
    *,
    body: object | None = None,
    token: str | None = None,
    timeout: float = 5,
) -> tuple[int, bytes]:
    raw = None if body is None else canonical(body)
    headers: dict[str, str] = {}
    if raw is not None:
        headers["Content-Type"] = "application/json"
    if token is not None:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(
        base + path, data=raw, headers=headers, method=method
    )
    try:
        with OPENER.open(request, timeout=timeout) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def json_http(*args, **kwargs) -> tuple[int, dict]:
    status, body = http(*args, **kwargs)
    return status, json.loads(body.decode("utf-8")) if body else {}


def client_identity() -> tuple[rsa.RSAPrivateKey, x509.Certificate, bytes, bytes]:
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = x509.Name(
        [
            x509.NameAttribute(NameOID.COMMON_NAME, "Ligase Synthetic Android"),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "LitchiCore Test"),
        ]
    )
    now = datetime.now(timezone.utc)
    certificate = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(subject)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - timedelta(minutes=1))
        .not_valid_after(now + timedelta(days=1))
        .sign(key, hashes.SHA256())
    )
    der = certificate.public_bytes(serialization.Encoding.DER)
    pem = certificate.public_bytes(serialization.Encoding.PEM)
    return key, certificate, der, pem


@dataclass
class Enrollment:
    request_id: str
    token: str
    pin: str
    client_private: x25519.X25519PrivateKey
    create_request: dict
    create_response: dict
    certificate: x509.Certificate
    certificate_key: rsa.RSAPrivateKey
    certificate_pem: bytes
    envelope_nonce: bytes
    envelope_ciphertext: bytes


def create_enrollment(base: str, device_name: str) -> Enrollment:
    certificate_key, certificate, certificate_der, certificate_pem = (
        client_identity()
    )
    client_private = x25519.X25519PrivateKey.generate()
    request_id = str(uuid.uuid4())
    client_nonce = os.urandom(32)
    request = {
        "version": 1,
        "requestId": request_id,
        "device": {"name": device_name, "platform": "android"},
        "clientEphemeralKey": b64url(
            client_private.public_key().public_bytes(
                serialization.Encoding.Raw, serialization.PublicFormat.Raw
            )
        ),
        "clientNonce": b64url(client_nonce),
        "clientCertificateSha256": b64url(hashlib.sha256(certificate_der).digest()),
    }
    status, response = json_http(
        base, "POST", "/ligase/v1/pairing/requests", body=request
    )
    if status != 201:
        raise RuntimeError(f"create failed: HTTP {status} {response}")
    response_without_token = dict(response)
    token = response_without_token.pop("requestToken")
    transcript = canonical(
        {"request": request, "response": response_without_token}
    )
    transcript_hash = hashlib.sha256(transcript).digest()
    shared = client_private.exchange(
        x25519.X25519PublicKey.from_public_bytes(
            b64url_decode(response["hostEphemeralKey"])
        )
    )
    import hmac

    salt = hashlib.sha256(
        client_nonce + b64url_decode(response["hostNonce"])
    ).digest()
    prk = hmac.new(salt, shared, hashlib.sha256).digest()
    info = b"Ligase attended pairing v1\x00" + transcript_hash
    pairing_key = hmac.new(prk, info + b"\x01", hashlib.sha256).digest()
    pin = f"{int.from_bytes(os.urandom(2), 'big') % 10000:04d}"
    plaintext = canonical({"legacyPin": pin})
    if len(plaintext) != 20:
        raise AssertionError("legacy PIN plaintext is not 20 bytes")
    nonce = os.urandom(12)
    ciphertext = ChaCha20Poly1305(pairing_key).encrypt(
        nonce, plaintext, transcript_hash
    )
    status, response_body = http(
        base,
        "PUT",
        f"/ligase/v1/pairing/requests/{request_id}/envelope",
        body={"nonce": b64url(nonce), "ciphertext": b64url(ciphertext)},
        token=token,
    )
    if status != 204 or response_body:
        raise RuntimeError(f"envelope failed: HTTP {status} {response_body!r}")
    return Enrollment(
        request_id,
        token,
        pin,
        client_private,
        request,
        response,
        certificate,
        certificate_key,
        certificate_pem,
        nonce,
        ciphertext,
    )


def xml_get(base: str, parameters: dict[str, str], timeout: float = 5) -> ET.Element:
    url = base + "/pair?" + urllib.parse.urlencode(parameters)
    with OPENER.open(url, timeout=timeout) as response:
        return ET.fromstring(response.read())


def paired(root: ET.Element) -> bool:
    return root.attrib.get("status_code") == "200" and root.findtext("paired") == "1"


def aes_ecb(key: bytes, value: bytes, encrypt: bool) -> bytes:
    context = Cipher(algorithms.AES(key), modes.ECB()).encryptor()
    if not encrypt:
        context = Cipher(algorithms.AES(key), modes.ECB()).decryptor()
    return context.update(value) + context.finalize()


def complete_legacy_pair(base: str, enrollment: Enrollment) -> None:
    salt = os.urandom(16)
    unique_id = uuid.uuid4().hex
    held: dict[str, object] = {}

    def first_phase() -> None:
        try:
            held["root"] = xml_get(
                base,
                {
                    "uniqueid": unique_id,
                    "phrase": "getservercert",
                    "salt": salt.hex(),
                    "clientcert": enrollment.certificate_pem.hex(),
                    "devicename": enrollment.create_request["device"]["name"],
                    "ligasepairingrequestid": enrollment.request_id,
                },
                timeout=15,
            )
        except BaseException as error:  # propagate in the main thread
            held["error"] = error

    thread = threading.Thread(target=first_phase, daemon=True)
    thread.start()
    for _ in range(50):
        status, listing = json_http(base, "GET", "/ligase/v1/pairing/requests")
        if status == 200 and listing["requests"]:
            item = next(
                value
                for value in listing["requests"]
                if value["requestId"] == enrollment.request_id
            )
            if item["readyForApproval"]:
                break
        threading.Event().wait(0.05)
    else:
        raise RuntimeError("request never became ready for approval")

    status, allowed = json_http(
        base,
        "POST",
        f"/ligase/v1/pairing/requests/{enrollment.request_id}/allow",
    )
    if status != 202 or allowed["state"] != "approved":
        raise RuntimeError(f"allow failed: HTTP {status} {allowed}")
    thread.join(5)
    if thread.is_alive():
        raise RuntimeError("held getservercert did not complete")
    if "error" in held:
        raise held["error"]  # type: ignore[misc]
    first = held["root"]
    if not isinstance(first, ET.Element) or not paired(first):
        raise RuntimeError("getservercert failed")

    host_certificate_pem = bytes.fromhex(first.findtext("plaincert", ""))
    host_certificate = x509.load_pem_x509_certificate(host_certificate_pem)
    expected_host_hash = b64url_decode(
        enrollment.create_response["hostCertificateSha256"]
    )
    actual_host_hash = hashlib.sha256(
        host_certificate.public_bytes(serialization.Encoding.DER)
    ).digest()
    if actual_host_hash != expected_host_hash:
        raise RuntimeError("listener certificate does not match transcript")

    aes_key = hashlib.sha256(salt + enrollment.pin.encode("ascii")).digest()[:16]
    client_challenge = os.urandom(16)
    phase2 = xml_get(
        base,
        {
            "uniqueid": unique_id,
            "clientchallenge": aes_ecb(aes_key, client_challenge, True).hex(),
        },
    )
    if not paired(phase2):
        raise RuntimeError("clientchallenge failed")
    challenge_response = aes_ecb(
        aes_key, bytes.fromhex(phase2.findtext("challengeresponse", "")), False
    )
    if len(challenge_response) != 48:
        raise RuntimeError("invalid challenge response length")
    server_challenge = challenge_response[32:]
    client_secret = os.urandom(16)
    client_hash = hashlib.sha256(
        server_challenge + enrollment.certificate.signature + client_secret
    ).digest()
    phase3 = xml_get(
        base,
        {
            "uniqueid": unique_id,
            "serverchallengeresp": aes_ecb(aes_key, client_hash, True).hex(),
        },
    )
    if not paired(phase3):
        raise RuntimeError("server challenge response failed")
    client_signature = enrollment.certificate_key.sign(
        client_secret, padding.PKCS1v15(), hashes.SHA256()
    )
    phase4 = xml_get(
        base,
        {
            "uniqueid": unique_id,
            "clientpairingsecret": (client_secret + client_signature).hex(),
        },
    )
    if not paired(phase4):
        raise RuntimeError("client pairing secret failed")
    status, final_status = json_http(
        base,
        "GET",
        f"/ligase/v1/pairing/requests/{enrollment.request_id}",
        token=enrollment.token,
    )
    if status != 200 or final_status["state"] != "paired":
        raise RuntimeError(f"terminal status is not paired: {status} {final_status}")
    status, devices = json_http(base, "GET", "/ligase/v1/devices")
    if status != 200 or not any(
        item.get("name") == enrollment.create_request["device"]["name"]
        for item in devices.get("devices", devices if isinstance(devices, list) else [])
    ):
        raise RuntimeError("paired device was not dynamically projected")


def exercise_negative_routes(
    base: str, enrollment: Enrollment, alternate_base: str | None
) -> dict[str, int]:
    request_path = f"/ligase/v1/pairing/requests/{enrollment.request_id}"
    envelope_path = request_path + "/envelope"
    envelope_body = {
        "nonce": b64url(enrollment.envelope_nonce),
        "ciphertext": b64url(enrollment.envelope_ciphertext),
    }
    status, _ = http(
        base,
        "PUT",
        envelope_path,
        body=envelope_body,
        token=enrollment.token,
    )
    if status != 204:
        raise RuntimeError(f"terminal exact replay was not idempotent: {status}")
    changed = bytearray(enrollment.envelope_ciphertext)
    changed[-1] ^= 1
    status, body = json_http(
        base,
        "PUT",
        envelope_path,
        body={
            "nonce": b64url(enrollment.envelope_nonce),
            "ciphertext": b64url(changed),
        },
        token=enrollment.token,
    )
    if status != 409 or body != {"code": "nonceReuse"}:
        raise RuntimeError(f"nonce replay branch mismatch: {status} {body}")
    other_nonce = bytearray(enrollment.envelope_nonce)
    other_nonce[0] ^= 1
    status, body = json_http(
        base,
        "PUT",
        envelope_path,
        body={
            "nonce": b64url(other_nonce),
            "ciphertext": b64url(enrollment.envelope_ciphertext),
        },
        token=enrollment.token,
    )
    if status != 409 or body != {"code": "envelopeConflict"}:
        raise RuntimeError(f"envelope conflict branch mismatch: {status} {body}")

    status, body = json_http(
        base, "GET", request_path, token=b64url(os.urandom(32))
    )
    if status != 401 or body != {"code": "invalidRequestToken"}:
        raise RuntimeError(f"bad token branch mismatch: {status} {body}")
    status, body = json_http(
        base,
        "GET",
        f"/ligase/v1/pairing/requests/{enrollment.request_id.upper()}",
        token=enrollment.token,
    )
    if status != 404 or body != {"code": "requestNotFound"}:
        raise RuntimeError(
            f"noncanonical request ID branch mismatch: {status} {body}"
        )
    if alternate_base is not None:
        status, body = json_http(
            alternate_base, "GET", request_path, token=enrollment.token
        )
        if status != 401 or body != {"code": "invalidRequestToken"}:
            raise RuntimeError(
                f"source/family mismatch branch mismatch: {status} {body}"
            )

    rejected = create_enrollment(base, "Synthetic Rejected")
    status, body = json_http(
        base,
        "POST",
        f"/ligase/v1/pairing/requests/{rejected.request_id}/reject",
    )
    if status != 200 or body["state"] != "rejected":
        raise RuntimeError(f"reject branch mismatch: {status} {body}")

    cancelled = create_enrollment(base, "Synthetic Cancelled")
    cancel_path = f"/ligase/v1/pairing/requests/{cancelled.request_id}"
    status, raw = http(
        base, "DELETE", cancel_path, token=cancelled.token
    )
    if status != 204 or raw:
        raise RuntimeError(f"cancel branch mismatch: {status} {raw!r}")
    status, body = json_http(
        base, "GET", cancel_path, token=cancelled.token
    )
    if status != 200 or body["state"] != "cancelled":
        raise RuntimeError(f"cancel status mismatch: {status} {body}")

    mismatched = create_enrollment(base, "Synthetic Cert Mismatch")
    _, _, _, wrong_pem = client_identity()
    root = xml_get(
        base,
        {
            "uniqueid": uuid.uuid4().hex,
            "phrase": "getservercert",
            "salt": os.urandom(16).hex(),
            "clientcert": wrong_pem.hex(),
            "devicename": "Synthetic Cert Mismatch",
            "ligasepairingrequestid": mismatched.request_id,
        },
    )
    if root.attrib.get("status_code") != "409":
        raise RuntimeError("certificate mismatch getservercert was not rejected")
    status, body = json_http(
        base,
        "GET",
        f"/ligase/v1/pairing/requests/{mismatched.request_id}",
        token=mismatched.token,
    )
    if (
        status != 200
        or body["state"] != "failed"
        or body["failure"] != "certificateMismatch"
    ):
        raise RuntimeError(f"certificate mismatch terminal state: {status} {body}")

    later = create_enrollment(base, "Synthetic Later Parameter")
    later_salt = os.urandom(16)
    later_unique_id = uuid.uuid4().hex
    held: dict[str, object] = {}

    def later_first_phase() -> None:
        try:
            held["root"] = xml_get(
                base,
                {
                    "uniqueid": later_unique_id,
                    "phrase": "getservercert",
                    "salt": later_salt.hex(),
                    "clientcert": later.certificate_pem.hex(),
                    "devicename": "Synthetic Later Parameter",
                    "ligasepairingrequestid": later.request_id,
                },
                timeout=15,
            )
        except BaseException as error:
            held["error"] = error

    held_thread = threading.Thread(target=later_first_phase, daemon=True)
    held_thread.start()
    for _ in range(50):
        status, listing = json_http(base, "GET", "/ligase/v1/pairing/requests")
        if status == 200 and any(
            item["requestId"] == later.request_id
            and item["readyForApproval"]
            for item in listing["requests"]
        ):
            break
        threading.Event().wait(0.05)
    else:
        raise RuntimeError("later-phase fixture never became ready")
    status, _ = json_http(
        base,
        "POST",
        f"/ligase/v1/pairing/requests/{later.request_id}/allow",
    )
    if status != 202:
        raise RuntimeError("later-phase fixture allow failed")
    held_thread.join(5)
    if held_thread.is_alive() or "error" in held:
        raise RuntimeError("later-phase held response did not complete")
    invalid_later = xml_get(
        base,
        {
            "uniqueid": later_unique_id,
            "clientchallenge": "00",
            "ligasepairingrequestid": later.request_id,
        },
    )
    if invalid_later.attrib.get("status_code") != "400":
        raise RuntimeError("later phase accepted attended request ID")
    status, body = json_http(
        base,
        "GET",
        f"/ligase/v1/pairing/requests/{later.request_id}",
        token=later.token,
    )
    if (
        status != 200
        or body["state"] != "failed"
        or body["failure"] != "legacyPairingFailed"
    ):
        raise RuntimeError(f"later parameter terminal state: {status} {body}")

    return {
        "terminalReplay": 204,
        "nonceReuse": 409,
        "envelopeConflict": 409,
        "badToken": 401,
        "noncanonicalRequestId": 404,
        "sourceFamilyMismatch": 401 if alternate_base is not None else 0,
        "reject": 200,
        "cancel": 204,
        "certificateMismatch": 409,
        "laterPhaseRequestId": 400,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--alternate-base")
    parser.add_argument(
        "--mode", choices=("full", "timeout", "rate"), default="full"
    )
    parser.add_argument("--wait-seconds", type=float, default=3)
    args = parser.parse_args()
    base = args.base.rstrip("/")
    if args.mode == "timeout":
        enrollment = create_enrollment(base, "Synthetic Timeout")
        time.sleep(args.wait_seconds)
        status, terminal = json_http(
            base,
            "GET",
            f"/ligase/v1/pairing/requests/{enrollment.request_id}",
            token=enrollment.token,
        )
        if status != 200 or terminal["state"] != "expired":
            raise RuntimeError(f"timeout mismatch: HTTP {status} {terminal}")
        print('{"result":"PASS","state":"expired"}')
        return
    if args.mode == "rate":
        first = create_enrollment(base, "Synthetic Rate One")
        status, response = json_http(
            base,
            "POST",
            "/ligase/v1/pairing/requests",
            body={
                **first.create_request,
                "requestId": str(uuid.uuid4()),
                "clientCertificateSha256": b64url(os.urandom(32)),
            },
        )
        if status != 201:
            raise RuntimeError(f"second rate create failed: {status} {response}")
        third = {
            **first.create_request,
            "requestId": str(uuid.uuid4()),
            "clientCertificateSha256": b64url(os.urandom(32)),
        }
        status, response = json_http(
            base, "POST", "/ligase/v1/pairing/requests", body=third
        )
        if status != 429 or response != {"code": "rateLimited"}:
            raise RuntimeError(f"rate limit mismatch: {status} {response}")
        print('{"result":"PASS","rateLimited":429}')
        return
    enrollment = create_enrollment(base, "Synthetic Android")
    complete_legacy_pair(base, enrollment)
    negatives = exercise_negative_routes(
        base,
        enrollment,
        args.alternate_base.rstrip("/") if args.alternate_base else None,
    )
    print(
        json.dumps(
            {
                "result": "PASS",
                "requestId": enrollment.request_id,
                "state": "paired",
                "managementAccountRequired": False,
                "negativeRoutes": negatives,
            },
            separators=(",", ":"),
        )
    )


if __name__ == "__main__":
    main()
