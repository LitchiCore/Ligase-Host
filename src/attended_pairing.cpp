/**
 * @file src/attended_pairing.cpp
 * @brief Pure attended-pairing v1 cryptographic primitives.
 */

#include "attended_pairing.h"

#include <algorithm>
#include <limits>
#include <memory>

#include <openssl/crypto.h>
#include <openssl/evp.h>
#include <openssl/hmac.h>

namespace attended_pairing {
  namespace {
    constexpr std::uint64_t max_safe_integer = 9007199254740991ULL;
    constexpr std::string_view base64url_alphabet =
      "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    constexpr std::string_view base32_alphabet =
      "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    template<typename T, void (*Free)(T *)>
    using openssl_ptr = std::unique_ptr<T, decltype(Free)>;

    [[noreturn]] void throw_protocol(std::string code) {
      throw protocol_error(std::move(code));
    }

    bool is_ascii_key(std::string_view key) {
      return std::ranges::all_of(key, [](unsigned char value) {
        return value > 0 && value < 0x80;
      });
    }

    void append_jcs(const nlohmann::json &value, std::string &output) {
      if (value.is_object()) {
        output.push_back('{');
        bool first = true;
        for (const auto &[key, child] : value.items()) {
          if (!is_ascii_key(key)) throw_protocol("invalidCanonicalProperty");
          if (!first) output.push_back(',');
          first = false;
          output += nlohmann::json(key).dump(
            -1, ' ', false, nlohmann::json::error_handler_t::strict);
          output.push_back(':');
          append_jcs(child, output);
        }
        output.push_back('}');
        return;
      }
      if (value.is_array()) {
        output.push_back('[');
        for (std::size_t index = 0; index < value.size(); ++index) {
          if (index != 0) output.push_back(',');
          append_jcs(value[index], output);
        }
        output.push_back(']');
        return;
      }
      if (value.is_number_float()) throw_protocol("invalidCanonicalNumber");
      if (value.is_number_unsigned()) {
        if (value.get<std::uint64_t>() > max_safe_integer) throw_protocol("invalidCanonicalNumber");
      } else if (value.is_number_integer()) {
        const auto integer = value.get<std::int64_t>();
        if (integer < -static_cast<std::int64_t>(max_safe_integer)
            || integer > static_cast<std::int64_t>(max_safe_integer)) {
          throw_protocol("invalidCanonicalNumber");
        }
      }
      output += value.dump(-1, ' ', false, nlohmann::json::error_handler_t::strict);
    }

    bytes32 to_array32(std::span<const std::uint8_t> value) {
      if (value.size() != 32) throw_protocol("invalidLength");
      bytes32 result {};
      std::ranges::copy(value, result.begin());
      return result;
    }

    bytes12 to_array12(std::span<const std::uint8_t> value) {
      if (value.size() != 12) throw_protocol("invalidLength");
      bytes12 result {};
      std::ranges::copy(value, result.begin());
      return result;
    }
  }  // namespace

  protocol_error::protocol_error(std::string code):
      std::runtime_error(code),
      code_(std::move(code)) {
  }

  const std::string &protocol_error::code() const noexcept {
    return code_;
  }

  secret_owner::secret_owner(bytes value):
      value_(std::move(value)) {
  }

  secret_owner::~secret_owner() {
    clear();
  }

  secret_owner::secret_owner(secret_owner &&other) noexcept:
      value_(std::move(other.value_)) {
    other.clear();
  }

  secret_owner &secret_owner::operator=(secret_owner &&other) noexcept {
    if (this != &other) {
      clear();
      value_ = std::move(other.value_);
      other.clear();
    }
    return *this;
  }

  std::span<const std::uint8_t> secret_owner::view() const noexcept {
    return value_;
  }

  bool secret_owner::empty() const noexcept {
    return value_.empty();
  }

  void secret_owner::clear() noexcept {
    secure_clear(value_);
    value_.clear();
    value_.shrink_to_fit();
  }

  request_lifetime::request_lifetime(
    clock::time_point created_at,
    std::chrono::seconds lifetime
  ):
      deadline_(created_at + lifetime) {
    if (lifetime <= std::chrono::seconds::zero()) throw_protocol("invalidLifetime");
  }

  request_state request_lifetime::state() const {
    std::scoped_lock lock(mutex_);
    return state_;
  }

  std::uint64_t request_lifetime::generation() const {
    std::scoped_lock lock(mutex_);
    return generation_;
  }

