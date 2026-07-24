/**
 * @file tests/unit/ligase/pairing/domain/test_attended_pairing.cpp
 * @brief Fixed standards vectors and negative tests for the pure P2-A module.
 */

#include <gtest/gtest.h>

#include <algorithm>
#include <fstream>
#include <ranges>
#include <set>

#include "src/ligase/pairing/domain/attended_pairing.h"
#include "src/utility.h"

namespace {
  using attended_pairing::bytes;
  using attended_pairing::bytes12;
  using attended_pairing::bytes32;

  template<std::size_t Size>
  std::array<std::uint8_t, Size> hex_array(std::string_view value) {
    if (value.size() != Size * 2) throw std::invalid_argument("wrong test vector size");
    const auto nibble = [](char character) -> std::uint8_t {
      if (character >= '0' && character <= '9') return character - '0';
      if (character >= 'a' && character <= 'f') return character - 'a' + 10;
      if (character >= 'A' && character <= 'F') return character - 'A' + 10;
      throw std::invalid_argument("invalid test vector hex");
    };
    std::array<std::uint8_t, Size> result {};
    for (std::size_t index = 0; index < Size; ++index) {
      result[index] = static_cast<std::uint8_t>(
        (nibble(value[index * 2]) << 4) | nibble(value[index * 2 + 1]));
    }
    return result;
  }

  std::string hex(std::span<const std::uint8_t> value) {
    static constexpr char alphabet[] = "0123456789abcdef";
    std::string result;
    result.reserve(value.size() * 2);
    for (const auto byte : value) {
      result.push_back(alphabet[byte >> 4]);
      result.push_back(alphabet[byte & 0x0f]);
    }
    return result;
  }
}

TEST(AttendedPairingCrypto, Base64UrlRejectsPaddingAndNonZeroTrailingBits) {
  const bytes value {0xfb, 0xef, 0xff};
  EXPECT_EQ(attended_pairing::base64url_encode(value), "--__");
  EXPECT_EQ(attended_pairing::base64url_decode("--__"), value);
  EXPECT_THROW((void) attended_pairing::base64url_decode("Zg=="),
               attended_pairing::protocol_error);
  EXPECT_THROW((void) attended_pairing::base64url_decode("Zh"),
               attended_pairing::protocol_error);
}

TEST(AttendedPairingCrypto, CanonicalJsonUsesStablePropertyOrderAndSafeIntegers) {
  const auto value = nlohmann::json::parse(
    R"({"response":{"version":1,"hostNonce":"é"},"request":{"version":1,"device":{"platform":"android","name":"Phone"}}})");
  EXPECT_EQ(
    attended_pairing::jcs(value),
    R"({"request":{"device":{"name":"Phone","platform":"android"},"version":1},"response":{"hostNonce":"é","version":1}})");
  EXPECT_THROW((void) attended_pairing::jcs(nlohmann::json::parse(R"({"version":1.0})")),
               attended_pairing::protocol_error);
  EXPECT_THROW((void) attended_pairing::jcs(nlohmann::json(9007199254740992ULL)),
               attended_pairing::protocol_error);
}

TEST(AttendedPairingCrypto, X25519MatchesRfc7748) {
  const auto alice_private = hex_array<32>(
    "77076d0a7318a57d3c16c17251b26645"
    "df4c2f87ebc0992ab177fba51db92c2a");
  const auto alice_public = hex_array<32>(
    "8520f0098930a754748b7ddcb43ef75a"
    "0dbf3a0d26381af4eba4a98eaa9b4e6a");
  const auto bob_public = hex_array<32>(
    "de9edb7d7b7dc1b4d35b61c2ece43537"
    "3f8343c85b78674dadfc7e146f882b4f");
  const auto shared = hex_array<32>(
    "4a5d9d5ba4ce2de1728e3bf480350f25"
    "e07e21c947d19e3376f09b3c1e161742");

  EXPECT_EQ(attended_pairing::x25519_public_key(alice_private), alice_public);
  EXPECT_EQ(attended_pairing::x25519_shared_secret(alice_private, bob_public), shared);

  bytes32 zero {};
  EXPECT_THROW((void) attended_pairing::x25519_shared_secret(alice_private, zero),
               attended_pairing::protocol_error);
}

