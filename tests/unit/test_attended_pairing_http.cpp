#include <gtest/gtest.h>

#include "src/attended_pairing_http.h"

namespace {
  using namespace attended_pairing;
  using namespace attended_pairing::http;

  constexpr auto request_id = "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4";
  const source_identity source {"192.0.2.10", 0};

  struct adapter final: legacy_pairing_adapter {
    bytes pin;
    std::uint64_t generation = 0;
    bool cancelled = false;
    void approve(
      std::span<const std::uint8_t> value,
      std::uint64_t approved_generation
    ) override {
      pin.assign(value.begin(), value.end());
      generation = approved_generation;
    }
    void cancel(std::uint64_t) noexcept override {
      cancelled = true;
    }
  };

  nlohmann::json body_json(const response &value) {
    return nlohmann::json::parse(
      std::string(value.body.begin(), value.body.end()));
  }

  request wire(
    std::string method,
    std::string path,
    const nlohmann::json &body = nullptr
  ) {
    request result {
      .method = std::move(method),
      .path = std::move(path),
      .source = source
    };
    if (!body.is_null()) {
      const auto text = body.dump();
      result.body.assign(text.begin(), text.end());
      result.headers.emplace_back("Content-Type", "application/json");
    }
    return result;
  }

  struct harness {
    bytes32 client_private {};
    bytes32 client_public {};
    bytes32 client_nonce {};
    bytes32 client_certificate_hash {};
    nlohmann::json create_body;
    pairing_service service;
    router routes;
    pairing_service::steady_clock::time_point monotonic {};
    pairing_service::wall_clock::time_point wall {
      std::chrono::sys_days {std::chrono::year {2026}/7/24}
    };

    harness():
        client_public(x25519_public_key(client_private)),
        create_body({
          {"clientCertificateSha256", base64url_encode(client_certificate_hash)},
          {"clientEphemeralKey", base64url_encode(client_public)},
          {"clientNonce", base64url_encode(client_nonce)},
          {"device", {{"name", "Phone"}, {"platform", "android"}}},
          {"requestId", request_id},
          {"version", 1}
        }),
        service(
          service_config {
            .host_unique_id = "53beb7ec-9788-4c23-861a-061f153029a5",
            .host_certificate_der = bytes {1, 2, 3}
          },
          [counter = std::uint8_t {1}](std::size_t size) mutable {
            bytes result(size);
            for (auto &value : result) value = counter++;
            return result;
          }),
        routes(service) {
      client_private[0] = 7;
      client_public = x25519_public_key(client_private);
      create_body["clientEphemeralKey"] = base64url_encode(client_public);
    }

    response create() {
      return routes.handle(
        wire("POST", "/ligase/v1/pairing/requests", create_body),
        monotonic,
        wall
      );
    }
  };
}

TEST(AttendedPairingHttp, CreateStatusSourceAndCancelAreFailClosed) {
  harness value;
  const auto created = value.create();
  ASSERT_EQ(created.status, 201);
  const auto created_json = body_json(created);
  const auto token = created_json.at("requestToken").get<std::string>();

  auto status = wire(
    "GET", std::string("/ligase/v1/pairing/requests/") + request_id);
  status.headers.emplace_back("Authorization", "Bearer " + token);
  ASSERT_EQ(value.routes.handle(status, value.monotonic, value.wall).status, 200);

  auto wrong_source = status;
  wrong_source.source.address = "192.0.2.11";
  EXPECT_EQ(
    value.routes.handle(wrong_source, value.monotonic, value.wall).status, 401);

  auto cancel = status;
  cancel.method = "DELETE";
  EXPECT_EQ(value.routes.handle(cancel, value.monotonic, value.wall).status, 204);
  const auto cancelled =
    body_json(value.routes.handle(status, value.monotonic, value.wall));
  EXPECT_EQ(cancelled.at("state"), "cancelled");
  EXPECT_EQ(value.create().status, 409);
}