  bool request_lifetime::ready_for_approval() const {
    std::scoped_lock lock(mutex_);
    return (state_ == request_state::pending || state_ == request_state::approved)
      && envelope_verified_
      && held_session_bound_;
  }

  std::uint32_t request_lifetime::ready_event_count() const {
    std::scoped_lock lock(mutex_);
    return ready_event_count_;
  }

  request_lifetime::clock::time_point request_lifetime::deadline() const noexcept {
    return deadline_;
  }

  internal_action_result request_lifetime::set_envelope_verified() {
    std::scoped_lock lock(mutex_);
    if (state_ != request_state::pending) return internal_action_result::invalid_state;
    if (envelope_verified_) return internal_action_result::unchanged;
    envelope_verified_ = true;
    if (held_session_bound_) ++ready_event_count_;
    return internal_action_result::changed;
  }

  internal_action_result request_lifetime::bind_held_session() {
    std::scoped_lock lock(mutex_);
    if (state_ != request_state::pending) return internal_action_result::invalid_state;
    if (held_session_bound_) return internal_action_result::unchanged;
    held_session_bound_ = true;
    if (envelope_verified_) ++ready_event_count_;
    return internal_action_result::changed;
  }

  envelope_replay_result envelope_replay_guard::observe(
    const bytes12 &nonce,
    const bytes32 &canonical_envelope_hash
  ) {
    if (!nonce_.has_value()) {
      nonce_ = nonce;
      hash_ = canonical_envelope_hash;
      return envelope_replay_result::first_upload;
    }
    if (CRYPTO_memcmp(nonce_->data(), nonce.data(), nonce.size()) == 0) {
      return CRYPTO_memcmp(
               hash_->data(),
               canonical_envelope_hash.data(),
               canonical_envelope_hash.size()) == 0
        ? envelope_replay_result::exact_replay
        : envelope_replay_result::nonce_reuse;
    }
    return envelope_replay_result::envelope_conflict;
  }

  bool envelope_replay_guard::has_metadata() const noexcept {
    return nonce_.has_value();
  }

  void envelope_replay_guard::clear() noexcept {
    if (nonce_) secure_clear(*nonce_);
    if (hash_) secure_clear(*hash_);
    nonce_.reset();
    hash_.reset();
  }

  internal_action_result request_lifetime::allow(
    legacy_pairing_adapter &adapter,
    secret_owner &pin
  ) {
    std::scoped_lock lock(mutex_);
    if (state_ == request_state::approved || state_ == request_state::paired) {
      return internal_action_result::unchanged;
    }
    if (state_ != request_state::pending) return internal_action_result::invalid_state;
    if (!envelope_verified_ || !held_session_bound_) {
      return internal_action_result::not_ready;
    }
    state_ = request_state::approved;
    ++generation_;
    try {
      adapter.approve(pin.view(), generation_);
    } catch (...) {
      state_ = request_state::failed;
      ++generation_;
      adapter.cancel(generation_);
      pin.clear();
      throw;
    }
    pin.clear();
    return internal_action_result::changed;
  }

  internal_action_result request_lifetime::terminate_locked(
    request_state target,
    legacy_pairing_adapter *adapter
  ) {
    state_ = target;
    ++generation_;
    if (adapter != nullptr) adapter->cancel(generation_);
    return internal_action_result::changed;
  }

  internal_action_result request_lifetime::reject(legacy_pairing_adapter *adapter) {
    std::scoped_lock lock(mutex_);
    if (state_ == request_state::rejected) return internal_action_result::unchanged;
    if (state_ != request_state::pending && state_ != request_state::approved) {
      return internal_action_result::invalid_state;
    }
    return terminate_locked(request_state::rejected, adapter);
  }

  internal_action_result request_lifetime::cancel(legacy_pairing_adapter *adapter) {
    std::scoped_lock lock(mutex_);
    if (state_ != request_state::pending && state_ != request_state::approved) {
      return internal_action_result::unchanged;
    }
    return terminate_locked(request_state::cancelled, adapter);
  }

  internal_action_result request_lifetime::commit_paired(
    std::uint64_t expected_generation
  ) {
    std::scoped_lock lock(mutex_);
    if (expected_generation != generation_) {
      return internal_action_result::stale_generation;
    }
    if (state_ == request_state::paired) return internal_action_result::unchanged;
    if (state_ != request_state::approved) return internal_action_result::invalid_state;
    state_ = request_state::paired;
    ++generation_;
    return internal_action_result::changed;
  }