TEST(AttendedPairingCrypto, HkdfMatchesRfc5869CaseOne) {
  const bytes ikm(22, 0x0b);
  const auto salt_array = hex_array<13>("000102030405060708090a0b0c");
  const auto info_array = hex_array<10>("f0f1f2f3f4f5f6f7f8f9");
  const bytes salt(salt_array.begin(), salt_array.end());
  const bytes info(info_array.begin(), info_array.end());
  EXPECT_EQ(
    hex(attended_pairing::hkdf_sha256(ikm, salt, info)),
    "3cb25f25faacd57a90434f64d0362f2a"
    "2d2d0a90cf1a5a4c5db02d56ecc4c5bf");
}

TEST(AttendedPairingCrypto, ChaChaAuthenticatesCiphertextAndAad) {
  const auto key = hex_array<32>(
    "000102030405060708090a0b0c0d0e0f"
    "101112131415161718191a1b1c1d1e1f");
  const auto nonce = hex_array<12>("000000000000004a00000000");
  const bytes aad {'a', 'a', 'd'};
  const bytes plaintext {'1', '2', '3', '4'};
  const auto encrypted =
    attended_pairing::chacha20_poly1305_encrypt(key, nonce, aad, plaintext);

  EXPECT_EQ(
    attended_pairing::chacha20_poly1305_decrypt(key, nonce, aad, encrypted),
    plaintext);
  auto wrong_aad = aad;
  wrong_aad[0] = 'b';
  EXPECT_THROW(
    (void) attended_pairing::chacha20_poly1305_decrypt(key, nonce, wrong_aad, encrypted),
    attended_pairing::protocol_error);
}

TEST(AttendedPairingCrypto, SafetyCodeHasFrozenAlphabetAndShape) {
  bytes32 pairing_key {};
  bytes32 transcript_hash {};
  for (std::size_t index = 0; index < 32; ++index) {
    pairing_key[index] = static_cast<std::uint8_t>(index);
    transcript_hash[index] = static_cast<std::uint8_t>(31 - index);
  }
  EXPECT_EQ(attended_pairing::derive_safety_code(pairing_key, transcript_hash),
            "MFQF-EVYD");
}

namespace {
  class recording_legacy_adapter final:
      public attended_pairing::legacy_pairing_adapter {
  public:
    void approve(
      std::span<const std::uint8_t> ascii_pin,
      std::uint64_t generation
    ) override {
      approved_pin.assign(ascii_pin.begin(), ascii_pin.end());
      approved_generation = generation;
    }

    void cancel(std::uint64_t generation) noexcept override {
      cancelled_generation = generation;
    }

    bytes approved_pin;
    std::uint64_t approved_generation = 0;
    std::uint64_t cancelled_generation = 0;
  };
}

TEST(AttendedPairingLifetime, ApprovalRequiresEnvelopeAndHeldLegacySession) {
  const auto created = attended_pairing::request_lifetime::clock::now();
  attended_pairing::request_lifetime lifetime(created);
  recording_legacy_adapter adapter;
  attended_pairing::secret_owner pin(bytes {'1', '2', '3', '4'});

  EXPECT_EQ(lifetime.allow(adapter, pin),
            attended_pairing::internal_action_result::not_ready);
  EXPECT_EQ(lifetime.set_envelope_verified(),
            attended_pairing::internal_action_result::changed);
  EXPECT_FALSE(lifetime.ready_for_approval());
  EXPECT_EQ(lifetime.bind_held_session(),
            attended_pairing::internal_action_result::changed);
  EXPECT_TRUE(lifetime.ready_for_approval());
  EXPECT_EQ(lifetime.ready_event_count(), 1u);
  EXPECT_EQ(lifetime.allow(adapter, pin),
            attended_pairing::internal_action_result::changed);
  EXPECT_TRUE(lifetime.ready_for_approval());
  EXPECT_EQ(lifetime.ready_event_count(), 1u);
  EXPECT_TRUE(pin.empty());
  EXPECT_EQ(adapter.approved_pin, (bytes {'1', '2', '3', '4'}));
  EXPECT_EQ(lifetime.state(), attended_pairing::request_state::approved);
}

TEST(AttendedPairingLifetime, CancellationInvalidatesAsyncPairCommitGeneration) {
  const auto created = attended_pairing::request_lifetime::clock::now();
  attended_pairing::request_lifetime lifetime(created);
  recording_legacy_adapter adapter;
  attended_pairing::secret_owner pin(bytes {'1', '2', '3', '4'});
  lifetime.set_envelope_verified();
  lifetime.bind_held_session();
  ASSERT_EQ(lifetime.allow(adapter, pin),
            attended_pairing::internal_action_result::changed);
  const auto approved_generation = lifetime.generation();

  EXPECT_EQ(lifetime.cancel(&adapter),
            attended_pairing::internal_action_result::changed);
  EXPECT_EQ(lifetime.commit_paired(approved_generation),
            attended_pairing::internal_action_result::stale_generation);
  EXPECT_EQ(lifetime.state(), attended_pairing::request_state::cancelled);
  EXPECT_NE(adapter.cancelled_generation, 0u);
}

