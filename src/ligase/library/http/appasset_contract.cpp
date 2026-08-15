/**
 * @file src/ligase/library/http/appasset_contract.cpp
 */
#include "src/ligase/library/http/appasset_contract.h"

#include <charconv>
#include <limits>

namespace ligase::library::http {
  bool is_canonical_appasset_id(std::string_view value) {
    if (value.empty() || value.front() == '0') return false;
    std::uint32_t parsed = 0;
    const auto [end, error] = std::from_chars(
      value.data(), value.data() + value.size(), parsed);
    return error == std::errc {} && end == value.data() + value.size() &&
           parsed > 0 &&
           parsed <= static_cast<std::uint32_t>(std::numeric_limits<int>::max());
  }

  bool is_canonical_uuid_d(std::string_view value) {
    if (value.size() != 36 || value[8] != '-' || value[13] != '-' ||
        value[18] != '-' || value[23] != '-') return false;
    for (std::size_t index = 0; index < value.size(); ++index) {
      if (index == 8 || index == 13 || index == 18 || index == 23) continue;
      const auto character = value[index];
      if (!((character >= '0' && character <= '9') ||
            (character >= 'a' && character <= 'f'))) return false;
    }
    return true;
  }

  bool is_lower_sha256(std::string_view value) {
    if (value.size() != 64) return false;
    for (const auto character : value) {
      if (!((character >= '0' && character <= '9') ||
            (character >= 'a' && character <= 'f'))) return false;
    }
    return true;
  }

  appasset_contract_result validate_appasset(const appasset_contract_input &input) {
    if (!is_canonical_uuid_d(input.app_uuid))
      return {409, "assetIdentityMismatch"};
    if (input.expected_sha256.empty() || !input.file_readable)
      return {404, "assetUnavailable"};
    if (input.content_length == 0 || input.content_length > appasset_max_bytes)
      return {413, "assetTooLarge"};
    if (!is_lower_sha256(input.expected_sha256) ||
        !is_lower_sha256(input.actual_sha256) ||
        input.expected_sha256 != input.actual_sha256)
      return {409, "assetCorrelationMismatch"};
    return {200, "ok"};
  }
}
