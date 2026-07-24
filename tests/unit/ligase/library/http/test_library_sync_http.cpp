#include <gtest/gtest.h>

#include <stdexcept>

#include "src/ligase/library/http/library_sync_http.h"

namespace {
  using ligase::library::http::handle_sync;
  using ligase::library::http::sync_dependencies;
  using ligase::library::http::sync_request;

  struct harness {
    std::mutex mutex;
    nlohmann::json sync {
      {"schemaVersion", 1},
      {"library", {
        {"revision", 18},
        {"sortMode", "manual"},
        {"items", nlohmann::json::array({
          {
            {"id", "11111111-1111-4111-8111-111111111111"},
            {"publishedToClients", true}
          }
        })}
      }},
      {"streaming", {
        {"revision", 6},
        {"globalResolution", {{"width", 1920}, {"height", 1080}}}
      }}
    };
    bool hdr_supported = true;
    bool missing = false;
    bool malformed = false;
    int loads = 0;

    sync_dependencies dependencies() {
      return {
        .sync_mutex = mutex,
        .load_sync = [this]() {
          ++loads;
          if (missing) {
            throw std::runtime_error("Ligase sync file is not available");
          }
          if (malformed) {
            throw nlohmann::json::parse_error::create(
              101,
              1,
              "parse error",
              nullptr);
          }
          return sync;
        },
        .hdr_encoding_supported = [this]() { return hdr_supported; }
      };
    }
  };
}

TEST(LibrarySyncHttp, SuccessPreservesLibraryAndStreamingProjection) {
  harness state;
  const auto original = state.sync;

  const auto response = handle_sync({.authorized = true}, state.dependencies());

  EXPECT_EQ(response.status, 200);
  EXPECT_EQ(response.body.at("library"), original.at("library"));
  EXPECT_EQ(response.body.at("streaming"), original.at("streaming"));
  EXPECT_EQ(response.body.at("library").at("revision"), 18);
  EXPECT_EQ(response.body.at("streaming").at("revision"), 6);
  EXPECT_TRUE(
    response.body.at("capabilities").at("hdrEncodingSupported").get<bool>());
  EXPECT_EQ(state.loads, 1);
  EXPECT_EQ(state.sync, original);
}

TEST(LibrarySyncHttp, ObserveReadPermissionUsesSameAuthorizedPath) {
  harness state;
  state.hdr_supported = false;

  const auto response = handle_sync({.authorized = true}, state.dependencies());

  EXPECT_EQ(response.status, 200);
  EXPECT_FALSE(
    response.body.at("capabilities").at("hdrEncodingSupported").get<bool>());
}

TEST(LibrarySyncHttp, UnauthorizedNeverReadsProjection) {
  harness state;

  const auto response = handle_sync({.authorized = false}, state.dependencies());

  EXPECT_EQ(response.status, 403);
  EXPECT_EQ(response.body, nlohmann::json({{"error", "permissionDenied"}}));
  EXPECT_EQ(state.loads, 0);
}

TEST(LibrarySyncHttp, MissingProjectionRetainsExistingNotFoundWire) {
  harness state;
  state.missing = true;

  const auto response = handle_sync({.authorized = true}, state.dependencies());

  EXPECT_EQ(response.status, 404);
  EXPECT_EQ(
    response.body,
    nlohmann::json({
      {"error", "syncUnavailable"},
      {"message", "Ligase sync file is not available"}
    }));
}

TEST(LibrarySyncHttp, MalformedProjectionRetainsExistingNotFoundWire) {
  harness state;
  state.malformed = true;

  const auto response = handle_sync({.authorized = true}, state.dependencies());

  EXPECT_EQ(response.status, 404);
  EXPECT_EQ(response.body.at("error"), "syncUnavailable");
  EXPECT_TRUE(response.body.at("message").is_string());
  EXPECT_FALSE(response.body.at("message").get<std::string>().empty());
}
