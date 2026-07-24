/**
 * @file src/ligase/library/http/library_sort_http.h
 * @brief HTTP-independent adapter contract for the Ligase manual-order route.
 */
#pragma once

#include <cstdint>
#include <functional>
#include <mutex>
#include <string>
#include <string_view>

#include <nlohmann/json.hpp>

namespace ligase::library::http {
  struct sort_request {
    bool authorized;
    std::string body;
  };

  struct sort_response {
    int status;
    nlohmann::json body;
  };

  struct sort_dependencies {
    std::mutex &mutation_mutex;
    std::function<nlohmann::json()> load_library;
    std::function<nlohmann::json()> load_sync;
    std::function<void(const nlohmann::json &)> save_library;
    std::function<void(const nlohmann::json &)> save_sync;
    std::function<std::string()> timestamp;
  };

  [[nodiscard]] bool manual_sort_revision_valid(std::int64_t revision);
  [[nodiscard]] bool manual_sort_uuid_valid(std::string_view value);

  [[nodiscard]] sort_response handle_sort(
    const sort_request &request,
    const sort_dependencies &dependencies
  );
}
