/**
 * @file src/ligase/library/http/appasset_contract.h
 * @brief Pure validation for the Android appasset authority response.
 */
#pragma once

#include <cstdint>
#include <string>
#include <string_view>

namespace ligase::library::http {
  constexpr std::uintmax_t appasset_max_bytes = 8U * 1024U * 1024U;

  struct appasset_contract_input {
    std::string app_uuid;
    std::string expected_sha256;
    std::string actual_sha256;
    std::uintmax_t content_length;
    bool file_readable;
  };

  struct appasset_contract_result {
    int status;
    std::string code;

    [[nodiscard]] bool accepted() const noexcept {
      return status == 200;
    }
  };

  [[nodiscard]] bool is_canonical_appasset_id(std::string_view value);
  [[nodiscard]] bool is_canonical_uuid_d(std::string_view value);
  [[nodiscard]] bool is_lower_sha256(std::string_view value);
  [[nodiscard]] appasset_contract_result validate_appasset(
    const appasset_contract_input &input
  );
}