TEST(AttendedPairingHttp, CanonicalRequestIdAcceptsFrozenUuidVersions) {
  harness value;
  value.create_body["requestId"] =
    "9dbbb480-9ef1-1e9e-bb1f-0c1d42dff8e4";
  EXPECT_EQ(value.create().status, 201);
}

TEST(AttendedPairingHttp, ContentTypeDoesNotNormalizeInvalidTokenWhitespace) {
  harness value;
  auto input = wire(
    "POST", "/ligase/v1/pairing/requests", value.create_body);
  input.headers.front().second = "application / json";
  EXPECT_EQ(
    value.routes.handle(input, value.monotonic, value.wall).status, 415);
}

TEST(AttendedPairingHttp, DeviceNameRejectsUntrimmedUnicodeWhitespace) {
  harness value;
  value.create_body["device"]["name"] = "\u00a0Phone";
  const auto response = value.create();
  ASSERT_EQ(response.status, 400);
  EXPECT_EQ(body_json(response), (nlohmann::json {
    {"code", "invalidRequest"},
    {"detail", "invalidValue"},
    {"path", "/device/name"}
  }));
}

TEST(AttendedPairingHttp, EnvelopeHeldApprovalAndPairCommitUseOneGeneration) {
  harness value;
  const auto created = body_json(value.create());
  const auto token = created.at("requestToken").get<std::string>();

  nlohmann::json response_without_token = created;
  response_without_token.erase("requestToken");
  const auto transcript = jcs({
    {"request", value.create_body},
    {"response", response_without_token}
  });
  const bytes transcript_bytes(transcript.begin(), transcript.end());
  const auto transcript_hash = sha256(transcript_bytes);
  const auto host_public =
    base64url_decode_32(created.at("hostEphemeralKey").get<std::string>());
  const auto host_nonce =
    base64url_decode_32(created.at("hostNonce").get<std::string>());
  const auto shared = x25519_shared_secret(value.client_private, host_public);
  const auto key = derive_pairing_key(
    shared, value.client_nonce, host_nonce, transcript_hash);
  bytes12 nonce {};
  nonce[0] = 9;
  const bytes plaintext {
    '{', '"', 'l', 'e', 'g', 'a', 'c', 'y', 'P', 'i',
    'n', '"', ':', '"', '1', '2', '3', '4', '"', '}'
  };
  const auto ciphertext =
    chacha20_poly1305_encrypt(key, nonce, transcript_hash, plaintext);
  auto envelope = wire(
    "PUT",
    std::string("/ligase/v1/pairing/requests/") + request_id + "/envelope",
    {
      {"ciphertext", base64url_encode(ciphertext)},
      {"nonce", base64url_encode(nonce)}
    });
  envelope.headers.emplace_back("Authorization", "Bearer " + token);
  ASSERT_EQ(
    value.routes.handle(envelope, value.monotonic, value.wall).status, 204);

  adapter held;
  value.service.bind_held_getservercert(
    request_id, value.client_certificate_hash, source, held, value.monotonic);

  auto listing = wire("GET", "/ligase/v1/pairing/requests");
  listing.loopback = true;
  const auto list_json =
    body_json(value.routes.handle(listing, value.monotonic, value.wall));
  ASSERT_TRUE(list_json["requests"][0]["readyForApproval"]);

  auto allow = wire(
    "POST",
    std::string("/ligase/v1/pairing/requests/") + request_id + "/allow");
  allow.loopback = true;
  ASSERT_EQ(value.routes.handle(allow, value.monotonic, value.wall).status, 202);
  EXPECT_EQ(held.pin, (bytes {'1', '2', '3', '4'}));
  ASSERT_GT(held.generation, 0u);

  value.service.commit_paired(request_id, held.generation);
  auto status = wire(
    "GET", std::string("/ligase/v1/pairing/requests/") + request_id);
  status.headers.emplace_back("Authorization", "Bearer " + token);
  EXPECT_EQ(body_json(
    value.routes.handle(status, value.monotonic, value.wall))["state"], "paired");
}

