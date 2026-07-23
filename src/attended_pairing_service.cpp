#include "attended_pairing_service.h"

#include <algorithm>
#include <format>
#include <iomanip>
#include <sstream>

#include <boost/asio/ip/address.hpp>
#include <openssl/crypto.h>
#include <openssl/rand.h>

namespace attended_pairing {
  namespace {
    bytes default_random(std::size_t size) {
      bytes result(size);
      if (RAND_bytes(result.data(), static_cast<int>(result.size())) != 1) {
        throw service_exception(service_error::pairing_unavailable);
      }
      return result;
    }

    bytes32 as32(bytes value) {
      if (value.size() != 32) {
        secure_clear(value);
        throw service_exception(service_error::pairing_unavailable);
      }
      bytes32 result {};
      std::ranges::copy(value, result.begin());
      secure_clear(value);
      return result;
    }

    std::string utc_seconds(std::chrono::system_clock::time_point value) {
      const auto seconds =
        std::chrono::floor<std::chrono::seconds>(value);
      const auto day = std::chrono::floor<std::chrono::days>(seconds);
      const std::chrono::year_month_day date {day};
      const std::chrono::hh_mm_ss time {seconds - day};
      return std::format(
        "{:%Y-%m-%d}T{:02}:{:02}:{:02}Z",
        date,
        time.hours().count(),
        time.minutes().count(),
        time.seconds().count()
      );
    }

    bool constant_equal(
      std::span<const std::uint8_t> left,
      std::span<const std::uint8_t> right
    ) {
      return left.size() == right.size()
        && CRYPTO_memcmp(left.data(), right.data(), left.size()) == 0;
    }

    std::string rate_key(const source_identity &source) {
      boost::system::error_code error;
      const auto address = boost::asio::ip::make_address(source.address, error);
      if (error) return "invalid";
      if (address.is_v4()) {
        const auto bytes = address.to_v4().to_bytes();
        return std::format(
          "4:{:02x}{:02x}{:02x}{:02x}",
          bytes[0], bytes[1], bytes[2], bytes[3]);
      }
      const auto ipv6 = address.to_v6();
      if (ipv6.is_v4_mapped()) {
        const auto bytes = ipv6.to_bytes();
        return std::format(
          "4:{:02x}{:02x}{:02x}{:02x}",
          bytes[12], bytes[13], bytes[14], bytes[15]);
      }
      const auto bytes = ipv6.to_bytes();
      auto result = std::format(
        "6:{:02x}{:02x}{:02x}{:02x}{:02x}{:02x}{:02x}{:02x}",
        bytes[0], bytes[1], bytes[2], bytes[3],
        bytes[4], bytes[5], bytes[6], bytes[7]);
      if (ipv6.is_link_local()) {
        result += ":" + std::to_string(source.scope_id);
      }
      return result;
    }
  }

  struct pairing_service::record {
    create_input input;
    create_output output;
    request_lifetime lifetime;
    steady_clock::time_point created_monotonic;
    std::string created_at;
    steady_clock::time_point terminal_at {};
    bool terminal_timestamped = false;
    bytes token;
    bytes32 token_hash {};
    bytes32 host_private_key {};
    bytes32 host_nonce {};
    bytes32 transcript_hash {};
    bytes32 pairing_key {};
    std::string safety_code;
    envelope_replay_guard replay;
    std::optional<bytes> encrypted_envelope;
    std::optional<bytes12> envelope_nonce;
    secret_owner pin;
    legacy_pairing_adapter *adapter = nullptr;
    std::optional<std::string> failure;

    record(
      create_input source,
      create_output response,
      steady_clock::time_point created,
      std::string created_wall,
      std::chrono::seconds duration
    ):
        input(std::move(source)),
        output(std::move(response)),
        lifetime(created, duration),
        created_monotonic(created),
        created_at(std::move(created_wall)) {
    }

