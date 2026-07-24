/**
 * @file src/ligase/library/http/library_sync_http.cpp
 * @brief Read-only use-case orchestration for the Ligase Sync route.
 */

#include "src/ligase/library/http/library_sync_http.h"

#include <exception>
#include <utility>

namespace ligase::library::http {
  sync_response handle_sync(
    const sync_request &request,
    const sync_dependencies &dependencies
  ) {
    if (!request.authorized) {
      return {403, {{"error", "permissionDenied"}}};
    }

    try {
      std::scoped_lock lock(dependencies.sync_mutex);
      auto sync = dependencies.load_sync();
      sync["capabilities"] = {
        {"hdrEncodingSupported", dependencies.hdr_encoding_supported()}
      };
      return {200, std::move(sync)};
    } catch (const std::exception &error) {
      return {
        404,
        {{"error", "syncUnavailable"}, {"message", error.what()}}
      };
    }
  }
}
