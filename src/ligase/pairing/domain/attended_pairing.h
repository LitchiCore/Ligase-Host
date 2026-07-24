/**
 * @file src/ligase/pairing/domain/attended_pairing.h
 * @brief Pure cryptographic and canonical-encoding primitives for Ligase
 *        attended pairing v1.
 *
 * This module deliberately has no HTTP, nvhttp, persistence, or UI dependency.
 * Public routes and capability advertisement must not be connected until the
 * wire contract and conformance vectors are frozen.
 */
#pragma once

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <optional>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

#include <nlohmann/json.hpp>

namespace attended_pairing {
  using bytes = std::vector<std::uint8_t>;
  using bytes12 = std::array<std::uint8_t, 12>;
  using bytes32 = std::array<std::uint8_t, 32>;

  class protocol_error: public std::runtime_error {
  public:
    explicit protocol_error(std::string code);
    [[nodiscard]] const std::string &code() const noexcept;

  private:
    std::string code_;
  };

  class secret_owner {
  public:
    secret_owner() = default;
    explicit secret_owner(bytes value);
    ~secret_owner();
    secret_owner(const secret_owner &) = delete;
    secret_owner &operator=(const secret_owner &) = delete;
    secret_owner(secret_owner &&other) noexcept;
    secret_owner &operator=(secret_owner &&other) noexcept;

    [[nodiscard]] std::span<const std::uint8_t> view() const noexcept;
    [[nodiscard]] bool empty() const noexcept;
    void clear() noexcept;

  private:
    bytes value_;
  };

  enum class request_state {
    pending,
    approved,
    paired,
    rejected,
    cancelled,
    expired,
    failed
  };

  enum class internal_action_result {
    changed,
    unchanged,
    not_ready,
    invalid_state,
    stale_generation
  };

  enum class envelope_replay_result {
    first_upload,
    exact_replay,
    nonce_reuse,
    envelope_conflict
  };

  /**
   * Minimal seam implemented by the existing GameStream certificate exchange.
   * P2-B supplies the nvhttp adapter; P2-A never reads global one_time_pin.
   */
  class legacy_pairing_adapter {
  public:
    virtual ~legacy_pairing_adapter() = default;
    virtual void approve(std::span<const std::uint8_t> ascii_pin,
                         std::uint64_t generation) = 0;
    virtual void cancel(std::uint64_t generation) noexcept = 0;
  };

  /**
   * Per-request monotonic state primitive. Route-specific status codes and JSON
   * are intentionally outside this type.
   */
  class request_lifetime {
  public:
    using clock = std::chrono::steady_clock;

    explicit request_lifetime(
      clock::time_point created_at,
      std::chrono::seconds lifetime = std::chrono::seconds(120)
    );

    [[nodiscard]] request_state state() const;
    [[nodiscard]] std::uint64_t generation() const;
    [[nodiscard]] bool ready_for_approval() const;
    [[nodiscard]] std::uint32_t ready_event_count() const;
    [[nodiscard]] clock::time_point deadline() const noexcept;

    internal_action_result set_envelope_verified();
    internal_action_result bind_held_session();
    internal_action_result allow(legacy_pairing_adapter &adapter, secret_owner &pin);
    internal_action_result reject(legacy_pairing_adapter *adapter = nullptr);
    internal_action_result cancel(legacy_pairing_adapter *adapter = nullptr);
    internal_action_result commit_paired(std::uint64_t expected_generation);
    internal_action_result fail(std::uint64_t expected_generation);
    internal_action_result expire(clock::time_point now,
                                  legacy_pairing_adapter *adapter = nullptr);

  private:
    internal_action_result terminate_locked(
      request_state target,
      legacy_pairing_adapter *adapter
    );

    mutable std::mutex mutex_;
    request_state state_ = request_state::pending;
    std::uint64_t generation_ = 1;
    bool envelope_verified_ = false;
    bool held_session_bound_ = false;
    std::uint32_t ready_event_count_ = 0;
    clock::time_point deadline_;
  };

  /**
   * Non-secret replay metadata retained after encrypted envelope cleanup.
   * The digest is SHA-256(JCS(envelope)); full ciphertext is not retained.
   */
  class envelope_replay_guard {
  public:
    [[nodiscard]] envelope_replay_result observe(
      const bytes12 &nonce,
      const bytes32 &canonical_envelope_hash
    );
    [[nodiscard]] bool has_metadata() const noexcept;
    void clear() noexcept;

  private:
    std::optional<bytes12> nonce_;
    std::optional<bytes32> hash_;
  };

  [[nodiscard]] std::string base64url_encode(std::span<const std::uint8_t> value);
  [[nodiscard]] bytes base64url_decode(std::string_view value);
  [[nodiscard]] bytes32 base64url_decode_32(std::string_view value);
  [[nodiscard]] bytes12 base64url_decode_12(std::string_view value);

  /**
   * Canonicalizes a schema-validated attended-pairing value using the RFC 8785
   * subset used by v1. Contract property names are ASCII and numbers must be
   * JSON integer tokens in the I-JSON safe range.
   */
  [[nodiscard]] std::string jcs(const nlohmann::json &value);

  [[nodiscard]] bytes32 sha256(std::span<const std::uint8_t> value);
  [[nodiscard]] bytes32 hmac_sha256(
    std::span<const std::uint8_t> key,
    std::span<const std::uint8_t> value
  );
  [[nodiscard]] bytes32 hkdf_sha256(
    std::span<const std::uint8_t> ikm,
    std::span<const std::uint8_t> salt,
    std::span<const std::uint8_t> info
  );

  [[nodiscard]] bytes32 x25519_public_key(const bytes32 &private_key);
  [[nodiscard]] bytes32 x25519_shared_secret(
    const bytes32 &private_key,
    const bytes32 &peer_public_key
  );

  [[nodiscard]] bytes chacha20_poly1305_encrypt(
    const bytes32 &key,
    const bytes12 &nonce,
    std::span<const std::uint8_t> aad,
    std::span<const std::uint8_t> plaintext
  );
  [[nodiscard]] bytes chacha20_poly1305_decrypt(
    const bytes32 &key,
    const bytes12 &nonce,
    std::span<const std::uint8_t> aad,
    std::span<const std::uint8_t> ciphertext_and_tag
  );

  [[nodiscard]] bytes32 derive_pairing_key(
    const bytes32 &shared_secret,
    const bytes32 &client_nonce,
    const bytes32 &host_nonce,
    const bytes32 &transcript_hash
  );
  [[nodiscard]] std::string derive_safety_code(
    const bytes32 &pairing_key,
    const bytes32 &transcript_hash
  );

  [[nodiscard]] std::array<char, 4> validate_legacy_pin_plaintext(
    std::span<const std::uint8_t> plaintext
  );

  void secure_clear(std::span<std::uint8_t> value) noexcept;
}  // namespace attended_pairing