TEST(AttendedPairingLifetime, MonotonicExpiryDoesNotExtendOnReadinessChanges) {
  const auto created = attended_pairing::request_lifetime::clock::now();
  attended_pairing::request_lifetime lifetime(created, std::chrono::seconds(120));
  const auto deadline = lifetime.deadline();
  lifetime.set_envelope_verified();
  lifetime.bind_held_session();
  EXPECT_EQ(lifetime.deadline(), deadline);
  EXPECT_EQ(lifetime.expire(deadline - std::chrono::milliseconds(1)),
            attended_pairing::internal_action_result::unchanged);
  EXPECT_EQ(lifetime.expire(deadline),
            attended_pairing::internal_action_result::changed);
  EXPECT_EQ(lifetime.state(), attended_pairing::request_state::expired);
}

TEST(AttendedPairingEnvelope, ReplayMetadataDistinguishesAllThreeBranches) {
  attended_pairing::envelope_replay_guard guard;
  bytes12 nonce {};
  bytes32 hash {};
  EXPECT_EQ(guard.observe(nonce, hash),
            attended_pairing::envelope_replay_result::first_upload);
  EXPECT_EQ(guard.observe(nonce, hash),
            attended_pairing::envelope_replay_result::exact_replay);
  hash[0] = 1;
  EXPECT_EQ(guard.observe(nonce, hash),
            attended_pairing::envelope_replay_result::nonce_reuse);
  nonce[0] = 1;
  EXPECT_EQ(guard.observe(nonce, hash),
            attended_pairing::envelope_replay_result::envelope_conflict);
  guard.clear();
  EXPECT_FALSE(guard.has_metadata());
}

TEST(AttendedPairingEnvelope, LegacyPinPlaintextIsExactTwentyByteTemplate) {
  const bytes valid {
    '{', '"', 'l', 'e', 'g', 'a', 'c', 'y', 'P', 'i',
    'n', '"', ':', '"', '1', '2', '3', '4', '"', '}'
  };
  EXPECT_EQ(
    attended_pairing::validate_legacy_pin_plaintext(valid),
    (std::array<char, 4> {'1', '2', '3', '4'}));
  auto invalid_digit = valid;
  invalid_digit[17] = 'x';
  EXPECT_THROW(
    (void) attended_pairing::validate_legacy_pin_plaintext(invalid_digit),
    attended_pairing::protocol_error);
  auto extra = valid;
  extra.push_back(' ');
  EXPECT_THROW(
    (void) attended_pairing::validate_legacy_pin_plaintext(extra),
    attended_pairing::protocol_error);
}

