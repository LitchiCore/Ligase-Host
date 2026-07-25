/**
 * @file src/ligase/library/http/library_applist_http.cpp
 * @brief Read-only use-case orchestration for the Moonlight application list.
 */

#include "src/ligase/library/http/library_applist_http.h"

#include <sstream>
#include <utility>

#include <boost/property_tree/ptree.hpp>
#include <boost/property_tree/xml_parser.hpp>

namespace ligase::library::http {
  namespace {
    namespace pt = boost::property_tree;

    void append_app(
      pt::ptree &apps,
      const int hdr_supported,
      const std::string &title,
      const std::string &uuid,
      const std::string &idx,
      const std::string &id
    ) {
      pt::ptree app_node;
      app_node.put("IsHdrSupported", hdr_supported);
      app_node.put("AppTitle", title);
      app_node.put("UUID", uuid);
      app_node.put("IDX", idx);
      app_node.put("ID", id);
      apps.push_back(std::make_pair("App", std::move(app_node)));
    }
  }

  applist_response handle_applist(
    const applist_request &request,
    const applist_dependencies &dependencies
  ) {
    pt::ptree tree;
    auto &apps = tree.add_child("root", pt::ptree {});
    apps.put("<xmlattr>.status_code", 200);

    std::exception_ptr failure;
    try {
      if (!request.authorized) {
        append_app(apps, 0, "Permission Denied", "", "0", "114514");
      } else {
        auto app_list = dependencies.load_apps();
        std::size_t bits = 0;
        if (request.legacy_ordering) {
          bits = dependencies.ordering_width(app_list.size());
        }

        const auto hide_inactive_apps =
          request.input_only_mode
          && request.current_app_id > 0
          && request.current_app_id != request.input_only_app_id;

        for (std::size_t index = 0; index < app_list.size(); ++index) {
          const auto &app = app_list[index];
          const auto numeric_id = dependencies.parse_id(app.id);
          if (hide_inactive_apps) {
            if (
              numeric_id != request.current_app_id
              && numeric_id != request.input_only_app_id
              && numeric_id != request.terminate_app_id
            ) {
              continue;
            }
          } else if (numeric_id == request.terminate_app_id) {
            continue;
          }

          const auto title = request.legacy_ordering
            ? dependencies.order_title(app.name, bits, index)
            : app.name;
          append_app(
            apps,
            request.hdr_supported ? 1 : 0,
            title,
            app.uuid,
            app.idx,
            app.id
          );
        }
      }
    } catch (...) {
      failure = std::current_exception();
    }

    std::ostringstream data;
    pt::write_xml(data, tree);
    return {data.str(), failure};
  }
}
