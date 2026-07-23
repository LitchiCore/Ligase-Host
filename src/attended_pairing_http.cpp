#include "attended_pairing_http.h"

#include <algorithm>
#include <cctype>
#include <map>
#include <regex>
#include <set>

namespace attended_pairing::http {
  namespace {
    using json = nlohmann::json;
    constexpr std::string_view base_path = "/ligase/v1/pairing/requests";
    const std::regex request_id_pattern {
      "^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"
    };

    std::string lower(std::string value) {
      std::ranges::transform(value, value.begin(), [](unsigned char c) {
        return static_cast<char>(std::tolower(c));
      });
      return value;
    }

    std::vector<std::string> header_values(
      const request &input,
      std::string_view name
    ) {
      std::vector<std::string> result;
      for (const auto &[candidate, value] : input.headers) {
        if (lower(candidate) == lower(std::string(name))) result.push_back(value);
      }
      return result;
    }

    response json_response(int status, const json &value) {
      const auto text = value.dump();
      return {
        .status = status,
        .headers = {
          {"Content-Type", "application/json"},
          {"Cache-Control", "no-store"}
        },
        .body = bytes(text.begin(), text.end())
      };
    }

    response empty_response(int status) {
      return {.status = status, .headers = {{"Cache-Control", "no-store"}}};
    }

    json error_json(
      std::string code,
      std::optional<std::string> detail = {},
      std::optional<std::string> path = {},
      std::optional<std::string> current_state = {}
    ) {
      json value {{"code", std::move(code)}};
      if (detail) value["detail"] = *detail;
      if (path) value["path"] = *path;
      if (current_state) value["currentState"] = *current_state;
      return value;
    }

    response invalid_request(
      std::string detail,
      std::optional<std::string> path = {}
    ) {
      return json_response(
        400, error_json("invalidRequest", std::move(detail), std::move(path)));
    }

    bool has_body(const request &input) {
      return !input.body.empty();
    }

    std::optional<response> require_empty_body(const request &input) {
      if (has_body(input)) return invalid_request("unexpectedBody");
      return std::nullopt;
    }

    std::optional<response> require_json_body(
      const request &input,
      std::size_t limit
    ) {
      const auto encoding = header_values(input, "Content-Encoding");
      if (!encoding.empty()) {
        return json_response(
          415,
          error_json(
            "unsupportedMediaType", "contentEncodingNotSupported"));
      }
      const auto content_types = header_values(input, "Content-Type");
      if (content_types.size() != 1) {
        return json_response(415, error_json("unsupportedMediaType"));
      }
      const auto media = lower(content_types.front());
      static const std::regex json_media_type {
        R"(^[ \t]*application/json[ \t]*(;[ \t]*charset[ \t]*=[ \t]*(utf-8|"utf-8")[ \t]*)?$)"
      };
      if (!std::regex_match(media, json_media_type)) {
        return json_response(415, error_json("unsupportedMediaType"));
      }
      if (input.body.empty() || input.body.size() > limit) {
        return invalid_request("invalidValue", "");
      }
      return std::nullopt;
    }

    struct parsed_json {
      json value;
      std::optional<std::string> duplicate_path;
    };

    std::string pointer_token(std::string_view value) {
      std::string result;
      for (const auto character : value) {
        if (character == '~') result += "~0";
        else if (character == '/') result += "~1";
        else result += character;
      }
      return result;
    }

    bool utf8_ordinal_less(
      const std::string &left,
      const std::string &right
    ) {
      return std::lexicographical_compare(
        left.begin(), left.end(), right.begin(), right.end(),
        [](char first, char second) {
          return static_cast<std::uint8_t>(first)
            < static_cast<std::uint8_t>(second);
        });
    }

    parsed_json parse_strict(std::span<const std::uint8_t> body) {
      struct object_frame {
        std::set<std::string> keys;
        std::string path;
        std::string current_key;
      };
      std::vector<object_frame> objects;
      std::vector<std::string> duplicates;
      const std::string text(body.begin(), body.end());
      auto callback = [&](int, json::parse_event_t event, json &parsed) {
        if (event == json::parse_event_t::object_start) {
          std::string path;
          if (!objects.empty()) {
            path = objects.back().path + "/"
              + pointer_token(objects.back().current_key);
          }
          objects.push_back({.path = std::move(path)});
        }
        if (event == json::parse_event_t::key && !objects.empty()) {
          const auto key = parsed.get<std::string>();
          objects.back().current_key = key;
          if (!objects.back().keys.insert(key).second) {
            duplicates.push_back(
              objects.back().path + "/" + pointer_token(key));
          }
        }
        if (event == json::parse_event_t::object_end && !objects.empty()) {
          objects.pop_back();
        }
        return true;
      };
      auto value = json::parse(text, callback, true, false);
      std::ranges::sort(duplicates, utf8_ordinal_less);
      return {
        .value = std::move(value),
        .duplicate_path =
          duplicates.empty()
            ? std::optional<std::string> {}
            : std::optional<std::string> {duplicates.front()}
      };
    }

