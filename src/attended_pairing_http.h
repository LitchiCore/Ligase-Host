#pragma once

#include "attended_pairing_service.h"

#include <string>
#include <utility>
#include <vector>

namespace attended_pairing::http {
  struct request {
    std::string method;
    std::string path;
    std::vector<std::pair<std::string, std::string>> headers;
    bytes body;
    source_identity source;
    bool loopback = false;
  };

  struct response {
    int status = 500;
    std::vector<std::pair<std::string, std::string>> headers;
    bytes body;
  };

  class router {
  public:
    explicit router(pairing_service &service);
    response handle(
      const request &input,
      pairing_service::steady_clock::time_point monotonic_now,
      pairing_service::wall_clock::time_point wall_now
    );

  private:
    pairing_service &service_;
  };
}  // namespace attended_pairing::http