    ~record() {
      secure_clear(token);
      secure_clear(token_hash);
      secure_clear(host_private_key);
      secure_clear(host_nonce);
      secure_clear(transcript_hash);
      secure_clear(pairing_key);
      replay.clear();
      if (encrypted_envelope) secure_clear(*encrypted_envelope);
      if (envelope_nonce) secure_clear(*envelope_nonce);
    }
  };

  service_exception::service_exception(
    service_error code,
    std::string detail,
    std::optional<request_state> current_state
  ):
      code_(code),
      detail_(std::move(detail)),
      current_state_(current_state) {
  }

  service_error service_exception::code() const noexcept {
    return code_;
  }

  const std::string &service_exception::detail() const noexcept {
    return detail_;
  }

  const std::optional<request_state> &
  service_exception::current_state() const noexcept {
    return current_state_;
  }

  const char *service_exception::what() const noexcept {
    return "attended pairing service error";
  }

  pairing_service::pairing_service(service_config config, random_bytes random):
      config_(std::move(config)),
      random_(random ? std::move(random) : default_random),
      host_certificate_sha256_(sha256(config_.host_certificate_der)) {
  }

  pairing_service::~pairing_service() {
    clear_for_restart();
  }

  pairing_service::record &pairing_service::lookup(std::string_view request_id) {
    const auto found = records_.find(std::string(request_id));
    if (found == records_.end()) {
      throw service_exception(service_error::request_not_found);
    }
    return *found->second;
  }

  pairing_service::record &pairing_service::authenticate(
    std::string_view request_id,
    std::span<const std::uint8_t> bearer,
    const source_identity &source
  ) {
    auto &value = lookup(request_id);
    const auto digest = sha256(bearer);
    if (!(value.input.source == source)
        || !constant_equal(digest, value.token_hash)) {
      throw service_exception(service_error::invalid_request_token);
    }
    return value;
  }

  void pairing_service::materialize_expiry(
    record &value,
    steady_clock::time_point now
  ) {
    const auto before = value.lifetime.state();
    if (value.lifetime.expire(now, value.adapter)
        == internal_action_result::changed) {
      cleanup_terminal(value);
    }
    const auto after = value.lifetime.state();
    if (before != after && after == request_state::expired) {
      value.terminal_at = now;
      value.terminal_timestamped = true;
    }
  }

  void pairing_service::evict(steady_clock::time_point now) {
    std::erase_if(records_, [&](const auto &entry) {
      const auto &value = *entry.second;
      return value.terminal_timestamped
        && now - value.terminal_at >= config_.terminal_cache_lifetime;
    });
  }