TEST(AttendedPairingHttp, InvalidEnvelopeDoesNotReserveReplayMetadata) {
  harness value;
  const auto created = body_json(value.create());
  const auto token = created.at("requestToken").get<std::string>();

  nlohmann::json response_without_token = created;
  response_without_token.erase("requestToken");
  const auto transcript = jcs({
    {"request", value.create_body},
    {"response", response_without_token}
  });
  const bytes transcript_bytes(transcript.begin(), transcript.end());
  const auto transcript_hash = sha256(transcript_bytes);
  const auto host_public =
    base64url_decode_32(created.at("hostEphemeralKey").get<std::string>());
  const auto host_nonce =
    base64url_decode_32(created.at("hostNonce").get<std::string>());
  const auto shared = x25519_shared_secret(value.client_private, host_public);
  const auto key = derive_pairing_key(
    shared, value.client_nonce, host_nonce, transcript_hash);
  bytes12 nonce {};
  nonce[0] = 17;
  const bytes plaintext {
    '{', '"', 'l', 'e', 'g', 'a', 'c', 'y', 'P', 'i',
    'n', '"', ':', '"', '4', '3', '2', '1', '"', '}'
  };
  const auto valid_ciphertext =
    chacha20_poly1305_encrypt(key, nonce, transcript_hash, plaintext);

  auto make_envelope = [&](const bytes &ciphertext) {
    auto result = wire(
      "PUT",
      std::string("/ligase/v1/pairing/requests/") + request_id + "/envelope",
      {
        {"ciphertext", base64url_encode(ciphertext)},
        {"nonce", base64url_encode(nonce)}
      });
    result.headers.emplace_back("Authorization", "Bearer " + token);
    return result;
  };

  auto invalid_ciphertext = valid_ciphertext;
  invalid_ciphertext.back() ^= 1;
  const auto rejected = value.routes.handle(
    make_envelope(invalid_ciphertext), value.monotonic, value.wall);
  ASSERT_EQ(rejected.status, 400);
  EXPECT_EQ(body_json(rejected), (nlohmann::json {
    {"code", "invalidEnvelope"},
    {"detail", "authenticationFailed"}
  }));

  EXPECT_EQ(
    value.routes.handle(
      make_envelope(valid_ciphertext), value.monotonic, value.wall).status,
    204);
  EXPECT_EQ(
    value.routes.handle(
      make_envelope(valid_ciphertext), value.monotonic, value.wall).status,
    204);

  auto nonce_reuse_ciphertext = valid_ciphertext;
  nonce_reuse_ciphertext.front() ^= 1;
  EXPECT_EQ(
    value.routes.handle(
      make_envelope(nonce_reuse_ciphertext), value.monotonic, value.wall).status,
    409);

  auto other_nonce_envelope = make_envelope(valid_ciphertext);
  auto other_nonce_body = body_json(response {
    .body = other_nonce_envelope.body
  });
  bytes12 other_nonce = nonce;
  other_nonce[1] = 1;
  other_nonce_body["nonce"] = base64url_encode(other_nonce);
  const auto other_nonce_text = other_nonce_body.dump();
  other_nonce_envelope.body.assign(
    other_nonce_text.begin(), other_nonce_text.end());
  EXPECT_EQ(
    value.routes.handle(
      other_nonce_envelope, value.monotonic, value.wall).status,
    409);

  auto cancel = wire(
    "DELETE", std::string("/ligase/v1/pairing/requests/") + request_id);
  cancel.headers.emplace_back("Authorization", "Bearer " + token);
  ASSERT_EQ(
    value.routes.handle(cancel, value.monotonic, value.wall).status, 204);
  EXPECT_EQ(
    value.routes.handle(
      make_envelope(valid_ciphertext), value.monotonic, value.wall).status,
    204);
}

