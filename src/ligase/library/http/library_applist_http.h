/**
 * @file src/ligase/library/http/library_applist_http.h
 * @brief HTTP-independent adapter contract for the Moonlight application list.
 */
#pragma once

#include <cstddef>
#include <exception>
#include <functional>
#include <string>
#include <string_view>
#include <vector>

namespace ligase::library::http {
  struct applist_app {
    std::string name;
    std::string uuid;
    std::string idx;
    std::string id;
  };

  struct applist_request {
    bool authorized;
    bool hdr_supported;
    bool input_only_mode;
    int current_app_id;
    int input_only_app_id;
    int terminate_app_id;
    bool legacy_ordering;
  };

  struct applist_dependencies {
    std::function<std::vector<applist_app>()> load_apps;
    std::function<int(std::string_view)> parse_id;
    std::function<std::size_t(std::size_t)> ordering_width;
    std::function<std::string(std::string_view, std::size_t, std::size_t)> order_title;
  };

  struct applist_response {
    std::string body;
    std::exception_ptr failure;
  };

  [[nodiscard]] applist_response handle_applist(
    const applist_request &request,
    const applist_dependencies &dependencies
  );
}