  create_output pairing_service::create(
    const create_input &input,
    steady_clock::time_point now,
    wall_clock::time_point wall_now
  ) {
    std::scoped_lock lock(mutex_);
    evict(now);
    if (const auto existing = records_.find(input.request_id);
        existing != records_.end()) {
      auto &value = *existing->second;
      materialize_expiry(value, now);
      if (!(value.input.source == input.source)
          || value.input.canonical_jcs != input.canonical_jcs) {
        throw service_exception(service_error::request_id_conflict);
      }
      const auto state = value.lifetime.state();
      if (state == request_state::pending || state == request_state::approved) {
        if (value.token.empty()) {
          throw service_exception(service_error::request_id_conflict);
        }
        auto response = value.output;
        response.status = 200;
        response.request_token = base64url_encode(value.token);
        return response;
      }
      if (state == request_state::expired) {
        throw service_exception(service_error::request_expired);
      }
      throw service_exception(service_error::request_id_conflict);
    }

    for (const auto &[id, candidate] : records_) {
      const auto state = candidate->lifetime.state();
      if ((state == request_state::pending || state == request_state::approved)
          && constant_equal(
            candidate->input.client_certificate_sha256,
            input.client_certificate_sha256)) {
        throw service_exception(service_error::client_certificate_busy);
      }
    }
    auto &bucket = rate_buckets_[rate_key(input.source)];
    while (!bucket.empty() && now - bucket.front() >= config_.rate_window) {
      bucket.pop_front();
    }
    if (bucket.size() >= config_.rate_limit) {
      throw service_exception(service_error::rate_limited);
    }
    bucket.push_back(now);

    auto host_private = as32(random_(32));
    auto host_nonce = as32(random_(32));
    auto token = random_(32);
    const auto host_public = x25519_public_key(host_private);
    const auto expires = wall_now + config_.request_lifetime;

    create_output output {
      .status = 201,
      .request_id = input.request_id,
      .request_token = base64url_encode(token),
      .host_unique_id = config_.host_unique_id,
      .host_certificate_sha256 = host_certificate_sha256_,
      .host_ephemeral_key = host_public,
      .host_nonce = host_nonce,
      .expires_at = utc_seconds(expires)
    };

    nlohmann::json response_without_token {
      {"expiresAt", output.expires_at},
      {"hostCertificateSha256", base64url_encode(output.host_certificate_sha256)},
      {"hostEphemeralKey", base64url_encode(output.host_ephemeral_key)},
      {"hostNonce", base64url_encode(output.host_nonce)},
      {"hostUniqueId", output.host_unique_id},
      {"requestId", output.request_id},
      {"version", 1}
    };
    const auto request_json = nlohmann::json::parse(input.canonical_jcs);
    const auto transcript = jcs({
      {"request", request_json},
      {"response", response_without_token}
    });
    const bytes transcript_bytes(transcript.begin(), transcript.end());
    const auto transcript_hash = sha256(transcript_bytes);
    const auto shared = x25519_shared_secret(
      host_private,
      input.client_ephemeral_key
    );
    const auto pairing_key = derive_pairing_key(
      shared,
      input.client_nonce,
      host_nonce,
      transcript_hash
    );

    auto value = std::make_unique<record>(
      input, output, now, utc_seconds(wall_now), config_.request_lifetime);
    value->token = std::move(token);
    value->token_hash = sha256(value->token);
    value->host_private_key = host_private;
    value->host_nonce = host_nonce;
    value->transcript_hash = transcript_hash;
    value->pairing_key = pairing_key;
    value->safety_code = derive_safety_code(pairing_key, transcript_hash);
    records_.emplace(input.request_id, std::move(value));
    return output;
  }

  void pairing_service::upload_envelope(
    std::string_view request_id,
    std::span<const std::uint8_t> bearer,
    const source_identity &source,
    const bytes12 &nonce,
    std::span<const std::uint8_t> ciphertext_and_tag,
    std::string_view canonical_envelope_jcs,
    steady_clock::time_point now
  ) {
    std::scoped_lock lock(mutex_);
    auto &value = authenticate(request_id, bearer, source);
    materialize_expiry(value, now);
    if (value.lifetime.state() == request_state::expired) {
      throw service_exception(service_error::request_expired);
    }
    const bytes canonical(
      canonical_envelope_jcs.begin(), canonical_envelope_jcs.end());
    const auto canonical_hash = sha256(canonical);
    if (value.replay.has_metadata()) {
      const auto replay = value.replay.observe(nonce, canonical_hash);
      if (replay == envelope_replay_result::exact_replay) return;
      if (replay == envelope_replay_result::nonce_reuse) {
        throw service_exception(service_error::nonce_reuse);
      }
      throw service_exception(service_error::envelope_conflict);
    }
    if (value.lifetime.state() != request_state::pending) {
      throw service_exception(
        service_error::invalid_state, {}, value.lifetime.state());
    }
    bytes plaintext;
    try {
      plaintext = chacha20_poly1305_decrypt(
        value.pairing_key,
        nonce,
        value.transcript_hash,
        ciphertext_and_tag
      );
    } catch (...) {
      secure_clear(plaintext);
      throw service_exception(service_error::invalid_envelope, "authenticationFailed");
    }
    try {
      const auto digits = validate_legacy_pin_plaintext(plaintext);
      value.pin = secret_owner(bytes(digits.begin(), digits.end()));
    } catch (...) {
      secure_clear(plaintext);
      throw service_exception(service_error::invalid_envelope, "invalidPlaintext");
    }
    secure_clear(plaintext);
    value.encrypted_envelope = bytes(
      ciphertext_and_tag.begin(), ciphertext_and_tag.end());
    value.envelope_nonce = nonce;
    const auto replay = value.replay.observe(nonce, canonical_hash);
    if (replay != envelope_replay_result::first_upload) {
      throw service_exception(service_error::pairing_unavailable);
    }
    value.lifetime.set_envelope_verified();
  }