TEST(AttendedPairingVectors, PositiveFixtureIsMechanicallyRecomputed) {
  std::ifstream input(
    std::string(SUNSHINE_SOURCE_DIR)
      + "/tests/fixtures/attended-pairing-v1-vectors.json");
  ASSERT_TRUE(input.good());
  nlohmann::json root;
  input >> root;
  ASSERT_EQ(root.at("schemaVersion"), 1);
  ASSERT_EQ(root.at("draft"), "attended-pairing-v1");
  const auto &cases = root.at("cases");
  const auto positive = std::ranges::find_if(cases, [](const auto &candidate) {
    return candidate.at("operation") == "ligasePositive";
  });
  ASSERT_NE(positive, cases.end());
  const auto &value = *positive;
  const auto field = [&value](std::string_view name) {
    return value.at(name).get<std::string>();
  };

  const auto client_private =
    attended_pairing::base64url_decode_32(field("clientPrivateKeyBase64url"));
  const auto host_private =
    attended_pairing::base64url_decode_32(field("hostPrivateKeyBase64url"));
  const auto client_nonce =
    attended_pairing::base64url_decode_32(field("clientNonceBase64url"));
  const auto host_nonce =
    attended_pairing::base64url_decode_32(field("hostNonceBase64url"));
  const auto transcript =
    attended_pairing::base64url_decode(field("transcriptJcsBase64url"));
  const auto transcript_hash = attended_pairing::sha256(transcript);

  EXPECT_EQ(
    attended_pairing::x25519_public_key(client_private),
    attended_pairing::base64url_decode_32(field("clientPublicKeyBase64url")));
  EXPECT_EQ(
    attended_pairing::x25519_public_key(host_private),
    attended_pairing::base64url_decode_32(field("hostPublicKeyBase64url")));
  const auto shared =
    attended_pairing::x25519_shared_secret(client_private, attended_pairing::x25519_public_key(host_private));
  EXPECT_EQ(
    shared,
    attended_pairing::base64url_decode_32(field("sharedSecretBase64url")));
  EXPECT_EQ(
    transcript_hash,
    attended_pairing::base64url_decode_32(field("transcriptHashBase64url")));

  const auto pairing_key =
    attended_pairing::derive_pairing_key(shared, client_nonce, host_nonce, transcript_hash);
  EXPECT_EQ(
    pairing_key,
    attended_pairing::base64url_decode_32(field("pairingKeyBase64url")));
  EXPECT_EQ(
    attended_pairing::derive_safety_code(pairing_key, transcript_hash),
    field("safetyCode"));

  const auto envelope_nonce =
    attended_pairing::base64url_decode_12(field("envelopeNonceBase64url"));
  const auto plaintext =
    attended_pairing::base64url_decode(field("envelopePlaintextJcsBase64url"));
  const auto ciphertext =
    attended_pairing::base64url_decode(field("envelopeCiphertextAndTagBase64url"));
  EXPECT_EQ(
    attended_pairing::chacha20_poly1305_decrypt(
      pairing_key, envelope_nonce, transcript_hash, ciphertext),
    plaintext);
  EXPECT_EQ(
    attended_pairing::validate_legacy_pin_plaintext(plaintext),
    (std::array<char, 4> {'1', '2', '3', '4'}));

  const auto request_token =
    attended_pairing::base64url_decode(field("requestTokenBase64url"));
  EXPECT_EQ(request_token.size(), 32u);
  EXPECT_EQ(
    attended_pairing::sha256(request_token),
    attended_pairing::base64url_decode_32(field("requestTokenSha256Base64url")));
}