    std::optional<response> exact_object(
      const json &value,
      std::initializer_list<std::string_view> required,
      std::string_view path = ""
    ) {
      if (!value.is_object()) {
        return invalid_request("invalidType", std::string(path));
      }
      std::set<std::string> allowed;
      for (auto item : required) allowed.emplace(item);
      std::vector<std::string> unknown_paths;
      for (const auto &[key, ignored] : value.items()) {
        if (!allowed.contains(key)) {
          unknown_paths.push_back(
            std::string(path) + "/" + pointer_token(key));
        }
      }
      if (!unknown_paths.empty()) {
        std::ranges::sort(unknown_paths, utf8_ordinal_less);
        return invalid_request("unknownField", unknown_paths.front());
      }
      for (const auto &key : allowed) {
        if (!value.contains(key)) {
          return invalid_request(
            "missingField", std::string(path) + "/" + key);
        }
      }
      return std::nullopt;
    }

    bool unicode_whitespace(std::uint32_t value) {
      return (value >= 0x0009 && value <= 0x000d)
        || value == 0x0020 || value == 0x0085 || value == 0x00a0
        || value == 0x1680 || (value >= 0x2000 && value <= 0x200a)
        || value == 0x2028 || value == 0x2029 || value == 0x202f
        || value == 0x205f || value == 0x3000;
    }

    bool valid_name(std::string_view name) {
      if (name.empty() || name.size() > 320) return false;
      std::vector<std::uint32_t> scalars;
      for (std::size_t index = 0; index < name.size();) {
        const auto first = static_cast<std::uint8_t>(name[index++]);
        std::uint32_t scalar = 0;
        std::size_t continuation = 0;
        if (first < 0x80) scalar = first;
        else if ((first & 0xe0) == 0xc0) {
          scalar = first & 0x1f;
          continuation = 1;
        } else if ((first & 0xf0) == 0xe0) {
          scalar = first & 0x0f;
          continuation = 2;
        } else if ((first & 0xf8) == 0xf0) {
          scalar = first & 0x07;
          continuation = 3;
        } else {
          return false;
        }
        if (index + continuation > name.size()) return false;
        while (continuation-- != 0) {
          const auto next = static_cast<std::uint8_t>(name[index++]);
          if ((next & 0xc0) != 0x80) return false;
          scalar = (scalar << 6) | (next & 0x3f);
        }
        scalars.push_back(scalar);
      }
      return scalars.size() >= 1 && scalars.size() <= 80
        && !unicode_whitespace(scalars.front())
        && !unicode_whitespace(scalars.back());
    }

    std::optional<std::string> path_request_id(
      std::string_view path,
      std::string_view suffix = {}
    ) {
      const auto prefix = std::string(base_path) + "/";
      if (!path.starts_with(prefix)) return std::nullopt;
      auto tail = std::string(path.substr(prefix.size()));
      if (!suffix.empty()) {
        const auto ending = "/" + std::string(suffix);
        if (!tail.ends_with(ending)) return std::nullopt;
        tail.resize(tail.size() - ending.size());
      } else if (tail.contains('/')) {
        return std::nullopt;
      }
      if (!std::regex_match(tail, request_id_pattern)) return std::nullopt;
      return tail;
    }

    std::optional<bytes> bearer(const request &input) {
      const auto values = header_values(input, "Authorization");
      if (values.size() != 1 || !values.front().starts_with("Bearer ")) {
        return std::nullopt;
      }
      try {
        auto token = base64url_decode(
          std::string_view(values.front()).substr(7));
        if (token.size() != 32) return std::nullopt;
        return token;
      } catch (...) {
        return std::nullopt;
      }
    }

    json status_json(const status_output &status) {
      return {
        {"expiresAt", status.expires_at},
        {"failure", status.failure ? json(*status.failure) : json(nullptr)},
        {"requestId", status.request_id},
        {"state", request_state_name(status.state)}
      };
    }