TEST(AttendedPairingHttp, StrictParserReturnsFrozenMachineErrors) {
  harness value;
  auto unsupported = wire(
    "POST", "/ligase/v1/pairing/requests", value.create_body);
  unsupported.headers.clear();
  auto response = value.routes.handle(
    unsupported, value.monotonic, value.wall);
  ASSERT_EQ(response.status, 415);
  EXPECT_EQ(body_json(response), (nlohmann::json {
    {"code", "unsupportedMediaType"}
  }));

  auto duplicate = wire(
    "POST", "/ligase/v1/pairing/requests", value.create_body);
  const std::string duplicate_json =
    R"({"clientCertificateSha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",)"
    R"("clientEphemeralKey":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",)"
    R"("clientNonce":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",)"
    R"("device":{"name":"Phone","name":"Other","platform":"android"},)"
    R"("requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","version":1})";
  duplicate.body.assign(duplicate_json.begin(), duplicate_json.end());
  response = value.routes.handle(duplicate, value.monotonic, value.wall);
  ASSERT_EQ(response.status, 400);
  EXPECT_EQ(body_json(response), (nlohmann::json {
    {"code", "invalidRequest"},
    {"detail", "duplicateField"},
    {"path", "/device/name"}
  }));

  auto invalid_type = wire(
    "POST", "/ligase/v1/pairing/requests", value.create_body);
  auto typed = value.create_body;
  typed["version"] = "1";
  const auto typed_text = typed.dump();
  invalid_type.body.assign(typed_text.begin(), typed_text.end());
  response = value.routes.handle(invalid_type, value.monotonic, value.wall);
  ASSERT_EQ(response.status, 400);
  EXPECT_EQ(body_json(response), (nlohmann::json {
    {"code", "invalidRequest"},
    {"detail", "invalidType"},
    {"path", "/version"}
  }));

  auto unknown = wire(
    "POST", "/ligase/v1/pairing/requests", value.create_body);
  auto with_unknown = value.create_body;
  with_unknown["~later"] = true;
  with_unknown["/first"] = true;
  const auto unknown_text = with_unknown.dump();
  unknown.body.assign(unknown_text.begin(), unknown_text.end());
  response = value.routes.handle(unknown, value.monotonic, value.wall);
  ASSERT_EQ(response.status, 400);
  EXPECT_EQ(body_json(response), (nlohmann::json {
    {"code", "invalidRequest"},
    {"detail", "unknownField"},
    {"path", "/~0later"}
  }));
}

TEST(AttendedPairingHttp, LookupExpiryAndLoopbackPrecedenceAreStable) {
  harness value;
  auto unknown = wire(
    "DELETE",
    "/ligase/v1/pairing/requests/11111111-1111-4111-8111-111111111111");
  ASSERT_EQ(
    value.routes.handle(unknown, value.monotonic, value.wall).status, 404);

  const auto created = body_json(value.create());
  const auto token = created.at("requestToken").get<std::string>();
  auto allow = wire(
    "POST",
    std::string("/ligase/v1/pairing/requests/") + request_id + "/allow");
  allow.loopback = true;
  auto response = value.routes.handle(allow, value.monotonic, value.wall);
  ASSERT_EQ(response.status, 409);
  EXPECT_EQ(body_json(response), (nlohmann::json {
    {"code", "invalidState"},
    {"currentState", "pending"},
    {"detail", "notReadyForApproval"}
  }));

  auto list = wire("GET", "/ligase/v1/pairing/requests");
  EXPECT_EQ(value.routes.handle(list, value.monotonic, value.wall).status, 404);

  auto status = wire(
    "GET", std::string("/ligase/v1/pairing/requests/") + request_id);
  status.headers.emplace_back("Authorization", "Bearer " + token);
  const auto expired_now = value.monotonic + std::chrono::seconds(121);
  response = value.routes.handle(status, expired_now, value.wall);
  ASSERT_EQ(response.status, 200);
  EXPECT_EQ(body_json(response).at("state"), "expired");

  auto late_envelope = wire(
    "PUT",
    std::string("/ligase/v1/pairing/requests/") + request_id + "/envelope");
  late_envelope.headers.emplace_back("Authorization", "Bearer " + token);
  response = value.routes.handle(late_envelope, expired_now, value.wall);
  ASSERT_EQ(response.status, 410);
  EXPECT_EQ(body_json(response), (nlohmann::json {
    {"code", "requestExpired"}
  }));
}
