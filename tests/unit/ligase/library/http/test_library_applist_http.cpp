#include <gtest/gtest.h>

#include <stdexcept>
#include <string>
#include <vector>

#include "src/ligase/library/http/library_applist_http.h"

namespace {
  using ligase::library::http::applist_app;
  using ligase::library::http::applist_dependencies;
  using ligase::library::http::applist_request;
  using ligase::library::http::handle_applist;

  constexpr auto xml_header = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";

  struct harness {
    std::vector<applist_app> apps;
    int loads = 0;
    int widths = 0;
    int titles = 0;

    applist_dependencies dependencies() {
      return {
        .load_apps = [this]() {
          ++loads;
          return apps;
        },
        .parse_id = [](const std::string_view id) {
          return std::stoi(std::string(id));
        },
        .ordering_width = [this](const std::size_t count) {
          ++widths;
          return count > 1 ? std::size_t {1} : std::size_t {0};
        },
        .order_title = [this](
                         const std::string_view title,
                         const std::size_t,
                         const std::size_t index
                       ) {
          ++titles;
          return std::to_string(index) + ":" + std::string(title);
        }
      };
    }
  };

  applist_request request(const bool authorized = true) {
    return {
      .authorized = authorized,
      .hdr_supported = true,
      .input_only_mode = false,
      .current_app_id = 0,
      .input_only_app_id = 1,
      .terminate_app_id = 2,
      .legacy_ordering = false
    };
  }

  TEST(LibraryApplistHttp, OperateAndObserveShareTheExactReadProjection) {
    harness h {
      .apps = {
        {"Desktop", "11111111-1111-4111-8111-111111111111", "0", "10"},
        {"Virtual Desktop", "22222222-2222-4222-8222-222222222222", "1", "11"},
        {"Game", "33333333-3333-4333-8333-333333333333", "2", "12"},
        {"Terminate", "", "3", "2"}
      }
    };

    const auto output = handle_applist(request(), h.dependencies());

    EXPECT_EQ(
      output.body,
      std::string(xml_header)
        + "<root status_code=\"200\"><App><IsHdrSupported>1</IsHdrSupported><AppTitle>Desktop</AppTitle>"
          "<UUID>11111111-1111-4111-8111-111111111111</UUID><IDX>0</IDX><ID>10</ID></App>"
          "<App><IsHdrSupported>1</IsHdrSupported><AppTitle>Virtual Desktop</AppTitle>"
          "<UUID>22222222-2222-4222-8222-222222222222</UUID><IDX>1</IDX><ID>11</ID></App>"
          "<App><IsHdrSupported>1</IsHdrSupported><AppTitle>Game</AppTitle>"
          "<UUID>33333333-3333-4333-8333-333333333333</UUID><IDX>2</IDX><ID>12</ID></App></root>"
    );
    EXPECT_EQ(h.loads, 1);
  }

  TEST(LibraryApplistHttp, UnauthorizedPreservesTheMoonlightSentinelResponse) {
    harness h {
      .apps = {{"Must Not Load", "11111111-1111-4111-8111-111111111111", "0", "10"}}
    };

    const auto output = handle_applist(request(false), h.dependencies());

    EXPECT_EQ(
      output.body,
      std::string(xml_header)
        + "<root status_code=\"200\"><App><IsHdrSupported>0</IsHdrSupported>"
          "<AppTitle>Permission Denied</AppTitle><UUID/><IDX>0</IDX><ID>114514</ID></App></root>"
    );
    EXPECT_EQ(h.loads, 0);
  }

  TEST(LibraryApplistHttp, EmptyProjectionPreservesTheEmptySuccessDocument) {
    harness h;

    const auto output = handle_applist(request(), h.dependencies());

    EXPECT_EQ(output.body, std::string(xml_header) + "<root status_code=\"200\"/>");
  }

  TEST(LibraryApplistHttp, MissingProjectionPreservesTheFailGuardResponse) {
    harness h;
    auto dependencies = h.dependencies();
    dependencies.load_apps = []() -> std::vector<applist_app> {
      throw std::runtime_error("projection unavailable");
    };

    const auto output = handle_applist(request(), dependencies);

    EXPECT_EQ(output.body, std::string(xml_header) + "<root status_code=\"200\"/>");
    EXPECT_NE(output.failure, nullptr);
  }

  TEST(LibraryApplistHttp, MalformedNumericIdPreservesThePartialFailGuardResponse) {
    harness h {
      .apps = {
        {"First", "11111111-1111-4111-8111-111111111111", "0", "10"},
        {"Malformed", "22222222-2222-4222-8222-222222222222", "1", "not-an-id"}
      }
    };

    const auto output = handle_applist(request(), h.dependencies());

    EXPECT_NE(output.body.find("<AppTitle>First</AppTitle>"), std::string::npos);
    EXPECT_EQ(output.body.find("Malformed"), std::string::npos);
    EXPECT_NE(output.failure, nullptr);
  }

  TEST(LibraryApplistHttp, InputOnlyModePreservesSourceOrderAndVisibilityRules) {
    harness h {
      .apps = {
        {"Hidden", "11111111-1111-4111-8111-111111111111", "0", "10"},
        {"Current", "22222222-2222-4222-8222-222222222222", "1", "20"},
        {"Input Only", "33333333-3333-4333-8333-333333333333", "2", "1"},
        {"Terminate", "", "3", "2"}
      }
    };
    auto input_only = request();
    input_only.input_only_mode = true;
    input_only.current_app_id = 20;

    const auto output = handle_applist(input_only, h.dependencies());

    EXPECT_EQ(output.body.find("Hidden"), std::string::npos);
    EXPECT_LT(output.body.find("Current"), output.body.find("Input Only"));
    EXPECT_LT(output.body.find("Input Only"), output.body.find("Terminate"));
  }

  TEST(LibraryApplistHttp, LegacyOrderingChangesOnlyTheProjectedTitle) {
    harness h {
      .apps = {
        {"First", "11111111-1111-4111-8111-111111111111", "7", "10"},
        {"Second", "22222222-2222-4222-8222-222222222222", "8", "11"}
      }
    };
    auto legacy = request();
    legacy.legacy_ordering = true;

    const auto output = handle_applist(legacy, h.dependencies());

    EXPECT_NE(output.body.find("<AppTitle>0:First</AppTitle>"), std::string::npos);
    EXPECT_NE(output.body.find("<AppTitle>1:Second</AppTitle>"), std::string::npos);
    EXPECT_NE(output.body.find("<IDX>7</IDX><ID>10</ID>"), std::string::npos);
    EXPECT_EQ(h.widths, 1);
    EXPECT_EQ(h.titles, 2);
  }

  TEST(LibraryApplistHttp, UpstreamSnapshotRemainsTheVisibilityBoundary) {
    harness h {
      .apps = {
        {"Unpublished Upstream App", "11111111-1111-4111-8111-111111111111", "0", "10"}
      }
    };

    const auto output = handle_applist(request(), h.dependencies());

    EXPECT_NE(output.body.find("Unpublished Upstream App"), std::string::npos);
  }
}