    response service_failure(const service_exception &error) {
      switch (error.code()) {
        case service_error::invalid_request:
          return invalid_request(error.detail().empty() ? "invalidValue" : error.detail());
        case service_error::invalid_request_token:
          return json_response(401, error_json("invalidRequestToken"));
        case service_error::request_not_found:
          return json_response(404, error_json("requestNotFound"));
        case service_error::request_id_conflict:
          return json_response(409, error_json("requestIdConflict"));
        case service_error::client_certificate_busy:
          return json_response(409, error_json("clientCertificateBusy"));
        case service_error::invalid_state:
          return json_response(
            409,
            error_json(
              "invalidState",
              error.detail().empty()
                ? std::optional<std::string> {}
                : std::optional<std::string> {error.detail()},
              {},
              error.current_state()
                ? std::optional<std::string> {
                    request_state_name(*error.current_state())}
                : std::optional<std::string> {}));
        case service_error::invalid_envelope:
          return json_response(
            400,
            error_json(
              "invalidEnvelope",
              error.detail().empty() ? "authenticationFailed" : error.detail()));
        case service_error::nonce_reuse:
          return json_response(409, error_json("nonceReuse"));
        case service_error::envelope_conflict:
          return json_response(409, error_json("envelopeConflict"));
        case service_error::request_expired:
          return json_response(410, error_json("requestExpired"));
        case service_error::rate_limited:
          return json_response(429, error_json("rateLimited"));
        case service_error::pairing_unavailable:
          return json_response(503, error_json("pairingUnavailable"));
      }
      return json_response(503, error_json("pairingUnavailable"));
    }
  }

  router::router(pairing_service &service): service_(service) {
  }