  internal_action_result request_lifetime::fail(std::uint64_t expected_generation) {
    std::scoped_lock lock(mutex_);
    if (expected_generation != generation_) {
      return internal_action_result::stale_generation;
    }
    if (state_ == request_state::failed) return internal_action_result::unchanged;
    if (state_ != request_state::pending && state_ != request_state::approved) {
      return internal_action_result::invalid_state;
    }
    state_ = request_state::failed;
    ++generation_;
    return internal_action_result::changed;
  }

  internal_action_result request_lifetime::expire(
    clock::time_point now,
    legacy_pairing_adapter *adapter
  ) {
    std::scoped_lock lock(mutex_);
    if (now < deadline_) return internal_action_result::unchanged;
    if (state_ != request_state::pending && state_ != request_state::approved) {
      return internal_action_result::unchanged;
    }
    return terminate_locked(request_state::expired, adapter);
  }

  std::string base64url_encode(std::span<const std::uint8_t> value) {
    if (value.empty()) return {};
    std::string output;
    output.reserve((value.size() * 4 + 2) / 3);
    std::uint32_t accumulator = 0;
    int bits = 0;
    for (const auto byte : value) {
      accumulator = (accumulator << 8) | byte;
      bits += 8;
      while (bits >= 6) {
        bits -= 6;
        output.push_back(base64url_alphabet[(accumulator >> bits) & 0x3f]);
      }
    }
    if (bits != 0) {
      output.push_back(base64url_alphabet[(accumulator << (6 - bits)) & 0x3f]);
    }
    return output;
  }

  bytes base64url_decode(std::string_view value) {
    if (value.find('=') != std::string_view::npos) throw_protocol("nonCanonicalBase64url");
    if (value.size() % 4 == 1) throw_protocol("invalidBase64url");

    bytes output;
    output.reserve(value.size() * 3 / 4);
    std::uint32_t accumulator = 0;
    int bits = 0;
    for (const auto character : value) {
      const auto offset = base64url_alphabet.find(character);
      if (offset == std::string_view::npos) throw_protocol("invalidBase64url");
      accumulator = (accumulator << 6) | static_cast<std::uint32_t>(offset);
      bits += 6;
      if (bits >= 8) {
        bits -= 8;
        output.push_back(static_cast<std::uint8_t>((accumulator >> bits) & 0xff));
      }
    }
    if (bits != 0 && (accumulator & ((1u << bits) - 1u)) != 0) {
      throw_protocol("nonCanonicalBase64url");
    }
    if (base64url_encode(output) != value) throw_protocol("nonCanonicalBase64url");
    return output;
  }

  bytes32 base64url_decode_32(std::string_view value) {
    const auto decoded = base64url_decode(value);
    return to_array32(decoded);
  }

  bytes12 base64url_decode_12(std::string_view value) {
    const auto decoded = base64url_decode(value);
    return to_array12(decoded);
  }

  std::string jcs(const nlohmann::json &value) {
    std::string output;
    append_jcs(value, output);
    return output;
  }

  bytes32 sha256(std::span<const std::uint8_t> value) {
    bytes32 result {};
    unsigned int size = 0;
    openssl_ptr<EVP_MD_CTX, EVP_MD_CTX_free> context(
      EVP_MD_CTX_new(), EVP_MD_CTX_free);
    if (!context
        || EVP_DigestInit_ex(context.get(), EVP_sha256(), nullptr) != 1
        || EVP_DigestUpdate(context.get(), value.data(), value.size()) != 1
        || EVP_DigestFinal_ex(context.get(), result.data(), &size) != 1
        || size != result.size()) {
      throw_protocol("cryptoFailure");
    }
    return result;
  }

  bytes32 hmac_sha256(
    std::span<const std::uint8_t> key,
    std::span<const std::uint8_t> value
  ) {
    bytes32 result {};
    unsigned int size = 0;
    if (HMAC(
          EVP_sha256(),
          key.data(),
          static_cast<int>(key.size()),
          value.data(),
          value.size(),
          result.data(),
          &size) == nullptr
        || size != result.size()) {
      throw_protocol("cryptoFailure");
    }
    return result;
  }

  bytes32 hkdf_sha256(
    std::span<const std::uint8_t> ikm,
    std::span<const std::uint8_t> salt,
    std::span<const std::uint8_t> info
  ) {
    auto pseudo_random_key = hmac_sha256(salt, ikm);
    bytes expand_input(info.begin(), info.end());
    expand_input.push_back(1);
    auto output = hmac_sha256(pseudo_random_key, expand_input);
    secure_clear(pseudo_random_key);
    secure_clear(expand_input);
    return output;
  }

