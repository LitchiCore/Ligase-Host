#include <gtest/gtest.h>

#include "src/ligase/library/http/appasset_contract.h"

namespace {
  using ligase::library::http::appasset_contract_input;
  using ligase::library::http::appasset_max_bytes;
  using ligase::library::http::is_canonical_appasset_id;
  using ligase::library::http::validate_appasset;

  constexpr auto uuid = "11111111-1111-4111-8111-111111111111";
  const std::string sha(64, 'a');

  appasset_contract_input valid() {
    return {
      .app_uuid = uuid,
      .expected_sha256 = sha,
      .actual_sha256 = sha,
      .content_length = 4096,
      .file_readable = true
    };
  }
}

TEST(AppAssetContract, AcceptsOnlyCanonicalLaunchId) {
  EXPECT_TRUE(is_canonical_appasset_id("1"));
  EXPECT_TRUE(is_canonical_appasset_id("2147483647"));
  for (const auto value : {"", "0", "01", "+1", "1.0", "2147483648"})
    EXPECT_FALSE(is_canonical_appasset_id(value));
}

TEST(AppAssetContract, CorrelatesUuidExpectedHashAndActualBytes) {
  EXPECT_EQ(validate_appasset(valid()).status, 200);

  auto missing = valid();
  missing.expected_sha256.clear();
  EXPECT_EQ(validate_appasset(missing).status, 404);
  EXPECT_EQ(validate_appasset(missing).code, "assetUnavailable");

  auto unreadable = valid();
  unreadable.file_readable = false;
  EXPECT_EQ(validate_appasset(unreadable).status, 404);

  auto mismatch = valid();
  mismatch.actual_sha256 = std::string(64, 'b');
  EXPECT_EQ(validate_appasset(mismatch).status, 409);
  EXPECT_EQ(validate_appasset(mismatch).code, "assetCorrelationMismatch");

  auto bad_uuid = valid();
  bad_uuid.app_uuid = "11111111-1111-4111-8111-11111111111A";
  EXPECT_EQ(validate_appasset(bad_uuid).code, "assetIdentityMismatch");
}

TEST(AppAssetContract, RejectsEmptyAndOversizedAssets) {
  auto empty = valid();
  empty.content_length = 0;
  EXPECT_EQ(validate_appasset(empty).status, 413);

  auto oversized = valid();
  oversized.content_length = appasset_max_bytes + 1;
  EXPECT_EQ(validate_appasset(oversized).status, 413);
  EXPECT_EQ(validate_appasset(oversized).code, "assetTooLarge");
}