TEST(AttendedPairingVectors, BehaviorAndRaceCoverageIsUniqueAndExecutable) {
  std::ifstream input(
    std::string(SUNSHINE_SOURCE_DIR)
      + "/tests/fixtures/attended-pairing-v1-vectors.json");
  ASSERT_TRUE(input.good());
  nlohmann::json root;
  input >> root;

  std::set<std::string> ids;
  std::set<std::string> operations;
  std::vector<std::string> ordered_ids;
  for (const auto &value: root.at("cases")) {
    const auto id = value.at("id").get<std::string>();
    EXPECT_TRUE(ids.emplace(id).second);
    ordered_ids.push_back(id);
    operations.emplace(value.at("operation").get<std::string>());
    if (value.contains("context")) {
      const auto &context = value.at("context");
      const auto state = context.at("state").get<std::string>();
      const auto validated = context.at("validatedEnvelope").get<bool>();
      const auto has_nonce =
        !context.at("replayNonceBase64url").get<std::string>().empty();
      const auto has_hash =
        !context.at("replayCanonicalEnvelopeHashBase64url").get<std::string>().empty();
      EXPECT_EQ(has_nonce, has_hash) << id;
      EXPECT_EQ(validated, has_nonce) << id;
      const auto ready = context.at("readyForApproval").get<bool>();
      EXPECT_EQ(ready, context.at("readyEventEmitted").get<bool>()) << id;
      if (ready) {
        EXPECT_TRUE(validated) << id;
        EXPECT_TRUE(context.at("certificateFingerprintMatched").get<bool>()) << id;
        EXPECT_TRUE(context.at("readyEventEmitted").get<bool>()) << id;
      }
      if (state == "pending" || state == "approved") {
        if (ready || state == "approved") {
          EXPECT_TRUE(context.at("heldGetservercert").get<bool>()) << id;
        }
      }
      if (state == "paired") {
        EXPECT_TRUE(ready) << id;
        EXPECT_FALSE(context.at("heldGetservercert").get<bool>()) << id;
        EXPECT_TRUE(context.at("pairedDeviceCommitted").get<bool>()) << id;
      }
    }
  }
  EXPECT_EQ(
    root.at("coverageManifest").at("version").get<int>(),
    1);
  EXPECT_EQ(
    ordered_ids,
    root.at("coverageManifest").at("requiredIds").get<std::vector<std::string>>());
  EXPECT_TRUE(std::ranges::is_sorted(ordered_ids));
  EXPECT_EQ(
    operations,
    (std::set<std::string> {
      "androidCancel", "cancel", "create", "envelope", "ligasePositive",
      "loopbackAllow", "loopbackList", "loopbackReject",
      "primitiveChaCha20Poly1305", "primitiveHkdfSha256", "primitiveJcs",
      "primitiveX25519", "race", "status"
    }));

  for (const auto &value: root.at("cases")) {
    const auto operation = value.at("operation").get<std::string>();
    if (operation == "primitiveX25519") {
      const auto private_key = attended_pairing::base64url_decode_32(
        value.at("privateKeyBase64url").get<std::string>());
      const auto peer_key = attended_pairing::base64url_decode_32(
        value.at("peerPublicKeyBase64url").get<std::string>());
      EXPECT_EQ(
        attended_pairing::x25519_public_key(private_key),
        attended_pairing::base64url_decode_32(
          value.at("expectedPublicKeyBase64url").get<std::string>()));
      EXPECT_EQ(
        attended_pairing::x25519_shared_secret(private_key, peer_key),
        attended_pairing::base64url_decode_32(
          value.at("expectedSharedSecretBase64url").get<std::string>()));
    } else if (operation == "primitiveHkdfSha256") {
      EXPECT_EQ(
        attended_pairing::hkdf_sha256(
          attended_pairing::base64url_decode(value.at("ikmBase64url").get<std::string>()),
          attended_pairing::base64url_decode(value.at("saltBase64url").get<std::string>()),
          attended_pairing::base64url_decode(value.at("infoBase64url").get<std::string>())),
        attended_pairing::base64url_decode_32(
          value.at("expectedOkmBase64url").get<std::string>()));
    } else if (operation == "primitiveChaCha20Poly1305") {
      EXPECT_EQ(
        attended_pairing::chacha20_poly1305_encrypt(
          attended_pairing::base64url_decode_32(value.at("keyBase64url").get<std::string>()),
          attended_pairing::base64url_decode_12(value.at("nonceBase64url").get<std::string>()),
          attended_pairing::base64url_decode(value.at("aadBase64url").get<std::string>()),
          attended_pairing::base64url_decode(value.at("plaintextBase64url").get<std::string>())),
        attended_pairing::base64url_decode(
          value.at("expectedCiphertextAndTagBase64url").get<std::string>()));
    } else if (operation == "primitiveJcs") {
      const auto input_bytes = attended_pairing::base64url_decode(
        value.at("inputUtf8Base64url").get<std::string>());
      const std::string input_text(input_bytes.begin(), input_bytes.end());
      const auto expected = attended_pairing::base64url_decode(
        value.at("expectedJcsUtf8Base64url").get<std::string>());
      EXPECT_EQ(
        attended_pairing::jcs(nlohmann::json::parse(input_text)),
        std::string(expected.begin(), expected.end()));
    }
  }

  const auto created = attended_pairing::request_lifetime::clock::now();
  {
    attended_pairing::request_lifetime lifetime(created);
    EXPECT_EQ(
      lifetime.expire(lifetime.deadline()),
      attended_pairing::internal_action_result::changed);
    EXPECT_EQ(lifetime.state(), attended_pairing::request_state::expired);
  }
  {
    attended_pairing::request_lifetime lifetime(created);
    recording_legacy_adapter adapter;
    EXPECT_EQ(
      lifetime.cancel(&adapter),
      attended_pairing::internal_action_result::changed);
    EXPECT_EQ(lifetime.state(), attended_pairing::request_state::cancelled);
  }
  {
    attended_pairing::request_lifetime lifetime(created);
    recording_legacy_adapter adapter;
    attended_pairing::secret_owner pin(bytes {'1', '2', '3', '4'});
    ASSERT_EQ(
      lifetime.set_envelope_verified(),
      attended_pairing::internal_action_result::changed);
    ASSERT_EQ(
      lifetime.bind_held_session(),
      attended_pairing::internal_action_result::changed);
    ASSERT_EQ(
      lifetime.allow(adapter, pin),
      attended_pairing::internal_action_result::changed);
    const auto approved_generation = lifetime.generation();
    ASSERT_EQ(
      lifetime.cancel(&adapter),
      attended_pairing::internal_action_result::changed);
    EXPECT_EQ(
      lifetime.commit_paired(approved_generation),
      attended_pairing::internal_action_result::stale_generation);
    EXPECT_EQ(lifetime.state(), attended_pairing::request_state::cancelled);
  }
}
