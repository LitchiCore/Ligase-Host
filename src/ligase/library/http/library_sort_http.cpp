/**
 * @file src/ligase/library/http/library_sort_http.cpp
 * @brief Strict parsing and use-case orchestration for manual library sorting.
 */

#include "src/ligase/library/http/library_sort_http.h"

#include <regex>
#include <stdexcept>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace ligase::library::http {
  namespace {
    constexpr std::int64_t max_safe_integer = 9007199254740991LL;

    sort_response invalid_manual_order() {
      return {400, {{"error", "invalidManualOrder"}}};
    }
  }

  bool manual_sort_revision_valid(std::int64_t revision) {
    return revision >= 1 && revision <= max_safe_integer;
  }

  bool manual_sort_uuid_valid(std::string_view value) {
    static const std::regex canonical(
      "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
    return std::regex_match(value.begin(), value.end(), canonical);
  }

  sort_response handle_sort(
    const sort_request &request,
    const sort_dependencies &dependencies
  ) {
    if (!request.authorized) {
      return {403, {{"error", "permissionDenied"}}};
    }

    nlohmann::json request_json;
    try {
      request_json = nlohmann::json::parse(request.body);
    } catch (const std::exception &error) {
      return {400, {{"error", "invalidJson"}, {"message", error.what()}}};
    }

    if (!request_json.is_object() ||
        request_json.size() != 2 ||
        !request_json.contains("baseRevision") ||
        !request_json.at("baseRevision").is_number_integer() ||
        !request_json.contains("orderedAppUuids") ||
        !request_json.at("orderedAppUuids").is_array()) {
      return invalid_manual_order();
    }

    const auto &base_revision_json = request_json.at("baseRevision");
    if (base_revision_json.is_number_unsigned() &&
        base_revision_json.get<std::uint64_t>() >
          static_cast<std::uint64_t>(max_safe_integer)) {
      return invalid_manual_order();
    }
    const auto base_revision = base_revision_json.get<std::int64_t>();
    if (!manual_sort_revision_valid(base_revision)) {
      return invalid_manual_order();
    }

    std::vector<std::string> requested_order;
    std::unordered_set<std::string> requested_ids;
    for (const auto &value : request_json.at("orderedAppUuids")) {
      if (!value.is_string()) {
        requested_order.clear();
        break;
      }
      auto uuid = value.get<std::string>();
      if (!manual_sort_uuid_valid(uuid) || !requested_ids.emplace(uuid).second) {
        requested_order.clear();
        break;
      }
      requested_order.emplace_back(std::move(uuid));
    }
    if (requested_order.size() != request_json.at("orderedAppUuids").size()) {
      return invalid_manual_order();
    }

    try {
      std::scoped_lock lock(dependencies.mutation_mutex);
      auto library = dependencies.load_library();
      auto sync = dependencies.load_sync();
      const auto current_revision = library.value("revision", std::int64_t {0});
      if (base_revision != current_revision) {
        return {
          409,
          {
            {"error", "revisionConflict"},
            {"currentRevision", current_revision}
          }
        };
      }

      std::unordered_set<std::string> expected_ids;
      std::vector<std::string> hidden_ids;
      for (const auto &item : sync.at("library").at("items")) {
        const auto id = item.at("id").get<std::string>();
        if (item.value("publishedToClients", true)) expected_ids.emplace(id);
        else hidden_ids.emplace_back(id);
      }
      if (requested_ids != expected_ids) {
        return invalid_manual_order();
      }

      auto full_order = requested_order;
      full_order.insert(full_order.end(), hidden_ids.begin(), hidden_ids.end());
      const auto reorder = [&full_order](nlohmann::json &items) {
        std::unordered_map<std::string, nlohmann::json> by_id;
        for (auto &item : items) {
          by_id.emplace(item.at("id").get<std::string>(), std::move(item));
        }
        nlohmann::json ordered = nlohmann::json::array();
        for (const auto &id : full_order) {
          auto iterator = by_id.find(id);
          if (iterator != by_id.end()) {
            ordered.push_back(std::move(iterator->second));
            by_id.erase(iterator);
          }
        }
        if (!by_id.empty()) throw std::runtime_error("library order projection mismatch");
        items = std::move(ordered);
      };

      auto updated_library = library;
      auto updated_sync = sync;
      reorder(updated_library["items"]);
      reorder(updated_sync["library"]["items"]);
      const auto updated_at = dependencies.timestamp();
      updated_library["sortMode"] = 5;
      updated_library["revision"] = current_revision + 1;
      updated_library["updatedAt"] = updated_at;
      updated_sync["library"]["sortMode"] = "manual";
      updated_sync["library"]["revision"] = current_revision + 1;
      updated_sync["library"]["updatedAt"] = updated_at;

      try {
        dependencies.save_library(updated_library);
        dependencies.save_sync(updated_sync);
      } catch (...) {
        dependencies.save_library(library);
        dependencies.save_sync(sync);
        throw;
      }

      return {
        200,
        {
          {"revision", current_revision + 1},
          {"sortMode", "manual"},
          {"orderedAppUuids", requested_order}
        }
      };
    } catch (const std::exception &) {
      return {500, {{"error", "libraryUpdateFailed"}}};
    }
  }
}