  response router::handle(
    const request &input,
    pairing_service::steady_clock::time_point monotonic_now,
    pairing_service::wall_clock::time_point wall_now
  ) {
    try {
      if (input.path == base_path && input.method == "POST") {
        if (auto rejected = require_json_body(input, 4096)) return *rejected;
        parsed_json parsed;
        try {
          parsed = parse_strict(input.body);
        } catch (...) {
          return invalid_request("invalidJson");
        }
        if (parsed.duplicate_path) {
          return invalid_request("duplicateField", *parsed.duplicate_path);
        }
        if (auto rejected = exact_object(
              parsed.value,
              {"version", "requestId", "device", "clientEphemeralKey",
               "clientNonce", "clientCertificateSha256"})) {
          return *rejected;
        }
        if (auto rejected = exact_object(
              parsed.value["device"], {"name", "platform"}, "/device")) {
          return *rejected;
        }
        const std::array typed_fields {
          std::pair {"/clientCertificateSha256",
                     &parsed.value["clientCertificateSha256"]},
          std::pair {"/clientEphemeralKey",
                     &parsed.value["clientEphemeralKey"]},
          std::pair {"/clientNonce", &parsed.value["clientNonce"]},
          std::pair {"/device/name", &parsed.value["device"]["name"]},
          std::pair {"/device/platform", &parsed.value["device"]["platform"]},
          std::pair {"/requestId", &parsed.value["requestId"]}
        };
        for (const auto &[path, field] : typed_fields) {
          if (!field->is_string()) return invalid_request("invalidType", path);
        }
        if (!parsed.value["version"].is_number_integer()) {
          return invalid_request("invalidType", "/version");
        }
        const auto request_id = parsed.value["requestId"].get<std::string>();
        const auto name = parsed.value["device"]["name"].get<std::string>();
        create_input create;
        create.request_id = request_id;
        create.device = {.name = name, .platform = "android"};
        create.canonical_jcs = jcs(parsed.value);
        create.source = input.source;
        try {
          create.client_certificate_sha256 = base64url_decode_32(
            parsed.value["clientCertificateSha256"].get<std::string>());
        } catch (...) {
          return invalid_request("invalidValue", "/clientCertificateSha256");
        }
        try {
          create.client_ephemeral_key = base64url_decode_32(
            parsed.value["clientEphemeralKey"].get<std::string>());
        } catch (...) {
          return invalid_request("invalidValue", "/clientEphemeralKey");
        }
        try {
          create.client_nonce = base64url_decode_32(
            parsed.value["clientNonce"].get<std::string>());
        } catch (...) {
          return invalid_request("invalidValue", "/clientNonce");
        }
        if (!valid_name(name)) {
          return invalid_request("invalidValue", "/device/name");
        }
        if (parsed.value["device"]["platform"] != "android") {
          return invalid_request("invalidValue", "/device/platform");
        }
        if (!std::regex_match(request_id, request_id_pattern)) {
          return invalid_request("invalidValue", "/requestId");
        }
        if (parsed.value["version"] != 1) {
          return invalid_request("invalidValue", "/version");
        }
        const auto result = service_.create(create, monotonic_now, wall_now);
        return json_response(result.status, {
          {"expiresAt", result.expires_at},
          {"hostCertificateSha256", base64url_encode(result.host_certificate_sha256)},
          {"hostEphemeralKey", base64url_encode(result.host_ephemeral_key)},
          {"hostNonce", base64url_encode(result.host_nonce)},
          {"hostUniqueId", result.host_unique_id},
          {"requestId", result.request_id},
          {"requestToken", result.request_token},
          {"version", 1}
        });
      }

      if (input.path == base_path && input.method == "GET") {
        if (!input.loopback) {
          return json_response(404, error_json("requestNotFound"));
        }
        if (auto rejected = require_empty_body(input)) return *rejected;
        json requests = json::array();
        for (const auto &item : service_.pending(monotonic_now)) {
          json source {{"address", item.source.address}};
          if (item.source.scope_id != 0) source["scopeId"] = item.source.scope_id;
          requests.push_back({
            {"createdAt", item.created_at},
            {"device", {{"name", item.device.name}, {"platform", item.device.platform}}},
            {"expiresAt", item.expires_at},
            {"readyForApproval", item.ready_for_approval},
            {"requestId", item.request_id},
            {"safetyCode", item.safety_code},
            {"sourceAddress", source},
            {"state", request_state_name(item.state)}
          });
        }
        return json_response(200, {{"requests", requests}, {"version", 1}});
      }

      enum class action { status, cancel, envelope, allow, reject };
      std::optional<std::string> request_id;
      action selected = action::status;
      if (input.method == "PUT") {
        request_id = path_request_id(input.path, "envelope");
        selected = action::envelope;
      } else if (input.method == "DELETE") {
        request_id = path_request_id(input.path);
        selected = action::cancel;
      } else if (input.method == "GET") {
        request_id = path_request_id(input.path);
      } else if (input.method == "POST") {
        if ((request_id = path_request_id(input.path, "allow"))) {
          selected = action::allow;
        } else if ((request_id = path_request_id(input.path, "reject"))) {
          selected = action::reject;
        }
      }
      if (!request_id) {
        return json_response(404, error_json("requestNotFound"));
      }
      if (!service_.exists(*request_id, monotonic_now)) {
        return json_response(404, error_json("requestNotFound"));
      }

      if (selected == action::allow || selected == action::reject) {
        if (!input.loopback) {
          return json_response(404, error_json("requestNotFound"));
        }
        if (auto rejected = require_empty_body(input)) return *rejected;
        if (selected == action::allow) {
          const auto [value, changed] =
            service_.allow(*request_id, monotonic_now);
          return json_response(changed ? 202 : 200, status_json(value));
        }
        return json_response(
          200,
          status_json(service_.reject(*request_id, monotonic_now)));
      }

      const auto token = bearer(input);
      if (!token) {
        return json_response(401, error_json("invalidRequestToken"));
      }
      const auto authenticated = service_.status(
        *request_id, *token, input.source, monotonic_now);
      if (selected == action::status) {
        if (auto rejected = require_empty_body(input)) return *rejected;
        return json_response(200, status_json(authenticated));
      }
      if (selected == action::cancel) {
        if (auto rejected = require_empty_body(input)) return *rejected;
        service_.cancel(*request_id, *token, input.source, monotonic_now);
        return empty_response(204);
      }
      if (authenticated.state == request_state::expired) {
        return json_response(410, error_json("requestExpired"));
      }

      if (auto rejected = require_json_body(input, 1024)) return *rejected;
      parsed_json parsed;
      try {
        parsed = parse_strict(input.body);
      } catch (...) {
        return invalid_request("invalidJson");
      }
      if (parsed.duplicate_path) {
        return invalid_request("duplicateField", *parsed.duplicate_path);
      }
      if (auto rejected = exact_object(parsed.value, {"nonce", "ciphertext"})) {
        return *rejected;
      }
      if (!parsed.value["nonce"].is_string()
          || !parsed.value["ciphertext"].is_string()) {
        return invalid_request(
          "invalidType",
          !parsed.value["ciphertext"].is_string()
            ? "/ciphertext"
            : "/nonce");
      }
      bytes12 nonce;
      bytes ciphertext;
      try {
        ciphertext =
          base64url_decode(parsed.value["ciphertext"].get<std::string>());
      } catch (...) {
        return invalid_request("invalidValue", "/ciphertext");
      }
      if (ciphertext.size() != 36) {
        return invalid_request("invalidValue", "/ciphertext");
      }
      try {
        nonce = base64url_decode_12(
          parsed.value["nonce"].get<std::string>());
      } catch (...) {
        return invalid_request("invalidValue", "/nonce");
      }
      service_.upload_envelope(
        *request_id,
        *token,
        input.source,
        nonce,
        ciphertext,
        jcs(parsed.value),
        monotonic_now
      );
      return empty_response(204);
    } catch (const protocol_error &) {
      return invalid_request("invalidValue", "");
    } catch (const service_exception &error) {
      return service_failure(error);
    } catch (...) {
      return json_response(503, error_json("pairingUnavailable"));
    }
  }
}  // namespace attended_pairing::http
