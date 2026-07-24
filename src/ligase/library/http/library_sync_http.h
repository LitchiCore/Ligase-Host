/**
 * @file src/ligase/library/http/library_sync_http.h
 * @brief HTTP-independent adapter contract for the Ligase Sync read route.
 */
#pragma once

#include <functional>
#include <mutex>

#include <nlohmann/json.hpp>

namespace ligase::library::http {
  struct sync_request {
    bool authorized;
  };

  struct sync_response {
    int status;
    nlohmann::json body;
  };

  struct sync_dependencies {
    std::mutex &sync_mutex;
    std::function<nlohmann::json()> load_sync;
    std::function<bool()> hdr_encoding_supported;
  };

  [[nodiscard]] sync_response handle_sync(
    const sync_request &request,
    const sync_dependencies &dependencies
  );
}