  bytes32 x25519_public_key(const bytes32 &private_key) {
    openssl_ptr<EVP_PKEY, EVP_PKEY_free> key(
      EVP_PKEY_new_raw_private_key_ex(
        nullptr, "X25519", nullptr, private_key.data(), private_key.size()),
      EVP_PKEY_free);
    bytes32 result {};
    std::size_t size = result.size();
    if (!key
        || EVP_PKEY_get_raw_public_key(key.get(), result.data(), &size) != 1
        || size != result.size()) {
      throw_protocol("cryptoFailure");
    }
    return result;
  }

  bytes32 x25519_shared_secret(
    const bytes32 &private_key,
    const bytes32 &peer_public_key
  ) {
    openssl_ptr<EVP_PKEY, EVP_PKEY_free> private_value(
      EVP_PKEY_new_raw_private_key_ex(
        nullptr, "X25519", nullptr, private_key.data(), private_key.size()),
      EVP_PKEY_free);
    openssl_ptr<EVP_PKEY, EVP_PKEY_free> peer_value(
      EVP_PKEY_new_raw_public_key_ex(
        nullptr, "X25519", nullptr, peer_public_key.data(), peer_public_key.size()),
      EVP_PKEY_free);
    openssl_ptr<EVP_PKEY_CTX, EVP_PKEY_CTX_free> context(
      private_value ? EVP_PKEY_CTX_new(private_value.get(), nullptr) : nullptr,
      EVP_PKEY_CTX_free);
    bytes32 result {};
    std::size_t size = result.size();
    if (!private_value || !peer_value || !context
        || EVP_PKEY_derive_init(context.get()) != 1
        || EVP_PKEY_derive_set_peer(context.get(), peer_value.get()) != 1
        || EVP_PKEY_derive(context.get(), result.data(), &size) != 1
        || size != result.size()
        || std::ranges::all_of(result, [](std::uint8_t value) { return value == 0; })) {
      secure_clear(result);
      throw_protocol("invalidSharedSecret");
    }
    return result;
  }

  bytes chacha20_poly1305_encrypt(
    const bytes32 &key,
    const bytes12 &nonce,
    std::span<const std::uint8_t> aad,
    std::span<const std::uint8_t> plaintext
  ) {
    openssl_ptr<EVP_CIPHER_CTX, EVP_CIPHER_CTX_free> context(
      EVP_CIPHER_CTX_new(), EVP_CIPHER_CTX_free);
    bytes result(plaintext.size() + 16);
    int length = 0;
    int written = 0;
    if (!context
        || EVP_EncryptInit_ex(context.get(), EVP_chacha20_poly1305(), nullptr, nullptr, nullptr) != 1
        || EVP_CIPHER_CTX_ctrl(context.get(), EVP_CTRL_AEAD_SET_IVLEN, nonce.size(), nullptr) != 1
        || EVP_EncryptInit_ex(context.get(), nullptr, nullptr, key.data(), nonce.data()) != 1
        || (!aad.empty()
            && EVP_EncryptUpdate(
                 context.get(), nullptr, &length, aad.data(), static_cast<int>(aad.size())) != 1)
        || (!plaintext.empty()
            && EVP_EncryptUpdate(
                 context.get(), result.data(), &length, plaintext.data(),
                 static_cast<int>(plaintext.size())) != 1)) {
      secure_clear(result);
      throw_protocol("cryptoFailure");
    }
    written = length;
    if (EVP_EncryptFinal_ex(context.get(), result.data() + written, &length) != 1) {
      secure_clear(result);
      throw_protocol("cryptoFailure");
    }
    written += length;
    if (EVP_CIPHER_CTX_ctrl(
          context.get(), EVP_CTRL_AEAD_GET_TAG, 16, result.data() + written) != 1) {
      secure_clear(result);
      throw_protocol("cryptoFailure");
    }
    result.resize(static_cast<std::size_t>(written) + 16);
    return result;
  }

