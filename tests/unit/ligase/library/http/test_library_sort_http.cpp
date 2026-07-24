#include <gtest/gtest.h>

#include "src/ligase/library/http/library_sort_http.h"

namespace {
  using ligase::library::http::handle_sort;
  using ligase::library::http::manual_sort_revision_valid;
  using ligase::library::http::manual_sort_uuid_valid;
  using ligase::library::http::sort_dependencies;
  using ligase::library::http::sort_request;

  constexpr auto first_id = "11111111-1111-4111-8111-111111111111";
  constexpr auto second_id = "22222222-2222-4222-8222-222222222222";
  constexpr auto hidden_id = "33333333-3333-4333-8333-333333333333";

  struct harness {
    std::mutex mutex;
    nlohmann::json library {
      {"revision", 7},
      {"sortMode", 5},
      {"items", nlohmann::json::array({
        {{"id", first_id}},
        {{"id", second_id}},
        {{"id", hidden_id}}
      })}
    };
    nlohmann::json sync {
      {"library", {
        {"revision", 7},
        {"sortMode", "manual"},
        {"items", nlohmann::json::array({
          {{"id", first_id}, {"publishedToClients", true}},
          {{"id", second_id}, {"publishedToClients", true}},
          {{"id", hidden_id}, {"publishedToClients", false}}
        })}
      }}
    };
    bool fail_sync_save = false;
    int library_saves = 0;
    int sync_saves = 0;

    sort_dependencies dependencies() {
      return {
        .mutation_mutex = mutex,
        .load_library = [this]() { return library; },
        .load_sync = [this]() { return sync; },
        .save_library = [this](const auto &value) {
          ++library_saves;
          library = value;
        },
        .save_sync = [this](const auto &value) {
          ++sync_saves;
          if (fail_sync_save && sync_saves == 1) {
            throw std::runtime_error("injected");
          }
          sync = value;
        },
        .timestamp = []() { return "2026-07-25T00:00:00Z"; }
      };
    }

    std::string body(
      std::int64_t revision = 7,
      nlohmann::json order = nlohmann::json::array({second_id, first_id})
    ) {
      return nlohmann::json {
        {"baseRevision", revision},
        {"orderedAppUuids", std::move(order)}
      }.dump();
    }
  };
}

TEST(LibrarySortHttp, SafeRevisionAndCanonicalUuidRemainStrict) {
  EXPECT_TRUE(manual_sort_revision_valid(1));
  EXPECT_TRUE(manual_sort_revision_valid(9007199254740991LL));
  EXPECT_FALSE(manual_sort_revision_valid(0));
  EXPECT_FALSE(manual_sort_revision_valid(9007199254740992LL));
  EXPECT_TRUE(manual_sort_uuid_valid(first_id));
  EXPECT_FALSE(manual_sort_uuid_valid("F3D67F4D-B1FE-4C5D-A77E-B78A51051C1A"));
  EXPECT_FALSE(manual_sort_uuid_valid("776710782"));
}

TEST(LibrarySortHttp, PermissionAndStrictBodyErrorsAreStable) {
  harness value;
  auto dependencies = value.dependencies();
  auto response = handle_sort({false, value.body()}, dependencies);
  EXPECT_EQ(response.status, 403);
  EXPECT_EQ(response.body, nlohmann::json({{"error", "permissionDenied"}}));
  EXPECT_EQ(value.library_saves, 0);

  response = handle_sort({true, "{"}, dependencies);
  EXPECT_EQ(response.status, 400);
  EXPECT_EQ(response.body.at("error"), "invalidJson");
  EXPECT_TRUE(response.body.contains("message"));

  response = handle_sort(
    {true, R"({"baseRevision":7,"orderedAppUuids":[],"later":true})"},
    dependencies);
  EXPECT_EQ(response.status, 400);
  EXPECT_EQ(response.body, nlohmann::json({{"error", "invalidManualOrder"}}));

  response = handle_sort({true, value.body(0)}, dependencies);
  EXPECT_EQ(response.status, 400);
  response = handle_sort(
    {true, R"({"baseRevision":9007199254740992,"orderedAppUuids":[]})"},
    dependencies);
  EXPECT_EQ(response.status, 400);
  response = handle_sort(
    {true, value.body(7, nlohmann::json::array({first_id, first_id}))},
    dependencies);
  EXPECT_EQ(response.status, 400);
}

TEST(LibrarySortHttp, RevisionAndExactPublishedSetRemainFailClosed) {
  harness value;
  auto dependencies = value.dependencies();
  auto response = handle_sort({true, value.body(6)}, dependencies);
  EXPECT_EQ(response.status, 409);
  EXPECT_EQ(response.body, (nlohmann::json {
    {"error", "revisionConflict"},
    {"currentRevision", 7}
  }));

  response = handle_sort(
    {true, value.body(7, nlohmann::json::array({first_id}))},
    dependencies);
  EXPECT_EQ(response.status, 400);
  EXPECT_EQ(response.body, nlohmann::json({{"error", "invalidManualOrder"}}));
}

TEST(LibrarySortHttp, SuccessReordersPublishedAndAppendsHiddenAtomically) {
  harness value;
  auto response = handle_sort(
    {true, value.body()},
    value.dependencies());
  ASSERT_EQ(response.status, 200);
  EXPECT_EQ(response.body, (nlohmann::json {
    {"revision", 8},
    {"sortMode", "manual"},
    {"orderedAppUuids", nlohmann::json::array({second_id, first_id})}
  }));
  EXPECT_EQ(value.library["revision"], 8);
  EXPECT_EQ(value.sync["library"]["revision"], 8);
  EXPECT_EQ(value.library["items"][0]["id"], second_id);
  EXPECT_EQ(value.library["items"][1]["id"], first_id);
  EXPECT_EQ(value.library["items"][2]["id"], hidden_id);
  EXPECT_EQ(value.sync["library"]["items"][0]["id"], second_id);
  EXPECT_EQ(value.sync["library"]["items"][1]["id"], first_id);
  EXPECT_EQ(value.sync["library"]["items"][2]["id"], hidden_id);
}

TEST(LibrarySortHttp, FailedSecondWriteRollsBackBothProjections) {
  harness value;
  const auto original_library = value.library;
  const auto original_sync = value.sync;
  value.fail_sync_save = true;
  auto response = handle_sort(
    {true, value.body()},
    value.dependencies());
  EXPECT_EQ(response.status, 500);
  EXPECT_EQ(response.body, nlohmann::json({{"error", "libraryUpdateFailed"}}));
  EXPECT_EQ(value.library, original_library);
  EXPECT_EQ(value.sync, original_sync);
  EXPECT_EQ(value.library_saves, 2);
  EXPECT_EQ(value.sync_saves, 2);
}