  status_output pairing_service::project_status(const record &value) const {
    return {
      .request_id = value.input.request_id,
      .state = value.lifetime.state(),
      .expires_at = value.output.expires_at,
      .failure = value.failure
    };
  }

  status_output pairing_service::status(
    std::string_view request_id,
    std::span<const std::uint8_t> bearer,
    const source_identity &source,
    steady_clock::time_point now
  ) {
    std::scoped_lock lock(mutex_);
    evict(now);
    auto &value = authenticate(request_id, bearer, source);
    materialize_expiry(value, now);
    return project_status(value);
  }

  bool pairing_service::exists(
    std::string_view request_id,
    steady_clock::time_point now
  ) {
    std::scoped_lock lock(mutex_);
    evict(now);
    const auto found = records_.find(std::string(request_id));
    if (found == records_.end()) return false;
    materialize_expiry(*found->second, now);
    return true;
  }

  void pairing_service::cleanup_terminal(record &value) {
    secure_clear(value.token);
    value.token.clear();
    value.token.shrink_to_fit();
    secure_clear(value.host_private_key);
    secure_clear(value.host_nonce);
    secure_clear(value.pairing_key);
    value.pin.clear();
    if (value.encrypted_envelope) secure_clear(*value.encrypted_envelope);
    value.encrypted_envelope.reset();
    value.envelope_nonce.reset();
  }

  void pairing_service::cancel(
    std::string_view request_id,
    std::span<const std::uint8_t> bearer,
    const source_identity &source,
    steady_clock::time_point now
  ) {
    std::scoped_lock lock(mutex_);
    evict(now);
    auto &value = authenticate(request_id, bearer, source);
    materialize_expiry(value, now);
    if (value.lifetime.cancel(value.adapter) == internal_action_result::changed) {
      value.terminal_at = now;
      value.terminal_timestamped = true;
      cleanup_terminal(value);
    }
  }

  std::vector<pending_output> pairing_service::pending(steady_clock::time_point now) {
    std::scoped_lock lock(mutex_);
    evict(now);
    std::vector<pending_output> result;
    for (auto &[id, pointer] : records_) {
      auto &value = *pointer;
      materialize_expiry(value, now);
      const auto state = value.lifetime.state();
      if (state != request_state::pending && state != request_state::approved) continue;
      result.push_back({
        .request_id = id,
        .device = value.input.device,
        .state = state,
        .ready_for_approval = value.lifetime.ready_for_approval(),
        .safety_code = value.safety_code,
        .created_at = value.created_at,
        .expires_at = value.output.expires_at,
        .source = value.input.source
      });
    }
    std::ranges::sort(result, {}, [](const pending_output &item) {
      return std::pair {item.created_at, item.request_id};
    });
    return result;
  }

  std::pair<status_output, bool> pairing_service::allow(
    std::string_view request_id,
    steady_clock::time_point now
  ) {
    std::scoped_lock lock(mutex_);
    auto &value = lookup(request_id);
    materialize_expiry(value, now);
    const auto state = value.lifetime.state();
    if (state == request_state::approved || state == request_state::paired) {
      return {project_status(value), false};
    }
    if (value.adapter == nullptr) {
      throw service_exception(
        service_error::invalid_state,
        "notReadyForApproval",
        state);
    }
    internal_action_result action;
    try {
      action = value.lifetime.allow(*value.adapter, value.pin);
    } catch (...) {
      value.failure = "legacyPairingFailed";
      value.terminal_at = now;
      value.terminal_timestamped = true;
      cleanup_terminal(value);
      throw service_exception(service_error::pairing_unavailable);
    }
    if (action == internal_action_result::not_ready) {
      throw service_exception(
        service_error::invalid_state,
        "notReadyForApproval",
        value.lifetime.state());
    }
    if (action == internal_action_result::invalid_state) {
      throw service_exception(
        service_error::invalid_state, {}, value.lifetime.state());
    }
    if (value.encrypted_envelope) secure_clear(*value.encrypted_envelope);
    value.encrypted_envelope.reset();
    secure_clear(value.host_private_key);
    secure_clear(value.pairing_key);
    return {project_status(value), action == internal_action_result::changed};
  }