  bytes chacha20_poly1305_decrypt(
    const bytes32 &key,
    const bytes12 &nonce,
    std::span<const std::uint8_t> aad,
    std::span<const std::uint8_t> ciphertext_and_tag
  ) {
    if (ciphertext_and_tag.size() < 16) throw_protocol("invalidCiphertext");
    const auto ciphertext_size = ciphertext_and_tag.size() - 16;
    openssl_ptr<EVP_CIPHER_CTX, EVP_CIPHER_CTX_free> context(
      EVP_CIPHER_CTX_new(), EVP_CIPHER_CTX_free);
    bytes result(ciphertext_size);
    int length = 0;
    int written = 0;
    if (!context
        || EVP_DecryptInit_ex(context.get(), EVP_chacha20_poly1305(), nullptr, nullptr, nullptr) != 1
        || EVP_CIPHER_CTX_ctrl(context.get(), EVP_CTRL_AEAD_SET_IVLEN, nonce.size(), nullptr) != 1
        || EVP_DecryptInit_ex(context.get(), nullptr, nullptr, key.data(), nonce.data()) != 1
        || (!aad.empty()
            && EVP_DecryptUpdate(
                 context.get(), nullptr, &length, aad.data(), static_cast<int>(aad.size())) != 1)
        || (ciphertext_size != 0
            && EVP_DecryptUpdate(
                 context.get(), result.data(), &length, ciphertext_and_tag.data(),
                 static_cast<int>(ciphertext_size)) != 1)) {
      secure_clear(result);
      throw_protocol("cryptoFailure");
    }
    written = length;
    if (EVP_CIPHER_CTX_ctrl(
          context.get(),
          EVP_CTRL_AEAD_SET_TAG,
          16,
          const_cast<std::uint8_t *>(ciphertext_and_tag.data() + ciphertext_size)) != 1
        || EVP_DecryptFinal_ex(context.get(), result.data() + written, &length) != 1) {
      secure_clear(result);
      throw_protocol("authenticationFailed");
    }
    result.resize(static_cast<std::size_t>(written + length));
    return result;
  }

  bytes32 derive_pairing_key(
    const bytes32 &shared_secret,
    const bytes32 &client_nonce,
    const bytes32 &host_nonce,
    const bytes32 &transcript_hash
  ) {
    bytes nonce_input;
    nonce_input.reserve(64);
    nonce_input.insert(nonce_input.end(), client_nonce.begin(), client_nonce.end());
    nonce_input.insert(nonce_input.end(), host_nonce.begin(), host_nonce.end());
    auto salt = sha256(nonce_input);

    constexpr std::string_view label = "Ligase attended pairing v1";
    bytes info(label.begin(), label.end());
    info.push_back(0);
    info.insert(info.end(), transcript_hash.begin(), transcript_hash.end());
    auto output = hkdf_sha256(shared_secret, salt, info);
    secure_clear(nonce_input);
    secure_clear(salt);
    secure_clear(info);
    return output;
  }

  std::string derive_safety_code(
    const bytes32 &pairing_key,
    const bytes32 &transcript_hash
  ) {
    constexpr std::string_view label = "Ligase attended pairing SAS v1";
    bytes input(label.begin(), label.end());
    input.push_back(0);
    input.insert(input.end(), transcript_hash.begin(), transcript_hash.end());
    auto digest = hmac_sha256(pairing_key, input);

    std::string raw;
    raw.reserve(8);
    std::uint64_t bits = 0;
    for (std::size_t index = 0; index < 5; ++index) {
      bits = (bits << 8) | digest[index];
    }
    for (int shift = 35; shift >= 0; shift -= 5) {
      raw.push_back(base32_alphabet[(bits >> shift) & 0x1f]);
    }
    secure_clear(input);
    secure_clear(digest);
    return raw.substr(0, 4) + "-" + raw.substr(4);
  }

  std::array<char, 4> validate_legacy_pin_plaintext(
    std::span<const std::uint8_t> plaintext
  ) {
    constexpr std::string_view prefix = R"({"legacyPin":")";
    constexpr std::string_view suffix = R"("})";
    if (plaintext.size() != 20
        || !std::equal(prefix.begin(), prefix.end(), plaintext.begin())
        || !std::equal(suffix.begin(), suffix.end(), plaintext.end() - suffix.size())) {
      throw_protocol("invalidPlaintext");
    }
    std::array<char, 4> pin {};
    const auto offset = prefix.size();
    for (std::size_t index = 0; index < pin.size(); ++index) {
      const auto value = plaintext[offset + index];
      if (value < '0' || value > '9') throw_protocol("invalidPlaintext");
      pin[index] = static_cast<char>(value);
    }
    return pin;
  }

  void secure_clear(std::span<std::uint8_t> value) noexcept {
    if (!value.empty()) OPENSSL_cleanse(value.data(), value.size());
  }
}  // namespace attended_pairing
