/**
 * @file src/ligase/pairing/service/attended_pairing_service.h
 * @brief HTTP-independent attended-pairing v1 coordinator repository.
 */
#pragma once

#include "src/ligase/pairing/domain/attended_pairing.h"

#include <chrono>
#include <functional>
#include <deque>
#include <memory>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

namespace attended_pairing {
  enum class access_mode {
    operate,
    observe
  };

  struct source_identity {
    std::string address;
    std::uint32_t scope_id = 0;

    bool operator==(const source_identity &) const = default;
  };

  struct device_identity {
    std::string name;
    std::string platform;
  };

  struct create_input {
    std::string request_id;
    device_identity device;
    bytes32 client_ephemeral_key {};
    bytes32 client_nonce {};
    bytes32 client_certificate_sha256 {};
    std::string canonical_jcs;
    source_identity source;
  };

  struct create_output {
    int status = 201;
    std::string request_id;
    std::string request_token;
    std::string host_unique_id;
    bytes32 host_certificate_sha256 {};
    bytes32 host_ephemeral_key {};
    bytes32 host_nonce {};
    std::string expires_at;
  };

  struct status_output {
    std::string request_id;
    request_state state = request_state::pending;
    std::string expires_at;
    std::optional<std::string> failure;
  };

  struct pending_output {
    std::string request_id;
    device_identity device;
    request_state state = request_state::pending;
    bool ready_for_approval = false;
    std::string safety_code;
    std::string created_at;
    std::string expires_at;
    source_identity source;
  };

  enum class service_error {
    invalid_request,
    invalid_request_token,
    request_not_found,
    request_id_conflict,
    client_certificate_busy,
    invalid_state,
    invalid_envelope,
    nonce_reuse,
    envelope_conflict,
    request_expired,
    rate_limited,
    pairing_unavailable
  };

  class service_exception: public std::exception {
  public:
    service_exception(
      service_error code,
      std::string detail = {},
      std::optional<request_state> current_state = {}
    );
    [[nodiscard]] service_error code() const noexcept;
    [[nodiscard]] const std::string &detail() const noexcept;
    [[nodiscard]] const std::optional<request_state> &current_state() const noexcept;
    [[nodiscard]] const char *what() const noexcept override;

  private:
    service_error code_;
    std::string detail_;
    std::optional<request_state> current_state_;
  };

  struct service_config {
    std::string host_unique_id;
    bytes host_certificate_der;
    std::chrono::seconds request_lifetime {120};
    std::chrono::minutes terminal_cache_lifetime {10};
    std::size_t rate_limit = 8;
    std::chrono::minutes rate_window {1};
  };

  class pairing_service {
  public:
    using steady_clock = request_lifetime::clock;
    using wall_clock = std::chrono::system_clock;
    using random_bytes = std::function<bytes(std::size_t)>;

    explicit pairing_service(
      service_config config,
      random_bytes random = {}
    );
    ~pairing_service();

    create_output create(
      const create_input &input,
      steady_clock::time_point now,
      wall_clock::time_point wall_now
    );
    void upload_envelope(
      std::string_view request_id,
      std::span<const std::uint8_t> bearer,
      const source_identity &source,
      const bytes12 &nonce,
      std::span<const std::uint8_t> ciphertext_and_tag,
      std::string_view canonical_envelope_jcs,
      steady_clock::time_point now
    );
    status_output status(
      std::string_view request_id,
      std::span<const std::uint8_t> bearer,
      const source_identity &source,
      steady_clock::time_point now
    );
    [[nodiscard]] bool exists(
      std::string_view request_id,
      steady_clock::time_point now
    );
    void cancel(
      std::string_view request_id,
      std::span<const std::uint8_t> bearer,
      const source_identity &source,
      steady_clock::time_point now
    );

    std::vector<pending_output> pending(steady_clock::time_point now);
    std::pair<status_output, bool> allow(
      std::string_view request_id,
      steady_clock::time_point now
    );
    status_output reject(
      std::string_view request_id,
      steady_clock::time_point now
    );
    void set_access_mode(
      std::string_view request_id,
      access_mode mode,
      steady_clock::time_point now
    );
    [[nodiscard]] access_mode access_mode_for_pairing(
      std::string_view request_id
    );

    void bind_held_getservercert(
      std::string_view request_id,
      const bytes32 &client_certificate_sha256,
      const source_identity &source,
      legacy_pairing_adapter &adapter,
      steady_clock::time_point now
    );
    void commit_paired(
      std::string_view request_id,
      std::uint64_t generation
    );
    void fail_pairing(
      std::string_view request_id,
      std::uint64_t generation,
      std::string failure,
      steady_clock::time_point now
    );
    void clear_for_restart() noexcept;

  private:
    struct record;

    record &lookup(std::string_view request_id);
    record &authenticate(
      std::string_view request_id,
      std::span<const std::uint8_t> bearer,
      const source_identity &source
    );
    void materialize_expiry(record &value, steady_clock::time_point now);
    void evict(steady_clock::time_point now);
    status_output project_status(const record &value) const;
    void cleanup_terminal(record &value);

    service_config config_;
    random_bytes random_;
    bytes32 host_certificate_sha256_ {};
    std::unordered_map<std::string, std::unique_ptr<record>> records_;
    std::unordered_map<std::string, std::deque<steady_clock::time_point>>
      rate_buckets_;
    std::mutex mutex_;
  };

  [[nodiscard]] std::string request_state_name(request_state state);
}  // namespace attended_pairing