  status_output pairing_service::reject(
    std::string_view request_id,
    steady_clock::time_point now
  ) {
    std::scoped_lock lock(mutex_);
    auto &value = lookup(request_id);
    materialize_expiry(value, now);
    const auto action = value.lifetime.reject(value.adapter);
    if (action == internal_action_result::invalid_state) {
      throw service_exception(
        service_error::invalid_state, {}, value.lifetime.state());
    }
    if (action == internal_action_result::changed) {
      value.terminal_at = now;
      value.terminal_timestamped = true;
      cleanup_terminal(value);
    }
    return project_status(value);
  }

  void pairing_service::bind_held_getservercert(
    std::string_view request_id,
    const bytes32 &client_certificate_sha256,
    const source_identity &source,
    legacy_pairing_adapter &adapter,
    steady_clock::time_point now
  ) {
    std::scoped_lock lock(mutex_);
    auto &value = lookup(request_id);
    materialize_expiry(value, now);
    const bool source_matches = value.input.source == source;
    const bool certificate_matches = constant_equal(
      client_certificate_sha256,
      value.input.client_certificate_sha256);
    if (!source_matches || !certificate_matches) {
      const auto generation = value.lifetime.generation();
      if (value.lifetime.fail(generation) == internal_action_result::changed) {
        value.failure =
          certificate_matches ? "pairSessionMissing" : "certificateMismatch";
        value.terminal_at = now;
        value.terminal_timestamped = true;
        cleanup_terminal(value);
      }
      throw service_exception(service_error::invalid_request_token);
    }
    if (value.adapter != nullptr && value.adapter != &adapter) {
      throw service_exception(service_error::client_certificate_busy);
    }
    value.adapter = &adapter;
    if (value.lifetime.bind_held_session()
        == internal_action_result::invalid_state) {
      throw service_exception(
        service_error::invalid_state, {}, value.lifetime.state());
    }
  }

  void pairing_service::commit_paired(
    std::string_view request_id,
    std::uint64_t generation
  ) {
    std::scoped_lock lock(mutex_);
    auto &value = lookup(request_id);
    if (value.lifetime.commit_paired(generation)
        != internal_action_result::changed) {
      throw service_exception(
        service_error::invalid_state, {}, value.lifetime.state());
    }
    value.terminal_at = steady_clock::now();
    value.terminal_timestamped = true;
    cleanup_terminal(value);
  }

  void pairing_service::fail_pairing(
    std::string_view request_id,
    std::uint64_t generation,
    std::string failure,
    steady_clock::time_point now
  ) {
    std::scoped_lock lock(mutex_);
    auto &value = lookup(request_id);
    if (value.lifetime.fail(generation) != internal_action_result::changed) {
      return;
    }
    value.failure = std::move(failure);
    value.terminal_at = now;
    value.terminal_timestamped = true;
    cleanup_terminal(value);
  }

  void pairing_service::clear_for_restart() noexcept {
    std::scoped_lock lock(mutex_);
    for (auto &[id, value] : records_) {
      value->lifetime.cancel(value->adapter);
      cleanup_terminal(*value);
    }
    records_.clear();
  }

  std::string request_state_name(request_state state) {
    switch (state) {
      case request_state::pending: return "pending";
      case request_state::approved: return "approved";
      case request_state::paired: return "paired";
      case request_state::rejected: return "rejected";
      case request_state::cancelled: return "cancelled";
      case request_state::expired: return "expired";
      case request_state::failed: return "failed";
    }
    return "failed";
  }
}  // namespace attended_pairing
