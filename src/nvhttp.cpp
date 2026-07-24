/**
 * @file src/nvhttp.cpp
 * @brief Definitions for the nvhttp (GameStream) server.
 */
// macros
#define BOOST_BIND_GLOBAL_PLACEHOLDERS

// standard includes
#include <cctype>
#include <cstdlib>
#include <filesystem>
#include <format>
#include <fstream>
#include <iomanip>
#include <mutex>
#include <regex>
#include <sstream>
#include <string>
#include <utility>

// lib includes
#include <boost/asio/ssl/context.hpp>
#include <boost/asio/ssl/context_base.hpp>
#include <boost/property_tree/json_parser.hpp>
#include <boost/property_tree/ptree.hpp>
#include <boost/property_tree/xml_parser.hpp>
#include <Simple-Web-Server/server_http.hpp>

// local includes
#include "config.h"
#include "ligase/library/http/library_sort_http.h"
#include "ligase/pairing/http/attended_pairing_http.h"
#include "ligase/pairing/service/attended_pairing_service.h"
#include "display_device.h"
#include "file_handler.h"
#include "globals.h"
#include "httpcommon.h"
#include "logging.h"
#include "network.h"
#include "nvhttp.h"
#include "platform/common.h"
#include "process.h"
#include "rtsp.h"
#include "stream.h"
#include "system_tray.h"
#include "utility.h"
#include "uuid.h"
#include "video.h"
#include "zwpad.h"

#ifdef _WIN32
  #include <windows.h>
  #include "platform/windows/virtual_display.h"
#endif

using namespace std::literals;

namespace nvhttp {

  namespace fs = std::filesystem;
  namespace pt = boost::property_tree;

  using p_named_cert_t = crypto::p_named_cert_t;
  using PERM = crypto::PERM;

  struct client_t {
    std::vector<p_named_cert_t> named_devices;
  };

  struct pair_session_t;

  crypto::cert_chain_t cert_chain;
  static std::string one_time_pin;
  static std::string otp_passphrase;
  static std::string otp_device_name;
  static std::chrono::time_point<std::chrono::steady_clock> otp_creation_time;

  class SunshineHTTPSServer: public SimpleWeb::ServerBase<SunshineHTTPS> {
  public:
    SunshineHTTPSServer(const std::string &certification_file, const std::string &private_key_file):
        ServerBase<SunshineHTTPS>::ServerBase(443),
        context(boost::asio::ssl::context::tls_server) {
      // Disabling TLS 1.0 and 1.1 (see RFC 8996)
      context.set_options(boost::asio::ssl::context::no_tlsv1);
      context.set_options(boost::asio::ssl::context::no_tlsv1_1);
      context.use_certificate_chain_file(certification_file);
      context.use_private_key_file(private_key_file, boost::asio::ssl::context::pem);
    }

    std::function<bool(std::shared_ptr<Request>, SSL*)> verify;
    std::function<void(std::shared_ptr<Response>, std::shared_ptr<Request>)> on_verify_failed;

  protected:
    boost::asio::ssl::context context;

    void after_bind() override {
      if (verify) {
        context.set_verify_mode(boost::asio::ssl::verify_peer | boost::asio::ssl::verify_fail_if_no_peer_cert | boost::asio::ssl::verify_client_once);
        context.set_verify_callback([](int verified, boost::asio::ssl::verify_context &ctx) {
          // To respond with an error message, a connection must be established
          return 1;
        });
      }
    }

    // This is Server<HTTPS>::accept() with SSL validation support added
    void accept() override {
      auto connection = create_connection(*io_service, context);

      acceptor->async_accept(connection->socket->lowest_layer(), [this, connection](const SimpleWeb::error_code &ec) {
        auto lock = connection->handler_runner->continue_lock();
        if (!lock) {
          return;
        }

        if (ec != SimpleWeb::error::operation_aborted) {
          this->accept();
        }

        auto session = std::make_shared<Session>(config.max_request_streambuf_size, connection);

        if (!ec) {
          boost::asio::ip::tcp::no_delay option(true);
          SimpleWeb::error_code ec;
          session->connection->socket->lowest_layer().set_option(option, ec);

          session->connection->set_timeout(config.timeout_request);
          session->connection->socket->async_handshake(boost::asio::ssl::stream_base::server, [this, session](const SimpleWeb::error_code &ec) {
            session->connection->cancel_timeout();
            auto lock = session->connection->handler_runner->continue_lock();
            if (!lock) {
              return;
            }
            if (!ec) {
              if (verify && !verify(session->request, session->connection->socket->native_handle())) {
                this->write(session, on_verify_failed);
              } else {
                this->read(session);
              }
            } else if (this->on_error) {
              this->on_error(session->request, ec);
            }
          });
        } else if (this->on_error) {
          this->on_error(session->request, ec);
        }
      });
    }
  };

  using https_server_t = SunshineHTTPSServer;
  using http_server_t = SimpleWeb::Server<SimpleWeb::HTTP>;

  struct conf_intern_t {
    std::string servercert;
    std::string pkey;
  } conf_intern;

  // uniqueID, session
  std::unordered_map<std::string, pair_session_t> map_id_sess;
  client_t client_root;
  std::atomic<uint32_t> session_id_counter;
  std::unique_ptr<attended_pairing::pairing_service> attended_pairing_service;
  std::unique_ptr<attended_pairing::http::router> attended_pairing_router;

  class attended_legacy_adapter final:
      public attended_pairing::legacy_pairing_adapter {
  public:
    attended_legacy_adapter(std::string unique_id, std::string request_id):
        unique_id_(std::move(unique_id)),
        request_id_(std::move(request_id)) {
    }

    void approve(
      std::span<const std::uint8_t> ascii_pin,
      std::uint64_t generation
    ) override {
      const auto found = map_id_sess.find(unique_id_);
      if (found == map_id_sess.end()) {
        throw std::runtime_error("held pair session unavailable");
      }
      auto &session = found->second;
      if (session.last_phase != PAIR_PHASE::NONE
          || session.async_insert_pin.salt.size() < 32) {
        throw std::runtime_error("held pair session is not usable");
      }
      session.attended_generation = generation;
      pt::ptree tree;
      getservercert(
        session,
        tree,
        std::string(ascii_pin.begin(), ascii_pin.end())
      );
      std::ostringstream data;
      pt::write_xml(data, tree);
      auto &async_response = session.async_insert_pin.response;
      if (async_response.has_left() && async_response.left()) {
        async_response.left()->write(data.str());
      } else {
        throw std::runtime_error("held pair response unavailable");
      }
      async_response = std::decay_t<decltype(async_response.left())>();
    }

    void cancel(std::uint64_t) noexcept override {
      const auto found = map_id_sess.find(unique_id_);
      if (found == map_id_sess.end()) return;
      auto &async_response = found->second.async_insert_pin.response;
      if (async_response.has_left() && async_response.left()) {
        pt::ptree tree;
        tree.put("root.paired", 0);
        tree.put("root.<xmlattr>.status_code", 409);
        tree.put("root.<xmlattr>.status_message", "Pairing request ended");
        std::ostringstream data;
        pt::write_xml(data, tree);
        async_response.left()->write(data.str());
      }
      map_id_sess.erase(found);
    }

    [[nodiscard]] const std::string &request_id() const noexcept {
      return request_id_;
    }

  private:
    std::string unique_id_;
    std::string request_id_;
  };

  std::unordered_map<std::string, std::unique_ptr<attended_legacy_adapter>>
    attended_legacy_adapters;

  using resp_https_t = std::shared_ptr<typename SimpleWeb::ServerBase<SunshineHTTPS>::Response>;
  using req_https_t = std::shared_ptr<typename SimpleWeb::ServerBase<SunshineHTTPS>::Request>;
  using resp_http_t = std::shared_ptr<typename SimpleWeb::ServerBase<SimpleWeb::HTTP>::Response>;
  using req_http_t = std::shared_ptr<typename SimpleWeb::ServerBase<SimpleWeb::HTTP>::Request>;

  namespace {
    attended_pairing::source_identity attended_source(
      const boost::asio::ip::address &address);
    attended_pairing::bytes host_certificate_der(std::string_view pem);
  }

  enum class op_e {
    ADD,  ///< Add certificate
    REMOVE  ///< Remove certificate
  };

  std::string get_arg(const args_t &args, const char *name, const char *default_value) {
    auto it = args.find(name);
    if (it == std::end(args)) {
      if (default_value != nullptr) {
        return std::string(default_value);
      }

      throw std::out_of_range(name);
    }
    return it->second;
  }

  // Helper function to extract command entries from a JSON object.
  cmd_list_t extract_command_entries(const nlohmann::json& j, const std::string& key) {
    cmd_list_t commands;

    // Check if the key exists in the JSON.
    if (j.contains(key)) {
      // Ensure that the value for the key is an array.
      try {
        for (const auto& item : j.at(key)) {
          try {
            // Extract "cmd" and "elevated" fields from the JSON object.
            std::string cmd = item.at("cmd").get<std::string>();
            bool elevated = util::get_non_string_json_value<bool>(item, "elevated", false);

            // Add the command entry to the list.
            commands.push_back({cmd, elevated});
          } catch (const std::exception& e) {
            BOOST_LOG(warning) << "Error parsing command entry: " << e.what();
          }
        }
      } catch (const std::exception &e) {
        BOOST_LOG(warning) << "Error retrieving key \"" << key << "\": " << e.what();
      }
    } else {
      BOOST_LOG(debug) << "Key \"" << key << "\" not found in the JSON.";
    }

    return commands;
  }

  void save_state() {
    nlohmann::json root = nlohmann::json::object();
    // If the state file exists, try to read it.
    if (fs::exists(config::nvhttp.file_state)) {
      try {
        std::ifstream in(config::nvhttp.file_state);
        in >> root;
      } catch (std::exception &e) {
        BOOST_LOG(error) << "Couldn't read "sv << config::nvhttp.file_state << ": "sv << e.what();
        return;
      }
    }

    // Erase any previous "root" key.
    root.erase("root");

    // Create a new "root" object and set the unique id.
    root["root"] = nlohmann::json::object();
    root["root"]["uniqueid"] = http::unique_id;

    client_t &client = client_root;
    nlohmann::json named_cert_nodes = nlohmann::json::array();

    std::unordered_set<std::string> unique_certs;
    std::unordered_map<std::string, int> name_counts;

    for (auto &named_cert_p : client.named_devices) {
      // Only add each unique certificate once.
      if (unique_certs.insert(named_cert_p->cert).second) {
        nlohmann::json named_cert_node = nlohmann::json::object();
        std::string base_name = named_cert_p->name;
        // Remove any pending id suffix (e.g., " (2)") if present.
        size_t pos = base_name.find(" (");
        if (pos != std::string::npos) {
          base_name = base_name.substr(0, pos);
        }
        int count = name_counts[base_name]++;
        std::string final_name = base_name;
        if (count > 0) {
          final_name += " (" + std::to_string(count + 1) + ")";
        }
        named_cert_node["name"] = final_name;
        named_cert_node["cert"] = named_cert_p->cert;
        named_cert_node["uuid"] = named_cert_p->uuid;
        named_cert_node["display_mode"] = named_cert_p->display_mode;
        named_cert_node["perm"] = static_cast<uint32_t>(named_cert_p->perm);
        named_cert_node["enable_legacy_ordering"] = named_cert_p->enable_legacy_ordering;
        named_cert_node["allow_client_commands"] = named_cert_p->allow_client_commands;
        named_cert_node["always_use_virtual_display"] = named_cert_p->always_use_virtual_display;

        // Add "do" commands if available.
        if (!named_cert_p->do_cmds.empty()) {
          nlohmann::json do_cmds_node = nlohmann::json::array();
          for (const auto &cmd : named_cert_p->do_cmds) {
            do_cmds_node.push_back(crypto::command_entry_t::serialize(cmd));
          }
          named_cert_node["do"] = do_cmds_node;
        }

        // Add "undo" commands if available.
        if (!named_cert_p->undo_cmds.empty()) {
          nlohmann::json undo_cmds_node = nlohmann::json::array();
          for (const auto &cmd : named_cert_p->undo_cmds) {
            undo_cmds_node.push_back(crypto::command_entry_t::serialize(cmd));
          }
          named_cert_node["undo"] = undo_cmds_node;
        }

        named_cert_nodes.push_back(named_cert_node);
      }
    }

    root["root"]["named_devices"] = named_cert_nodes;

    try {
      std::ofstream out(config::nvhttp.file_state);
      out << root.dump(4);  // Pretty-print with an indent of 4 spaces.
    } catch (std::exception &e) {
      BOOST_LOG(error) << "Couldn't write "sv << config::nvhttp.file_state << ": "sv << e.what();
      return;
    }
  }

  void load_state() {
    if (!fs::exists(config::nvhttp.file_state)) {
      BOOST_LOG(info) << "File "sv << config::nvhttp.file_state << " doesn't exist"sv;
      http::unique_id = uuid_util::uuid_t::generate().string();
      return;
    }

    nlohmann::json tree;
    try {
      std::ifstream in(config::nvhttp.file_state);
      in >> tree;
    } catch (std::exception &e) {
      BOOST_LOG(error) << "Couldn't read "sv << config::nvhttp.file_state << ": "sv << e.what();
      return;
    }

    // Check that the file contains a "root.uniqueid" value.
    if (!tree.contains("root") || !tree["root"].contains("uniqueid")) {
      http::uuid = uuid_util::uuid_t::generate();
      http::unique_id = http::uuid.string();
      return;
    }

    std::string uid = tree["root"]["uniqueid"];
    http::uuid = uuid_util::uuid_t::parse(uid);
    http::unique_id = uid;

    nlohmann::json root = tree["root"];
    client_t client;  // Local client to load into

    // Import from the old format if available.
    if (root.contains("devices")) {
      for (auto &device_node : root["devices"]) {
        // For each device, if there is a "certs" array, add a named certificate.
        if (device_node.contains("certs")) {
          for (auto &el : device_node["certs"]) {
            auto named_cert_p = std::make_shared<crypto::named_cert_t>();
            named_cert_p->name = "";
            named_cert_p->cert = el.get<std::string>();
            named_cert_p->uuid = uuid_util::uuid_t::generate().string();
            named_cert_p->display_mode = "";
            named_cert_p->perm = PERM::_all;
            named_cert_p->enable_legacy_ordering = true;
            named_cert_p->allow_client_commands = true;
            named_cert_p->always_use_virtual_display = false;
            client.named_devices.emplace_back(named_cert_p);
          }
        }
      }
    }

    // Import from the new format.
    if (root.contains("named_devices")) {
      for (auto &el : root["named_devices"]) {
        auto named_cert_p = std::make_shared<crypto::named_cert_t>();
        named_cert_p->name = el.value("name", "");
        named_cert_p->cert = el.value("cert", "");
        named_cert_p->uuid = el.value("uuid", "");
        named_cert_p->display_mode = el.value("display_mode", "");
        named_cert_p->perm = (PERM)(util::get_non_string_json_value<uint32_t>(el, "perm", (uint32_t)PERM::_all)) & PERM::_all;
        named_cert_p->enable_legacy_ordering = el.value("enable_legacy_ordering", true);
        named_cert_p->allow_client_commands = el.value("allow_client_commands", true);
        named_cert_p->always_use_virtual_display = el.value("always_use_virtual_display", false);
        // Load command entries for "do" and "undo" keys.
        named_cert_p->do_cmds = extract_command_entries(el, "do");
        named_cert_p->undo_cmds = extract_command_entries(el, "undo");
        client.named_devices.emplace_back(named_cert_p);
      }
    }

    // Clear any existing certificate chain and add the imported certificates.
    cert_chain.clear();
    for (auto &named_cert : client.named_devices) {
      cert_chain.add(named_cert);
    }

    client_root = client;
  }

  void add_authorized_client(const p_named_cert_t& named_cert_p) {
    client_t &client = client_root;
    client.named_devices.push_back(named_cert_p);

#if defined SUNSHINE_TESTS
    // Unit pairing tests do not own a tray window or a writable state path.
    // The production path below remains unchanged.
    return;
#endif

#if defined SUNSHINE_TRAY && SUNSHINE_TRAY >= 1
    system_tray::update_tray_paired(named_cert_p->name);
#endif

    if (!config::sunshine.flags[config::flag::FRESH_STATE]) {
      save_state();
      load_state();
    }
  }

  std::shared_ptr<rtsp_stream::launch_session_t> make_launch_session(bool host_audio, bool input_only, const args_t &args, const crypto::named_cert_t* named_cert_p) {
    auto launch_session = std::make_shared<rtsp_stream::launch_session_t>();

    launch_session->id = ++session_id_counter;

    // If launched from client
    if (named_cert_p->uuid != http::unique_id) {
      auto rikey = util::from_hex_vec(get_arg(args, "rikey"), true);
      std::copy(rikey.cbegin(), rikey.cend(), std::back_inserter(launch_session->gcm_key));

      launch_session->host_audio = host_audio;

      // Encrypted RTSP is enabled with client reported corever >= 1
      auto corever = util::from_view(get_arg(args, "corever", "0"));
      if (corever >= 1) {
        launch_session->rtsp_cipher = crypto::cipher::gcm_t {
          launch_session->gcm_key, false
        };
        launch_session->rtsp_iv_counter = 0;
      }
      launch_session->rtsp_url_scheme = launch_session->rtsp_cipher ? "rtspenc://"s : "rtsp://"s;

      // Generate the unique identifiers for this connection that we will send later during RTSP handshake
      unsigned char raw_payload[8];
      RAND_bytes(raw_payload, sizeof(raw_payload));
      launch_session->av_ping_payload = util::hex_vec(raw_payload);
      RAND_bytes((unsigned char *) &launch_session->control_connect_data, sizeof(launch_session->control_connect_data));

      launch_session->iv.resize(16);
      uint32_t prepend_iv = util::endian::big<uint32_t>(util::from_view(get_arg(args, "rikeyid")));
      auto prepend_iv_p = (uint8_t *) &prepend_iv;
      std::copy(prepend_iv_p, prepend_iv_p + sizeof(prepend_iv), std::begin(launch_session->iv));
    }

    std::stringstream mode;
    if (named_cert_p->display_mode.empty()) {
      auto mode_str = get_arg(args, "mode", config::video.fallback_mode.c_str());
      mode = std::stringstream(mode_str);
      BOOST_LOG(info) << "Display mode for client ["sv << named_cert_p->name <<"] requested to ["sv << mode_str << ']';
    } else {
      mode = std::stringstream(named_cert_p->display_mode);
      BOOST_LOG(info) << "Display mode for client ["sv << named_cert_p->name <<"] overriden to ["sv << named_cert_p->display_mode << ']';
    }

    // Split mode by the char "x", to populate width/height/fps
    int x = 0;
    std::string segment;
    while (std::getline(mode, segment, 'x')) {
      if (x == 0) {
        launch_session->width = atoi(segment.c_str());
      }
      if (x == 1) {
        launch_session->height = atoi(segment.c_str());
      }
      if (x == 2) {
        auto fps = atof(segment.c_str());
        if (fps < 1000) {
          fps *= 1000;
        };
        launch_session->fps = (int)fps;
        break;
      }
      x++;
    }

    // Parsing have failed or missing components
    if (x != 2) {
      launch_session->width = 1920;
      launch_session->height = 1080;
      launch_session->fps = 60000; // 60fps * 1000 denominator
    }

    launch_session->device_name = named_cert_p->name.empty() ? "ApolloDisplay"s : named_cert_p->name;
    launch_session->unique_id = named_cert_p->uuid;
    launch_session->perm = named_cert_p->perm;
    launch_session->enable_sops = util::from_view(get_arg(args, "sops", "0"));
    launch_session->surround_info = util::from_view(get_arg(args, "surroundAudioInfo", "196610"));
    launch_session->surround_params = (get_arg(args, "surroundParams", ""));
    launch_session->gcmap = util::from_view(get_arg(args, "gcmap", "0"));
    launch_session->enable_hdr = util::from_view(get_arg(args, "hdrMode", "0"));
    launch_session->virtual_display = util::from_view(get_arg(args, "virtualDisplay", "0")) || named_cert_p->always_use_virtual_display;
    launch_session->scale_factor = util::from_view(get_arg(args, "scaleFactor", "100"));

    launch_session->client_do_cmds = named_cert_p->do_cmds;
    launch_session->client_undo_cmds = named_cert_p->undo_cmds;

    launch_session->input_only = input_only;

    return launch_session;
  }

  void remove_session(const pair_session_t &sess) {
    map_id_sess.erase(sess.client.uniqueID);
  }

  void fail_pair(pair_session_t &sess, pt::ptree &tree, const std::string status_msg) {
    tree.put("root.paired", 0);
    tree.put("root.<xmlattr>.status_code", 400);
    tree.put("root.<xmlattr>.status_message", status_msg);
    remove_session(sess);  // Security measure, delete the session when something went wrong and force a re-pair
    BOOST_LOG(warning) << "Pair attempt failed due to " << status_msg;
  }

  void getservercert(pair_session_t &sess, pt::ptree &tree, const std::string &pin) {
    if (sess.last_phase != PAIR_PHASE::NONE) {
      fail_pair(sess, tree, "Out of order call to getservercert");
      return;
    }
    sess.last_phase = PAIR_PHASE::GETSERVERCERT;

    if (sess.async_insert_pin.salt.size() < 32) {
      fail_pair(sess, tree, "Salt too short");
      return;
    }

    std::string_view salt_view {sess.async_insert_pin.salt.data(), 32};

    auto salt = util::from_hex<std::array<uint8_t, 16>>(salt_view, true);

    auto key = crypto::gen_aes_key(salt, pin);
    sess.cipher_key = std::make_unique<crypto::aes_t>(key);

    tree.put("root.paired", 1);
    tree.put("root.plaincert", util::hex_vec(conf_intern.servercert, true));
    tree.put("root.<xmlattr>.status_code", 200);
  }

  void clientchallenge(pair_session_t &sess, pt::ptree &tree, const std::string &challenge) {
    if (sess.last_phase != PAIR_PHASE::GETSERVERCERT) {
      fail_pair(sess, tree, "Out of order call to clientchallenge");
      return;
    }
    sess.last_phase = PAIR_PHASE::CLIENTCHALLENGE;

    if (!sess.cipher_key) {
      fail_pair(sess, tree, "Cipher key not set");
      return;
    }
    crypto::cipher::ecb_t cipher(*sess.cipher_key, false);

    std::vector<uint8_t> decrypted;
    cipher.decrypt(challenge, decrypted);

    auto x509 = crypto::x509(conf_intern.servercert);
    auto sign = crypto::signature(x509);
    auto serversecret = crypto::rand(16);

    decrypted.insert(std::end(decrypted), std::begin(sign), std::end(sign));
    decrypted.insert(std::end(decrypted), std::begin(serversecret), std::end(serversecret));

    auto hash = crypto::hash({(char *) decrypted.data(), decrypted.size()});
    auto serverchallenge = crypto::rand(16);

    std::string plaintext;
    plaintext.reserve(hash.size() + serverchallenge.size());

    plaintext.insert(std::end(plaintext), std::begin(hash), std::end(hash));
    plaintext.insert(std::end(plaintext), std::begin(serverchallenge), std::end(serverchallenge));

    std::vector<uint8_t> encrypted;
    cipher.encrypt(plaintext, encrypted);

    sess.serversecret = std::move(serversecret);
    sess.serverchallenge = std::move(serverchallenge);

    tree.put("root.paired", 1);
    tree.put("root.challengeresponse", util::hex_vec(encrypted, true));
    tree.put("root.<xmlattr>.status_code", 200);
  }

  void serverchallengeresp(pair_session_t &sess, pt::ptree &tree, const std::string &encrypted_response) {
    if (sess.last_phase != PAIR_PHASE::CLIENTCHALLENGE) {
      fail_pair(sess, tree, "Out of order call to serverchallengeresp");
      return;
    }
    sess.last_phase = PAIR_PHASE::SERVERCHALLENGERESP;

    if (!sess.cipher_key || sess.serversecret.empty()) {
      fail_pair(sess, tree, "Cipher key or serversecret not set");
      return;
    }

    std::vector<uint8_t> decrypted;
    crypto::cipher::ecb_t cipher(*sess.cipher_key, false);

    cipher.decrypt(encrypted_response, decrypted);

    sess.clienthash = std::move(decrypted);

    auto serversecret = sess.serversecret;
    auto sign = crypto::sign256(crypto::pkey(conf_intern.pkey), serversecret);

    serversecret.insert(std::end(serversecret), std::begin(sign), std::end(sign));

    tree.put("root.pairingsecret", util::hex_vec(serversecret, true));
    tree.put("root.paired", 1);
    tree.put("root.<xmlattr>.status_code", 200);
  }

  void clientpairingsecret(pair_session_t &sess, pt::ptree &tree, const std::string &client_pairing_secret) {
    if (sess.last_phase != PAIR_PHASE::SERVERCHALLENGERESP) {
      fail_pair(sess, tree, "Out of order call to clientpairingsecret");
      return;
    }
    sess.last_phase = PAIR_PHASE::CLIENTPAIRINGSECRET;

    auto &client = sess.client;

    if (client_pairing_secret.size() <= 16) {
      fail_pair(sess, tree, "Client pairing secret too short");
      return;
    }

    std::string_view secret {client_pairing_secret.data(), 16};
    std::string_view sign {client_pairing_secret.data() + secret.size(), client_pairing_secret.size() - secret.size()};

    auto x509 = crypto::x509(client.cert);
    if (!x509) {
      fail_pair(sess, tree, "Invalid client certificate");
      return;
    }
    auto x509_sign = crypto::signature(x509);

    std::string data;
    data.reserve(sess.serverchallenge.size() + x509_sign.size() + secret.size());

    data.insert(std::end(data), std::begin(sess.serverchallenge), std::end(sess.serverchallenge));
    data.insert(std::end(data), std::begin(x509_sign), std::end(x509_sign));
    data.insert(std::end(data), std::begin(secret), std::end(secret));

    auto hash = crypto::hash(data);

    // if hash not correct, probably MITM
    bool same_hash = hash.size() == sess.clienthash.size() && std::equal(hash.begin(), hash.end(), sess.clienthash.begin());
    auto verify = crypto::verify256(crypto::x509(client.cert), secret, sign);
    if (same_hash && verify) {
      tree.put("root.paired", 1);

      auto named_cert_p = std::make_shared<crypto::named_cert_t>();
      named_cert_p->name = client.name;
      for (char& c : named_cert_p->name) {
        if (c == '(') c = '[';
        else if (c == ')') c = ']';
      }
      named_cert_p->cert = std::move(client.cert);
      named_cert_p->uuid = uuid_util::uuid_t::generate().string();
      const auto has_attended_access =
        !sess.attended_request_id.empty() && attended_pairing_service;
      const auto attended_access = has_attended_access
        ? attended_pairing_service->access_mode_for_pairing(
            sess.attended_request_id)
        : attended_pairing::access_mode::operate;
      if (has_attended_access) {
        named_cert_p->perm =
          attended_access == attended_pairing::access_mode::observe
            ? PERM::_default
            : PERM::_all;
      } else {
        // Preserve the legacy PIN pairing ABI.
        named_cert_p->perm = client_root.named_devices.empty()
          ? PERM::_all
          : PERM::_default;
      }

      named_cert_p->enable_legacy_ordering = true;
      named_cert_p->allow_client_commands =
        !has_attended_access ||
        attended_access == attended_pairing::access_mode::operate;
      named_cert_p->always_use_virtual_display = false;

      add_authorized_client(named_cert_p);
    } else {
      tree.put("root.paired", 0);
      BOOST_LOG(warning) << "Pair attempt failed due to same_hash: " << same_hash << ", verify: " << verify;
    }

    remove_session(sess);
    tree.put("root.<xmlattr>.status_code", 200);
  }

  template<class T>
  struct tunnel;

  template<>
  struct tunnel<SunshineHTTPS> {
    static auto constexpr to_string = "HTTPS"sv;
  };

  template<>
  struct tunnel<SimpleWeb::HTTP> {
    static auto constexpr to_string = "NONE"sv;
  };

  inline crypto::named_cert_t* get_verified_cert(req_https_t request) {
    return (crypto::named_cert_t*)request->userp.get();
  }

  std::string_view ligase_client_access_mode(
    const crypto::named_cert_t &client
  ) {
    return client.perm == PERM::_all && client.allow_client_commands
      ? "operate"sv
      : "observe"sv;
  }

  bool ligase_client_can_read_library(const crypto::named_cert_t &client) {
    return !!(client.perm & PERM::list);
  }

  bool ligase_client_can_mutate(const crypto::named_cert_t &client) {
    return !!(client.perm & PERM::launch);
  }

  template <class T>
  void print_req(std::shared_ptr<typename SimpleWeb::ServerBase<T>::Request> request) {
    BOOST_LOG(debug) << "TUNNEL :: "sv << tunnel<T>::to_string;

    BOOST_LOG(debug) << "METHOD :: "sv << request->method;
    BOOST_LOG(debug) << "DESTINATION :: "sv << request->path;

    for (auto &[name, val] : request->header) {
      BOOST_LOG(debug) << name << " -- " << val;
    }

    BOOST_LOG(debug) << " [--] "sv;

    for (auto &[name, val] : request->parse_query_string()) {
      BOOST_LOG(debug) << name << " -- " << val;
    }

    BOOST_LOG(debug) << " [--] "sv;
  }

  template<class T>
  void not_found(std::shared_ptr<typename SimpleWeb::ServerBase<T>::Response> response, std::shared_ptr<typename SimpleWeb::ServerBase<T>::Request> request) {
    print_req<T>(request);

    pt::ptree tree;
    tree.put("root.<xmlattr>.status_code", 404);

    std::ostringstream data;

    pt::write_xml(data, tree);
    response->write(SimpleWeb::StatusCode::client_error_not_found, data.str());
    response->close_connection_after_response = true;
  }

  template <class T>
  void pair(std::shared_ptr<typename SimpleWeb::ServerBase<T>::Response> response, std::shared_ptr<typename SimpleWeb::ServerBase<T>::Request> request) {
    print_req<T>(request);

    pt::ptree tree;

    auto fg = util::fail_guard([&]() {
      std::ostringstream data;

      pt::write_xml(data, tree);
      response->write(data.str());
      response->close_connection_after_response = true;
    });

    if (!config::sunshine.enable_pairing) {
      tree.put("root.<xmlattr>.status_code", 403);
      tree.put("root.<xmlattr>.status_message", "Pairing is disabled for this instance");

      return;
    }

    auto args = request->parse_query_string();
    if (args.find("uniqueid"s) == std::end(args)) {
      tree.put("root.<xmlattr>.status_code", 400);
      tree.put("root.<xmlattr>.status_message", "Missing uniqueid parameter");

      return;
    }

    auto uniqID {get_arg(args, "uniqueid")};

    args_t::const_iterator it;
    if (it = args.find("phrase"); it != std::end(args)) {
      if (it->second == "getservercert"sv) {
        pair_session_t sess;

        auto deviceName { get_arg(args, "devicename") };

        if (deviceName == "roth"sv) {
          deviceName = "Legacy Moonlight Client";
        }

        sess.client.uniqueID = std::move(uniqID);
        sess.client.name = std::move(deviceName);
        sess.client.cert = util::from_hex_vec(get_arg(args, "clientcert"), true);

        BOOST_LOG(debug) << sess.client.cert;
        auto ptr = map_id_sess.emplace(sess.client.uniqueID, std::move(sess)).first;

        ptr->second.async_insert_pin.salt = std::move(get_arg(args, "salt"));

        const auto attended_id = args.find("ligasepairingrequestid");
        if (attended_id != args.end()) {
          if (args.count("ligasepairingrequestid") != 1) {
            tree.put("root.<xmlattr>.status_code", 400);
            tree.put("root.<xmlattr>.status_message", "Invalid pairing request");
            map_id_sess.erase(ptr);
            return;
          }
          if constexpr (!std::is_same_v<T, SimpleWeb::HTTP>) {
            tree.put("root.<xmlattr>.status_code", 400);
            tree.put("root.<xmlattr>.status_message", "Invalid pairing request");
            map_id_sess.erase(ptr);
            return;
          } else {
            if (!attended_pairing_service) {
              tree.put("root.<xmlattr>.status_code", 503);
              tree.put("root.<xmlattr>.status_message", "Pairing unavailable");
              map_id_sess.erase(ptr);
              return;
            }
            try {
              const auto request_id = attended_id->second;
              ptr->second.attended_request_id = request_id;
              auto adapter = std::make_unique<attended_legacy_adapter>(
                ptr->second.client.uniqueID, request_id);
              auto *adapter_pointer = adapter.get();
              attended_legacy_adapters[ptr->second.client.uniqueID] =
                std::move(adapter);
              const auto client_der =
                host_certificate_der(ptr->second.client.cert);
              attended_pairing_service->bind_held_getservercert(
                request_id,
                attended_pairing::sha256(client_der),
                attended_source(request->remote_endpoint().address()),
                *adapter_pointer,
                attended_pairing::pairing_service::steady_clock::now()
              );
              ptr->second.async_insert_pin.response = std::move(response);
              fg.disable();
              return;
            } catch (const std::exception &) {
              attended_legacy_adapters.erase(ptr->second.client.uniqueID);
              map_id_sess.erase(ptr);
              tree.put("root.<xmlattr>.status_code", 409);
              tree.put("root.<xmlattr>.status_message", "Pairing request rejected");
              return;
            }
          }
        }

        auto it = args.find("otpauth");
        if (it != std::end(args)) {
          if (one_time_pin.empty() || (std::chrono::steady_clock::now() - otp_creation_time > OTP_EXPIRE_DURATION)) {
            one_time_pin.clear();
            otp_passphrase.clear();
            otp_device_name.clear();
            tree.put("root.<xmlattr>.status_code", 503);
            tree.put("root.<xmlattr>.status_message", "OTP auth not available.");
          } else {
            auto hash = util::hex(crypto::hash(one_time_pin + ptr->second.async_insert_pin.salt + otp_passphrase), true);

            if (hash.to_string_view() == it->second) {

              if (!otp_device_name.empty()) {
                ptr->second.client.name = std::move(otp_device_name);
              }

              getservercert(ptr->second, tree, one_time_pin);

              one_time_pin.clear();
              otp_passphrase.clear();
              otp_device_name.clear();
              return;
            }
          }

          // Always return positive, attackers will fail in the next steps.
          getservercert(ptr->second, tree, crypto::rand(16));
          return;
        }

        if (config::sunshine.flags[config::flag::PIN_STDIN]) {
          std::string pin;

          std::cout << "Please insert pin: "sv;
          std::getline(std::cin, pin);

          getservercert(ptr->second, tree, pin);
        } else {
#if defined SUNSHINE_TRAY && SUNSHINE_TRAY >= 1
          system_tray::update_tray_require_pin();
#endif
          ptr->second.async_insert_pin.response = std::move(response);

          fg.disable();
          return;
        }
      } else if (it->second == "pairchallenge"sv) {
        tree.put("root.paired", 1);
        tree.put("root.<xmlattr>.status_code", 200);
        return;
      }
    }

    auto sess_it = map_id_sess.find(uniqID);
    if (sess_it == std::end(map_id_sess)) {
      tree.put("root.<xmlattr>.status_code", 400);
      tree.put("root.<xmlattr>.status_message", "Invalid uniqueid");

      return;
    }

    if (args.contains("ligasepairingrequestid")) {
      tree.put("root.<xmlattr>.status_code", 400);
      tree.put("root.<xmlattr>.status_message", "Invalid pairing request");
      if (!sess_it->second.attended_request_id.empty()
          && attended_pairing_service) {
        const auto attended_unique_id = sess_it->second.client.uniqueID;
        attended_pairing_service->fail_pairing(
          sess_it->second.attended_request_id,
          sess_it->second.attended_generation,
          "legacyPairingFailed",
          attended_pairing::pairing_service::steady_clock::now()
        );
        attended_legacy_adapters.erase(attended_unique_id);
      }
      return;
    }

    const auto attended_request_id = sess_it->second.attended_request_id;
    const auto attended_generation = sess_it->second.attended_generation;
    const auto attended_unique_id = sess_it->second.client.uniqueID;
    auto fail_attended = [&]() {
      if (attended_request_id.empty() || !attended_pairing_service) return;
      attended_pairing_service->fail_pairing(
        attended_request_id,
        attended_generation,
        "legacyPairingFailed",
        attended_pairing::pairing_service::steady_clock::now()
      );
      attended_legacy_adapters.erase(attended_unique_id);
    };

    if (it = args.find("clientchallenge"); it != std::end(args)) {
      auto challenge = util::from_hex_vec(it->second, true);
      clientchallenge(sess_it->second, tree, challenge);
      if (tree.get<int>("root.paired", 0) != 1) fail_attended();
    } else if (it = args.find("serverchallengeresp"); it != std::end(args)) {
      auto encrypted_response = util::from_hex_vec(it->second, true);
      serverchallengeresp(sess_it->second, tree, encrypted_response);
      if (tree.get<int>("root.paired", 0) != 1) fail_attended();
    } else if (it = args.find("clientpairingsecret"); it != std::end(args)) {
      auto pairingsecret = util::from_hex_vec(it->second, true);
      clientpairingsecret(sess_it->second, tree, pairingsecret);
      if (!attended_request_id.empty()
          && tree.get<int>("root.paired", 0) == 1) {
        try {
          attended_pairing_service->commit_paired(
            attended_request_id,
            attended_generation
          );
          attended_legacy_adapters.erase(attended_unique_id);
        } catch (const std::exception &) {
          tree.put("root.paired", 0);
          tree.put("root.<xmlattr>.status_code", 409);
          tree.put("root.<xmlattr>.status_message", "Pairing request ended");
        }
      } else if (tree.get<int>("root.paired", 0) != 1) {
        fail_attended();
      }
    } else {
      tree.put("root.<xmlattr>.status_code", 404);
      tree.put("root.<xmlattr>.status_message", "Invalid pairing request");
      fail_attended();
    }
  }

  bool pin(std::string pin, std::string name) {
    pt::ptree tree;
    if (map_id_sess.empty()) {
      return false;
    }

    // ensure pin is 4 digits
    if (pin.size() != 4) {
      tree.put("root.paired", 0);
      tree.put("root.<xmlattr>.status_code", 400);
      tree.put(
        "root.<xmlattr>.status_message",
        std::format("Pin must be 4 digits, {} provided", pin.size())
      );
      return false;
    }

    // ensure all pin characters are numeric
    if (!std::all_of(pin.begin(), pin.end(), ::isdigit)) {
      tree.put("root.paired", 0);
      tree.put("root.<xmlattr>.status_code", 400);
      tree.put("root.<xmlattr>.status_message", "Pin must be numeric");
      return false;
    }

    auto &sess = std::begin(map_id_sess)->second;
    getservercert(sess, tree, pin);

    if (!name.empty()) {
      sess.client.name = name;
    }

    // response to the request for pin
    std::ostringstream data;
    pt::write_xml(data, tree);

    auto &async_response = sess.async_insert_pin.response;
    if (async_response.has_left() && async_response.left()) {
      async_response.left()->write(data.str());
    } else if (async_response.has_right() && async_response.right()) {
      async_response.right()->write(data.str());
    } else {
      return false;
    }

    // reset async_response
    async_response = std::decay_t<decltype(async_response.left())>();
    // response to the current request
    return true;
  }

  template<class T>
  void serverinfo(std::shared_ptr<typename SimpleWeb::ServerBase<T>::Response> response, std::shared_ptr<typename SimpleWeb::ServerBase<T>::Request> request) {
    print_req<T>(request);

    int pair_status = 0;
    if constexpr (std::is_same_v<SunshineHTTPS, T>) {
      auto args = request->parse_query_string();
      auto clientID = args.find("uniqueid"s);

      if (clientID != std::end(args)) {
        pair_status = 1;
      }
    }

    auto local_endpoint = request->local_endpoint();

    pt::ptree tree;

    tree.put("root.<xmlattr>.status_code", 200);
    tree.put("root.hostname", config::nvhttp.sunshine_name);

    tree.put("root.appversion", VERSION);
    tree.put("root.GfeVersion", GFE_VERSION);
    tree.put("root.uniqueid", http::unique_id);
    tree.put("root.HttpsPort", net::map_port(PORT_HTTPS));
    tree.put("root.ExternalPort", net::map_port(PORT_HTTP));
    tree.put("root.MaxLumaPixelsHEVC", video::active_hevc_mode > 1 ? "1869449984" : "0");
    tree.put("root.LigaseSyncVersion", 1);
    tree.put("root.LigaseSyncPath", "/ligase/v1/sync");
    tree.put("root.LigaseHdrEncodingSupported", video::active_hevc_mode == 3 ? 1 : 0);
    tree.put("root.LigaseAttendedPairingVersion", 1);
    tree.put(
      "root.LigaseAttendedPairingPath",
      "/ligase/v1/pairing/requests");

    // Only include the MAC address for requests sent from paired clients over HTTPS.
    // For HTTP requests, use a placeholder MAC address that Moonlight knows to ignore.
    if constexpr (std::is_same_v<SunshineHTTPS, T>) {
      tree.put("root.mac", platf::get_mac_address(net::addr_to_normalized_string(local_endpoint.address())));

      auto named_cert_p = get_verified_cert(request);
      if (!!(named_cert_p->perm & PERM::server_cmd)) {
        pt::ptree& root_node = tree.get_child("root");

        if (config::sunshine.server_cmds.size() > 0) {
          // Broadcast server_cmds
          for (const auto& cmd : config::sunshine.server_cmds) {
            pt::ptree cmd_node;
            cmd_node.put_value(cmd.cmd_name);
            root_node.push_back(std::make_pair("ServerCommand", cmd_node));
          }
        }
      } else {
        BOOST_LOG(debug) << "Permission Get ServerCommand denied for [" << named_cert_p->name << "] (" << (uint32_t)named_cert_p->perm << ")";
      }

      tree.put("root.Permission", std::to_string((uint32_t)named_cert_p->perm));
      tree.put(
        "root.LigaseClientAccessMode",
        ligase_client_access_mode(*named_cert_p));

    #ifdef _WIN32
      tree.put("root.VirtualDisplayCapable", true);
      if (!!(named_cert_p->perm & PERM::_all_actions)) {
        tree.put("root.VirtualDisplayDriverReady", proc::vDisplayDriverStatus == VDISPLAY::DRIVER_STATUS::OK);
      } else {
        tree.put("root.VirtualDisplayDriverReady", true);
      }
    #endif
    } else {
      tree.put("root.mac", "00:00:00:00:00:00");
      tree.put("root.Permission", "0");
    }

    // Moonlight clients track LAN IPv6 addresses separately from LocalIP which is expected to
    // always be an IPv4 address. If we return that same IPv6 address here, it will clobber the
    // stored LAN IPv4 address. To avoid this, we need to return an IPv4 address in this field
    // when we get a request over IPv6.
    //
    // HACK: We should return the IPv4 address of local interface here, but we don't currently
    // have that implemented. For now, we will emulate the behavior of GFE+GS-IPv6-Forwarder,
    // which returns 127.0.0.1 as LocalIP for IPv6 connections. Moonlight clients with IPv6
    // support know to ignore this bogus address.
    if (local_endpoint.address().is_v6() && !local_endpoint.address().to_v6().is_v4_mapped()) {
      tree.put("root.LocalIP", "127.0.0.1");
    } else {
      tree.put("root.LocalIP", net::addr_to_normalized_string(local_endpoint.address()));
    }

    uint32_t codec_mode_flags = SCM_H264;
    if (video::last_encoder_probe_supported_yuv444_for_codec[0]) {
      codec_mode_flags |= SCM_H264_HIGH8_444;
    }
    if (video::active_hevc_mode >= 2) {
      codec_mode_flags |= SCM_HEVC;
      if (video::last_encoder_probe_supported_yuv444_for_codec[1]) {
        codec_mode_flags |= SCM_HEVC_REXT8_444;
      }
    }
    if (video::active_hevc_mode >= 3) {
      codec_mode_flags |= SCM_HEVC_MAIN10;
      if (video::last_encoder_probe_supported_yuv444_for_codec[1]) {
        codec_mode_flags |= SCM_HEVC_REXT10_444;
      }
    }
    if (video::active_av1_mode >= 2) {
      codec_mode_flags |= SCM_AV1_MAIN8;
      if (video::last_encoder_probe_supported_yuv444_for_codec[2]) {
        codec_mode_flags |= SCM_AV1_HIGH8_444;
      }
    }
    if (video::active_av1_mode >= 3) {
      codec_mode_flags |= SCM_AV1_MAIN10;
      if (video::last_encoder_probe_supported_yuv444_for_codec[2]) {
        codec_mode_flags |= SCM_AV1_HIGH10_444;
      }
    }
    tree.put("root.ServerCodecModeSupport", codec_mode_flags);

    tree.put("root.PairStatus", pair_status);

    if constexpr (std::is_same_v<SunshineHTTPS, T>) {
      int current_appid = proc::proc.running();
      // When input only mode is enabled, the only resume method should be launching the same app again.
      if (config::input.enable_input_only_mode && current_appid != proc::input_only_app_id) {
        current_appid = 0;
      }
      tree.put("root.currentgame", current_appid);
      tree.put("root.currentgameuuid", proc::proc.get_running_app_uuid());
      tree.put("root.state", current_appid > 0 ? "SUNSHINE_SERVER_BUSY" : "SUNSHINE_SERVER_FREE");
    } else {
      tree.put("root.currentgame", 0);
      tree.put("root.currentgameuuid", "");
      tree.put("root.state", "SUNSHINE_SERVER_FREE");
    }

    std::ostringstream data;

    pt::write_xml(data, tree);
    response->write(data.str());
    response->close_connection_after_response = true;
  }

  nlohmann::json get_all_clients() {
    nlohmann::json named_cert_nodes = nlohmann::json::array();
    client_t &client = client_root;
    std::list<std::string> connected_uuids = rtsp_stream::get_all_session_uuids();

    for (auto &named_cert : client.named_devices) {
      nlohmann::json named_cert_node;
      named_cert_node["name"] = named_cert->name;
      named_cert_node["uuid"] = named_cert->uuid;
      named_cert_node["display_mode"] = named_cert->display_mode;
      named_cert_node["perm"] = static_cast<uint32_t>(named_cert->perm);
      named_cert_node["access_mode"] =
        named_cert->perm == PERM::_all ? "operate" : "observe";
      named_cert_node["enable_legacy_ordering"] = named_cert->enable_legacy_ordering;
      named_cert_node["allow_client_commands"] = named_cert->allow_client_commands;
      named_cert_node["always_use_virtual_display"] = named_cert->always_use_virtual_display;

      // Add "do" commands if available
      if (!named_cert->do_cmds.empty()) {
        nlohmann::json do_cmds_node = nlohmann::json::array();
        for (const auto &cmd : named_cert->do_cmds) {
          do_cmds_node.push_back(crypto::command_entry_t::serialize(cmd));
        }
        named_cert_node["do"] = do_cmds_node;
      }

      // Add "undo" commands if available
      if (!named_cert->undo_cmds.empty()) {
        nlohmann::json undo_cmds_node = nlohmann::json::array();
        for (const auto &cmd : named_cert->undo_cmds) {
          undo_cmds_node.push_back(crypto::command_entry_t::serialize(cmd));
        }
        named_cert_node["undo"] = undo_cmds_node;
      }

      // Determine connection status
      bool connected = false;
      if (connected_uuids.empty()) {
        connected = false;
      } else {
        for (auto it = connected_uuids.begin(); it != connected_uuids.end(); ++it) {
          if (*it == named_cert->uuid) {
            connected = true;
            connected_uuids.erase(it);
            break;
          }
        }
      }
      named_cert_node["connected"] = connected;

      named_cert_nodes.push_back(named_cert_node);
    }

    return named_cert_nodes;
  }

  namespace {
    std::mutex ligase_sync_mutex;

    fs::path ligase_root_path() {
      return fs::path(config::stream.file_apps).parent_path().parent_path();
    }

    fs::path ligase_sync_path() {
      return ligase_root_path() / "ligase-sync.json";
    }

    fs::path ligase_streaming_path() {
      return ligase_root_path() / "streaming.json";
    }

    fs::path ligase_library_path() {
      return ligase_root_path() / "library.json";
    }

    fs::path ligase_authority_path() {
      return ligase_root_path() / "ligase-authority.json";
    }

    std::string ligase_timestamp() {
      const auto now = std::chrono::system_clock::now();
      const auto value = std::chrono::system_clock::to_time_t(now);
      std::tm utc {};
    #ifdef _WIN32
      gmtime_s(&utc, &value);
    #else
      gmtime_r(&value, &utc);
    #endif
      std::ostringstream output;
      output << std::put_time(&utc, "%Y-%m-%dT%H:%M:%SZ");
      return output.str();
    }

    nlohmann::json read_ligase_json(const fs::path &path) {
      std::ifstream input(path);
      if (!input.is_open()) {
        throw std::runtime_error("Ligase sync file is not available");
      }
      return nlohmann::json::parse(input);
    }

    void write_ligase_json(const fs::path &path, const nlohmann::json &document) {
      const auto temporary = fs::path(path.string() + ".tmp");
      {
        std::ofstream output(temporary, std::ios::trunc);
        if (!output.is_open()) {
          throw std::runtime_error("Cannot create Ligase sync temporary file");
        }
        output << document.dump(2);
      }

    #ifdef _WIN32
      if (!MoveFileExW(
            temporary.c_str(),
            path.c_str(),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        fs::remove(temporary);
        throw std::runtime_error("Cannot replace Ligase sync file");
      }
    #else
      fs::rename(temporary, path);
    #endif
    }

    template<class Response>
    void send_ligase_json(
      std::shared_ptr<Response> response,
      SimpleWeb::StatusCode status,
      const nlohmann::json &document
    ) {
      SimpleWeb::CaseInsensitiveMultimap headers;
      headers.emplace("Content-Type", "application/json");
      headers.emplace("Cache-Control", "no-store");
      response->write(status, document.dump(), headers);
      response->close_connection_after_response = true;
    }

    SimpleWeb::StatusCode attended_status(int status) {
      switch (status) {
        case 200: return SimpleWeb::StatusCode::success_ok;
        case 201: return SimpleWeb::StatusCode::success_created;
        case 202: return SimpleWeb::StatusCode::success_accepted;
        case 204: return SimpleWeb::StatusCode::success_no_content;
        case 400: return SimpleWeb::StatusCode::client_error_bad_request;
        case 401: return SimpleWeb::StatusCode::client_error_unauthorized;
        case 404: return SimpleWeb::StatusCode::client_error_not_found;
        case 409: return SimpleWeb::StatusCode::client_error_conflict;
        case 410: return SimpleWeb::StatusCode::client_error_gone;
        case 415: return SimpleWeb::StatusCode::client_error_unsupported_media_type;
        case 429: return SimpleWeb::StatusCode::client_error_too_many_requests;
        default: return SimpleWeb::StatusCode::server_error_service_unavailable;
      }
    }

    attended_pairing::source_identity attended_source(
      const boost::asio::ip::address &address
    ) {
      if (address.is_v4()) {
        return {address.to_v4().to_string(), 0};
      }
      auto v6 = address.to_v6();
      if (v6.is_v4_mapped()) {
        const auto bytes = v6.to_bytes();
        boost::asio::ip::address_v4::bytes_type v4 {
          bytes[12], bytes[13], bytes[14], bytes[15]
        };
        return {boost::asio::ip::address_v4(v4).to_string(), 0};
      }
      const auto scope_id = v6.is_link_local() ? v6.scope_id() : 0;
      v6.scope_id(0);
      return {v6.to_string(), scope_id};
    }

    void attended_route(resp_http_t response, req_http_t request) {
      if (!attended_pairing_router) {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::server_error_service_unavailable,
          {{"code", "pairingUnavailable"}}
        );
        return;
      }
      const auto source = attended_source(request->remote_endpoint().address());
      const bool source_loopback =
        request->remote_endpoint().address().is_loopback()
        || source.address.starts_with("127.");
      attended_pairing::http::request input {
        .method = request->method,
        .path = request->path,
        .source = source,
        .loopback = source_loopback
      };
      for (const auto &[name, value] : request->header) {
        input.headers.emplace_back(name, value);
      }
      const auto body = request->content.string();
      input.body.assign(body.begin(), body.end());
      const auto output = attended_pairing_router->handle(
        input,
        attended_pairing::pairing_service::steady_clock::now(),
        attended_pairing::pairing_service::wall_clock::now()
      );
      SimpleWeb::CaseInsensitiveMultimap headers;
      for (const auto &[name, value] : output.headers) headers.emplace(name, value);
      response->write(
        attended_status(output.status),
        std::string(output.body.begin(), output.body.end()),
        headers
      );
      response->close_connection_after_response = true;
    }

    attended_pairing::bytes host_certificate_der(std::string_view pem) {
      auto certificate = crypto::x509(std::string(pem));
      const auto length = i2d_X509(certificate.get(), nullptr);
      if (length <= 0) throw std::runtime_error("Host certificate DER unavailable");
      attended_pairing::bytes result(static_cast<std::size_t>(length));
      auto *cursor = result.data();
      if (i2d_X509(certificate.get(), &cursor) != length) {
        throw std::runtime_error("Host certificate DER conversion failed");
      }
      return result;
    }

    bool ligase_mutation_authorized(resp_https_t response, req_https_t request) {
      auto named_cert_p = get_verified_cert(request);
      if (ligase_client_can_mutate(*named_cert_p)) {
        return true;
      }

      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_forbidden,
        {{"error", "permissionDenied"}}
      );
      return false;
    }

    bool valid_ligase_resolution(const nlohmann::json &resolution) {
      if (!resolution.is_object() ||
          !resolution.contains("width") ||
          !resolution.contains("height") ||
          !resolution["width"].is_number_integer() ||
          !resolution["height"].is_number_integer()) {
        return false;
      }
      const auto width = resolution["width"].get<int>();
      const auto height = resolution["height"].get<int>();
      return width >= 320 && width <= 16384 && height >= 240 && height <= 16384;
    }

    bool ligase_uuid_equals(std::string_view left, std::string_view right) {
      return left.size() == right.size() &&
             std::equal(
               left.begin(),
               left.end(),
               right.begin(),
               [](unsigned char left_char, unsigned char right_char) {
                 return std::tolower(left_char) == std::tolower(right_char);
               }
             );
    }

    std::optional<std::string> find_ligase_library_uuid(
      const nlohmann::json &items,
      std::string_view requested_uuid
    ) {
      for (const auto &item : items) {
        if (!item.is_object() || !item.contains("id") || !item.at("id").is_string()) {
          continue;
        }
        const auto canonical_uuid = item.at("id").get<std::string>();
        if (ligase_uuid_equals(canonical_uuid, requested_uuid)) {
          return canonical_uuid;
        }
      }
      return std::nullopt;
    }

    const nlohmann::json *find_ligase_app_settings(
      const nlohmann::json &apps,
      std::string_view requested_uuid
    ) {
      if (!apps.is_object()) {
        return nullptr;
      }
      for (auto iterator = apps.begin(); iterator != apps.end(); ++iterator) {
        if (ligase_uuid_equals(iterator.key(), requested_uuid)) {
          return &iterator.value();
        }
      }
      return nullptr;
    }

    std::optional<nlohmann::json> parse_ligase_request(
      resp_https_t response,
      req_https_t request
    ) {
      try {
        std::stringstream body;
        body << request->content.rdbuf();
        return nlohmann::json::parse(body.str());
      } catch (const std::exception &error) {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_bad_request,
          {{"error", "invalidJson"}, {"message", error.what()}}
        );
        return std::nullopt;
      }
    }

    void send_ligase_revision_conflict(
      resp_https_t response,
      std::int64_t current_revision
    ) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_conflict,
        {
          {"error", "revisionConflict"},
          {"currentRevision", current_revision}
        }
      );
    }

    nlohmann::json build_ligase_authority_readback(const nlohmann::json &authority) {
      nlohmann::json library_items = nlohmann::json::array();
      nlohmann::json library_order = nlohmann::json::array();
      const auto sync = read_ligase_json(ligase_sync_path());
      for (const auto &item : sync.at("library").at("items")) {
        library_order.push_back(item.at("id"));
        library_items.push_back({
          {"id", item.at("id")},
          {"kind", item.at("kind")},
          {"steamAppId", item.value("steamAppId", nlohmann::json(nullptr))},
          {"publishedToClients", item.value("publishedToClients", true)}
        });
      }

      nlohmann::json loaded_apps = nlohmann::json::array();
      for (const auto &app : proc::proc.get_apps()) {
        loaded_apps.push_back({
          {"uuid", app.uuid},
          {"appId", app.id}
        });
      }

      return {
        {"schemaVersion", 1},
        {"authorityToken", authority.at("token")},
        {"startNonce", authority.at("startNonce")},
        {"rootFingerprint", authority.at("rootFingerprint")},
        {"hostUniqueId", http::unique_id},
        {"libraryItems", library_items},
        {"apps", loaded_apps},
        {"libraryRevision", sync.at("library").value("revision", std::int64_t {0})},
        {"librarySortMode", sync.at("library").value("sortMode", "nameAscending")},
        {"libraryOrder", library_order}
      };
    }

    template<class Request>
    bool ligase_request_is_loopback(const std::shared_ptr<Request> &request) {
      const auto address = request->remote_endpoint().address();
      if (address.is_loopback()) {
        return true;
      }
      if (!address.is_v6()) {
        return false;
      }
      const auto text = address.to_string();
      return text.starts_with("::ffff:127.");
    }

    void apply_ligase_resolution(
      const std::string &app_uuid,
      rtsp_stream::launch_session_t &session
    ) {
      if (app_uuid.empty()) {
        return;
      }

      try {
        std::scoped_lock lock(ligase_sync_mutex);
        const auto sync = read_ligase_json(ligase_sync_path());
        const auto &streaming = sync.at("streaming");
        const auto &apps = streaming.at("apps");
        const nlohmann::json *resolution = &streaming.at("globalResolution");
        if (const auto *candidate = find_ligase_app_settings(apps, app_uuid);
            candidate != nullptr) {
          if (candidate->is_object() &&
              candidate->contains("resolution") &&
              !candidate->at("resolution").is_null()) {
            resolution = &candidate->at("resolution");
          }
        }

        if (!valid_ligase_resolution(*resolution)) {
          BOOST_LOG(warning) << "Ignoring invalid Ligase resolution for app UUID [" << app_uuid << "]";
          return;
        }

        session.width = resolution->at("width").get<int>();
        session.height = resolution->at("height").get<int>();
        BOOST_LOG(info) << "Applied Ligase resolution " << session.width << 'x' << session.height
                        << " for app UUID [" << app_uuid << "]";
      } catch (const std::exception &error) {
        BOOST_LOG(debug) << "Ligase resolution override unavailable: " << error.what();
      }
    }
  }

  void ligase_sync(resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);
    auto named_cert_p = get_verified_cert(request);
    if (!ligase_client_can_read_library(*named_cert_p)) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_forbidden,
        {{"error", "permissionDenied"}}
      );
      return;
    }

    try {
      std::scoped_lock lock(ligase_sync_mutex);
      auto sync = read_ligase_json(ligase_sync_path());
      sync["capabilities"] = {
        {"hdrEncodingSupported", video::active_hevc_mode == 3}
      };
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::success_ok,
        sync
      );
    } catch (const std::exception &error) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"error", "syncUnavailable"}, {"message", error.what()}}
      );
    }
  }

  void ligase_update_streaming(resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);
    if (!ligase_mutation_authorized(response, request)) {
      return;
    }
    const auto request_json = parse_ligase_request(response, request);
    if (!request_json) {
      return;
    }

    try {
      std::scoped_lock lock(ligase_sync_mutex);
      auto sync = read_ligase_json(ligase_sync_path());
      auto state = read_ligase_json(ligase_streaming_path());
      const auto current_revision = state.value("revision", std::int64_t {0});
      if (!request_json->contains("baseRevision") ||
          !request_json->at("baseRevision").is_number_integer()) {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_bad_request,
          {{"error", "baseRevisionRequired"}}
        );
        return;
      }
      if (request_json->at("baseRevision").get<std::int64_t>() != current_revision) {
        send_ligase_revision_conflict(response, current_revision);
        return;
      }

      bool changed = false;
      if (request_json->contains("globalResolution")) {
        if (!valid_ligase_resolution(request_json->at("globalResolution"))) {
          send_ligase_json(
            response,
            SimpleWeb::StatusCode::client_error_bad_request,
            {{"error", "invalidResolution"}}
          );
          return;
        }
        state["globalResolution"] = request_json->at("globalResolution");
        changed = true;
      }

      if (request_json->contains("app")) {
        const auto &app = request_json->at("app");
        if (!app.is_object() ||
            !app.contains("id") ||
            !app.at("id").is_string() ||
            !app.contains("resolution")) {
          send_ligase_json(
            response,
            SimpleWeb::StatusCode::client_error_bad_request,
            {{"error", "invalidAppOverride"}}
          );
          return;
        }
        const auto requested_app_id = app.at("id").get<std::string>();
        const auto &items = sync.at("library").at("items");
        const auto app_id = find_ligase_library_uuid(items, requested_app_id);
        if (!app_id) {
          send_ligase_json(
            response,
            SimpleWeb::StatusCode::client_error_not_found,
            {{"error", "appNotFound"}}
          );
          return;
        }
        if (app.at("resolution").is_null()) {
          if (const auto *existing = find_ligase_app_settings(state["apps"], *app_id);
              existing != nullptr) {
            const auto existing_key = std::find_if(
              state["apps"].begin(),
              state["apps"].end(),
              [&](const auto &entry) {
                return &entry == existing;
              }
            );
            if (existing_key != state["apps"].end()) {
              state["apps"].erase(existing_key);
            }
          }
        } else {
          if (!valid_ligase_resolution(app.at("resolution"))) {
            send_ligase_json(
              response,
              SimpleWeb::StatusCode::client_error_bad_request,
              {{"error", "invalidResolution"}}
            );
            return;
          }
          state["apps"][*app_id] = {{"resolution", app.at("resolution")}};
        }
        changed = true;
      }

      if (!changed) {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_bad_request,
          {{"error", "noChanges"}}
        );
        return;
      }

      state["revision"] = current_revision + 1;
      state["updatedAt"] = ligase_timestamp();
      write_ligase_json(ligase_streaming_path(), state);

      sync["streaming"] = state;
      write_ligase_json(ligase_sync_path(), sync);
      send_ligase_json(response, SimpleWeb::StatusCode::success_ok, state);
    } catch (const std::exception &error) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::server_error_internal_server_error,
        {{"error", "streamingUpdateFailed"}, {"message", error.what()}}
      );
    }
  }

  void ligase_update_library_sort(resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);
    auto named_cert_p = get_verified_cert(request);
    const auto output = ligase::library::http::handle_sort(
      {
        .authorized = ligase_client_can_mutate(*named_cert_p),
        .body = request->content.string()
      },
      {
        .mutation_mutex = ligase_sync_mutex,
        .load_library = []() { return read_ligase_json(ligase_library_path()); },
        .load_sync = []() { return read_ligase_json(ligase_sync_path()); },
        .save_library = [](const auto &value) {
          write_ligase_json(ligase_library_path(), value);
        },
        .save_sync = [](const auto &value) {
          write_ligase_json(ligase_sync_path(), value);
        },
        .timestamp = ligase_timestamp
      }
    );
    const auto status = [&]() {
      switch (output.status) {
        case 200: return SimpleWeb::StatusCode::success_ok;
        case 400: return SimpleWeb::StatusCode::client_error_bad_request;
        case 403: return SimpleWeb::StatusCode::client_error_forbidden;
        case 409: return SimpleWeb::StatusCode::client_error_conflict;
        default: return SimpleWeb::StatusCode::server_error_internal_server_error;
      }
    }();
    send_ligase_json(response, status, output.body);
  }

  void ligase_devices_local(resp_http_t response, req_http_t request) {
    print_req<SimpleWeb::HTTP>(request);
    if (!ligase_request_is_loopback(request)) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_forbidden,
        {{"error", "loopbackOnly"}}
      );
      return;
    }

    send_ligase_json(
      response,
      SimpleWeb::StatusCode::success_ok,
      {
        {"schemaVersion", 1},
        {"devices", get_all_clients()}
      }
    );
  }

  std::optional<std::string> ligase_canonical_path_uuid(
    std::string_view path,
    std::string_view prefix,
    std::string_view suffix = {}
  ) {
    if (!path.starts_with(prefix) || !path.ends_with(suffix)) return {};
    const auto start = prefix.size();
    const auto count = path.size() - prefix.size() - suffix.size();
    const auto value = std::string(path.substr(start, count));
    static const std::regex canonical(
      "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
    return std::regex_match(value, canonical)
      ? std::optional<std::string>(value)
      : std::nullopt;
  }

  void ligase_pairing_access_local(resp_http_t response, req_http_t request) {
    print_req<SimpleWeb::HTTP>(request);
    if (!ligase_request_is_loopback(request) || !attended_pairing_service) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"code", "requestNotFound"}});
      return;
    }
    const auto request_id = ligase_canonical_path_uuid(
      request->path, "/ligase/v1/pairing/requests/", "/access");
    if (!request_id) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"code", "requestNotFound"}});
      return;
    }
    try {
      const auto value = nlohmann::json::parse(request->content.string());
      if (!value.is_object() || value.size() != 1 ||
          !value.contains("mode") || !value["mode"].is_string()) {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_bad_request,
          {{"code", "invalidRequest"}});
        return;
      }
      const auto mode = value["mode"].get<std::string>();
      if (mode != "operate" && mode != "observe") {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_bad_request,
          {{"code", "invalidAccessMode"}});
        return;
      }
      attended_pairing_service->set_access_mode(
        *request_id,
        mode == "observe"
          ? attended_pairing::access_mode::observe
          : attended_pairing::access_mode::operate,
        attended_pairing::pairing_service::steady_clock::now());
      response->write(SimpleWeb::StatusCode::success_no_content);
    } catch (const attended_pairing::service_exception &error) {
      const auto status = error.code() ==
        attended_pairing::service_error::request_not_found
          ? SimpleWeb::StatusCode::client_error_not_found
          : SimpleWeb::StatusCode::client_error_conflict;
      send_ligase_json(
        response,
        status,
        {{"code", error.code() ==
          attended_pairing::service_error::request_not_found
            ? "requestNotFound"
            : "invalidState"}});
    } catch (...) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_bad_request,
        {{"code", "invalidRequest"}});
    }
  }

  void ligase_device_access_local(resp_http_t response, req_http_t request) {
    print_req<SimpleWeb::HTTP>(request);
    if (!ligase_request_is_loopback(request)) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"code", "deviceNotFound"}});
      return;
    }
    const auto uuid = ligase_canonical_path_uuid(
      request->path, "/ligase/v1/devices/", "/access");
    if (!uuid) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"code", "deviceNotFound"}});
      return;
    }
    try {
      const auto value = nlohmann::json::parse(request->content.string());
      if (!value.is_object() || value.size() != 1 ||
          !value.contains("mode") || !value["mode"].is_string()) {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_bad_request,
          {{"code", "invalidRequest"}});
        return;
      }
      const auto mode = value["mode"].get<std::string>();
      if (mode != "operate" && mode != "observe") {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_bad_request,
          {{"code", "invalidAccessMode"}});
        return;
      }
      for (const auto &device : client_root.named_devices) {
        if (!ligase_uuid_equals(device->uuid, *uuid)) continue;
        const auto permission = mode == "observe" ? PERM::_default : PERM::_all;
        update_device_info(
          device->uuid,
          device->name,
          device->display_mode,
          device->do_cmds,
          device->undo_cmds,
          permission,
          device->enable_legacy_ordering,
          mode == "operate",
          device->always_use_virtual_display);
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::success_ok,
          {{"accessMode", mode}, {"uuid", *uuid}});
        return;
      }
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"code", "deviceNotFound"}});
    } catch (...) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_bad_request,
        {{"code", "invalidRequest"}});
    }
  }

  void ligase_device_delete_local(resp_http_t response, req_http_t request) {
    print_req<SimpleWeb::HTTP>(request);
    if (!ligase_request_is_loopback(request)) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"code", "deviceNotFound"}});
      return;
    }
    if (!request->content.string().empty()) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_bad_request,
        {{"code", "unexpectedBody"}});
      return;
    }
    const bool end_session = request->path.ends_with(
      "/end-session-and-delete");
    const auto uuid = ligase_canonical_path_uuid(
      request->path,
      "/ligase/v1/devices/",
      end_session ? "/end-session-and-delete" : "");
    if (!uuid) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"code", "deviceNotFound"}});
      return;
    }
    const auto device = std::ranges::find_if(
      client_root.named_devices,
      [&](const auto &candidate) {
        return ligase_uuid_equals(candidate->uuid, *uuid);
      });
    if (device == client_root.named_devices.end()) {
      response->write(SimpleWeb::StatusCode::success_no_content);
      return;
    }
    const auto persisted_uuid = (*device)->uuid;
    const auto session = rtsp_stream::find_session(persisted_uuid);
    if (session && !end_session) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_conflict,
        {{"code", "deviceActive"}});
      return;
    }
    if (session) stop_session(*session, true);
    unpair_client(persisted_uuid);
    response->write(SimpleWeb::StatusCode::success_no_content);
  }

  void ligase_cancel_session_local(resp_http_t response, req_http_t request) {
    print_req<SimpleWeb::HTTP>(request);
    if (!ligase_request_is_loopback(request)) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_forbidden,
        {{"error", "loopbackOnly"}}
      );
      return;
    }

    const auto was_running = proc::proc.running() > 0;
    rtsp_stream::terminate_sessions();
    if (was_running) {
      proc::proc.terminate();
    }
    display_device::revert_configuration();
    send_ligase_json(
      response,
      SimpleWeb::StatusCode::success_ok,
      {
        {"cancelled", was_running},
        {"sessionState", "free"}
      }
    );
  }

  void ligase_authority_readback_local(resp_http_t response, req_http_t request) {
    print_req<SimpleWeb::HTTP>(request);
    if (!ligase_request_is_loopback(request)) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_forbidden,
        {{"error", "loopbackOnly"}}
      );
      return;
    }

    try {
      std::stringstream body;
      body << request->content.rdbuf();
      const auto request_json = nlohmann::json::parse(body.str());
      const auto authority = read_ligase_json(ligase_authority_path());
      if (!request_json.contains("token") ||
          !request_json["token"].is_string() ||
          !authority.contains("token") ||
          request_json["token"] != authority["token"]) {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_forbidden,
          {{"error", "authorityMismatch"}}
        );
        return;
      }

      send_ligase_json(
        response,
        SimpleWeb::StatusCode::success_ok,
        build_ligase_authority_readback(authority)
      );
    } catch (const std::exception &) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"error", "authorityUnavailable"}}
      );
    }
  }

  void ligase_authority_reload_local(resp_http_t response, req_http_t request) {
    print_req<SimpleWeb::HTTP>(request);
    if (!ligase_request_is_loopback(request)) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_forbidden,
        {{"error", "loopbackOnly"}}
      );
      return;
    }

    nlohmann::json authority;
    try {
      std::stringstream body;
      body << request->content.rdbuf();
      const auto request_json = nlohmann::json::parse(body.str());
      authority = read_ligase_json(ligase_authority_path());
      if (!request_json.contains("token") ||
          !request_json["token"].is_string() ||
          !authority.contains("token") ||
          request_json["token"] != authority["token"]) {
        send_ligase_json(
          response,
          SimpleWeb::StatusCode::client_error_forbidden,
          {{"error", "authorityMismatch"}}
        );
        return;
      }
    } catch (const std::exception &) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_not_found,
        {{"error", "authorityUnavailable"}}
      );
      return;
    }

    if (proc::proc.running() > 0) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::client_error_conflict,
        {{"error", "sessionActive"}}
      );
      return;
    }

    try {
      proc::refresh(config::stream.file_apps, false);
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::success_ok,
        build_ligase_authority_readback(authority)
      );
    } catch (const std::exception &) {
      send_ligase_json(
        response,
        SimpleWeb::StatusCode::server_error_internal_server_error,
        {{"error", "reloadFailed"}}
      );
    }
  }

  void applist(resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);

    pt::ptree tree;

    auto g = util::fail_guard([&]() {
      std::ostringstream data;

      pt::write_xml(data, tree);
      response->write(data.str());
      response->close_connection_after_response = true;
    });

    auto &apps = tree.add_child("root", pt::ptree {});

    apps.put("<xmlattr>.status_code", 200);

    auto named_cert_p = get_verified_cert(request);
    if (!!(named_cert_p->perm & PERM::list)) {
      auto current_appid = proc::proc.running();
      auto should_hide_inactive_apps = config::input.enable_input_only_mode && current_appid > 0 && current_appid != proc::input_only_app_id;

      auto app_list = proc::proc.get_apps();

      bool enable_legacy_ordering = config::sunshine.legacy_ordering && named_cert_p->enable_legacy_ordering;
      size_t bits;
      if (enable_legacy_ordering) {
        bits = zwpad::pad_width_for_count(app_list.size());
      }

      for (size_t i = 0; i < app_list.size(); i++) {
        auto& app = app_list[i];
        auto appid = util::from_view(app.id);
        if (should_hide_inactive_apps) {
          if (
            appid != current_appid
            && appid != proc::input_only_app_id
            && appid != proc::terminate_app_id
          ) {
            continue;
          }
        } else {
          if (appid == proc::terminate_app_id) {
            continue;
          }
        }

        std::string app_name;
        if (enable_legacy_ordering) {
          app_name = zwpad::pad_for_ordering(app.name, bits, i);
        } else {
          app_name = app.name;
        }

        pt::ptree app_node;

        app_node.put("IsHdrSupported"s, video::active_hevc_mode == 3 ? 1 : 0);
        app_node.put("AppTitle"s, app_name);
        app_node.put("UUID", app.uuid);
        app_node.put("IDX", app.idx);
        app_node.put("ID", app.id);

        apps.push_back(std::make_pair("App", std::move(app_node)));
      }
    } else {
      BOOST_LOG(debug) << "Permission ListApp denied for [" << named_cert_p->name << "] (" << (uint32_t)named_cert_p->perm << ")";

      pt::ptree app_node;

      app_node.put("IsHdrSupported"s, 0);
      app_node.put("AppTitle"s, "Permission Denied");
      app_node.put("UUID", "");
      app_node.put("IDX", "0");
      app_node.put("ID", "114514");

      apps.push_back(std::make_pair("App", std::move(app_node)));

      return;
    }

  }

  void launch(bool &host_audio, resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);

    pt::ptree tree;
    auto g = util::fail_guard([&]() {
      std::ostringstream data;

      pt::write_xml(data, tree);
      response->write(data.str());
      response->close_connection_after_response = true;
    });

    auto args = request->parse_query_string();

    auto appid_str = get_arg(args, "appid", "0");
    auto appuuid_str = get_arg(args, "appuuid", "");
    auto appid = util::from_view(appid_str);
    auto current_appid = proc::proc.running();
    auto current_app_uuid = proc::proc.get_running_app_uuid();
    bool is_input_only = config::input.enable_input_only_mode && (appid == proc::input_only_app_id || (appuuid_str == REMOTE_INPUT_UUID));

    auto named_cert_p = get_verified_cert(request);
    auto perm = PERM::launch;

    BOOST_LOG(verbose) << "Launching app [" << appid_str << "] with UUID [" << appuuid_str << "]";
    // BOOST_LOG(verbose) << "QS: " << request->query_string;

    // If we have already launched an app, we should allow clients with view permission to join the input only or current app's session.
    if (
      current_appid > 0
      && (appuuid_str != TERMINATE_APP_UUID || appid != proc::terminate_app_id)
      && (is_input_only || appid == current_appid || (!appuuid_str.empty() && appuuid_str == current_app_uuid))
    ) {
      perm = PERM::_allow_view;
    }

    if (!(named_cert_p->perm & perm)) {
      BOOST_LOG(debug) << "Permission LaunchApp denied for [" << named_cert_p->name << "] (" << (uint32_t)named_cert_p->perm << ")";

      tree.put("root.resume", 0);
      tree.put("root.<xmlattr>.status_code", 403);
      tree.put("root.<xmlattr>.status_message", "Permission denied");

      return;
    }
    if (
      args.find("rikey"s) == std::end(args) ||
      args.find("rikeyid"s) == std::end(args) ||
      args.find("localAudioPlayMode"s) == std::end(args) ||
      (args.find("appid"s) == std::end(args) && args.find("appuuid"s) == std::end(args))
    ) {
      tree.put("root.resume", 0);
      tree.put("root.<xmlattr>.status_code", 400);
      tree.put("root.<xmlattr>.status_message", "Missing a required launch parameter");

      return;
    }

    if (!is_input_only) {
      // Special handling for the "terminate" app
      if (
        (config::input.enable_input_only_mode && appid == proc::terminate_app_id)
        || appuuid_str == TERMINATE_APP_UUID
      ) {
        proc::proc.terminate();

        tree.put("root.resume", 0);
        tree.put("root.<xmlattr>.status_code", 410);
        tree.put("root.<xmlattr>.status_message", "App terminated.");

        return;
      }

      if (
        current_appid > 0
        && current_appid != proc::input_only_app_id
        && (
          (appid > 0 && appid != current_appid)
          || (!appuuid_str.empty() && appuuid_str != current_app_uuid)
        )
      ) {
        tree.put("root.resume", 0);
        tree.put("root.<xmlattr>.status_code", 400);
        tree.put("root.<xmlattr>.status_message", "An app is already running on this host");

        return;
      }
    }

    host_audio = util::from_view(get_arg(args, "localAudioPlayMode"));
    auto launch_session = make_launch_session(host_audio, is_input_only, args, named_cert_p);
    std::string resolved_app_uuid = appuuid_str;
    if (resolved_app_uuid.empty() && appid > 0) {
      const auto &apps = proc::proc.get_apps();
      const auto app = std::find_if(apps.begin(), apps.end(), [&appid_str](const auto &candidate) {
        return candidate.id == appid_str;
      });
      if (app != apps.end()) {
        resolved_app_uuid = app->uuid;
      }
    }
    apply_ligase_resolution(resolved_app_uuid, *launch_session);

    auto encryption_mode = net::encryption_mode_for_address(request->remote_endpoint().address());
    if (!launch_session->rtsp_cipher && encryption_mode == config::ENCRYPTION_MODE_MANDATORY) {
      BOOST_LOG(error) << "Rejecting client that cannot comply with mandatory encryption requirement"sv;

      tree.put("root.<xmlattr>.status_code", 403);
      tree.put("root.<xmlattr>.status_message", "Encryption is mandatory for this host but unsupported by the client");
      tree.put("root.gamesession", 0);

      return;
    }

    bool no_active_sessions = rtsp_stream::session_count() == 0;

    if (is_input_only) {
      BOOST_LOG(info) << "Launching input only session..."sv;

      launch_session->client_do_cmds.clear();
      launch_session->client_undo_cmds.clear();

      // Still probe encoders once, if input only session is launched first
      // But we're ignoring if it's successful or not
      if (no_active_sessions && !proc::proc.virtual_display) {
        video::probe_encoders();
        if (current_appid == 0) {
          proc::proc.launch_input_only();
        }
      }
    } else if (appid > 0 || !appuuid_str.empty()) {
      if (appid == current_appid || (!appuuid_str.empty() && appuuid_str == current_app_uuid)) {
        // We're basically resuming the same app

        BOOST_LOG(debug) << "Resuming app [" << proc::proc.get_last_run_app_name() << "] from launch app path...";

        if (!proc::proc.allow_client_commands || !named_cert_p->allow_client_commands) {
          launch_session->client_do_cmds.clear();
          launch_session->client_undo_cmds.clear();
        }

        if (current_appid == proc::input_only_app_id) {
          launch_session->input_only = true;
        }

        if (no_active_sessions && !proc::proc.virtual_display) {
          display_device::configure_display(config::video, *launch_session);
          if (video::probe_encoders()) {
            tree.put("root.resume", 0);
            tree.put("root.<xmlattr>.status_code", 503);
            tree.put("root.<xmlattr>.status_message", "Failed to initialize video capture/encoding. Is a display connected and turned on?");

            return;
          }
        }
      } else {
        const auto& apps = proc::proc.get_apps();
        auto app_iter = std::find_if(apps.begin(), apps.end(), [&appid_str, &appuuid_str](const auto _app) {
          return _app.id == appid_str || _app.uuid == appuuid_str;
        });

        if (app_iter == apps.end()) {
          BOOST_LOG(error) << "Couldn't find app with ID ["sv << appid_str << "] or UUID ["sv << appuuid_str << ']';
          tree.put("root.<xmlattr>.status_code", 404);
          tree.put("root.<xmlattr>.status_message", "Cannot find requested application");
          tree.put("root.gamesession", 0);
          return;
        }

        if (!app_iter->allow_client_commands) {
          launch_session->client_do_cmds.clear();
          launch_session->client_undo_cmds.clear();
        }

        auto err = proc::proc.execute(*app_iter, launch_session);
        if (err) {
          tree.put("root.<xmlattr>.status_code", err);
          tree.put(
            "root.<xmlattr>.status_message",
            err == 503
            ? "Failed to initialize video capture/encoding. Is a display connected and turned on?"
            : "Failed to start the specified application");
          tree.put("root.gamesession", 0);

          return;
        }
      }
    } else {
      tree.put("root.<xmlattr>.status_code", 403);
      tree.put("root.<xmlattr>.status_message", "How did you get here?");
      tree.put("root.gamesession", 0);
    }

    tree.put("root.<xmlattr>.status_code", 200);
    tree.put(
      "root.sessionUrl0",
      std::format(
        "{}{}:{}",
        launch_session->rtsp_url_scheme,
        net::addr_to_url_escaped_string(request->local_endpoint().address()),
        static_cast<int>(net::map_port(rtsp_stream::RTSP_SETUP_PORT))
      )
    );
    tree.put("root.gamesession", 1);

    rtsp_stream::launch_session_raise(launch_session);
  }

  void resume(bool &host_audio, resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);

    pt::ptree tree;
    auto g = util::fail_guard([&]() {
      std::ostringstream data;

      pt::write_xml(data, tree);
      response->write(data.str());
      response->close_connection_after_response = true;
    });

    auto named_cert_p = get_verified_cert(request);
    if (!(named_cert_p->perm & PERM::_allow_view)) {
      BOOST_LOG(debug) << "Permission ViewApp denied for [" << named_cert_p->name << "] (" << (uint32_t)named_cert_p->perm << ")";

      tree.put("root.resume", 0);
      tree.put("root.<xmlattr>.status_code", 403);
      tree.put("root.<xmlattr>.status_message", "Permission denied");

      return;
    }

    auto current_appid = proc::proc.running();
    if (current_appid == 0) {
      tree.put("root.resume", 0);
      tree.put("root.<xmlattr>.status_code", 503);
      tree.put("root.<xmlattr>.status_message", "No running app to resume");

      return;
    }

    auto args = request->parse_query_string();
    if (
      args.find("rikey"s) == std::end(args) ||
      args.find("rikeyid"s) == std::end(args)
    ) {
      tree.put("root.resume", 0);
      tree.put("root.<xmlattr>.status_code", 400);
      tree.put("root.<xmlattr>.status_message", "Missing a required resume parameter");

      return;
    }

    // Newer Moonlight clients send localAudioPlayMode on /resume too,
    // so we should use it if it's present in the args and there are
    // no active sessions we could be interfering with.
    const bool no_active_sessions {rtsp_stream::session_count() == 0};
    if (no_active_sessions && args.find("localAudioPlayMode"s) != std::end(args)) {
      host_audio = util::from_view(get_arg(args, "localAudioPlayMode"));
    }
    auto launch_session = make_launch_session(host_audio, false, args, named_cert_p);

    if (!proc::proc.allow_client_commands || !named_cert_p->allow_client_commands) {
      launch_session->client_do_cmds.clear();
      launch_session->client_undo_cmds.clear();
    }

    if (config::input.enable_input_only_mode && current_appid == proc::input_only_app_id) {
      launch_session->input_only = true;
    }

    if (no_active_sessions && !proc::proc.virtual_display) {
      // We want to prepare display only if there are no active sessions
      // and the current session isn't virtual display at the moment.
      // This should be done before probing encoders as it could change the active displays.
      display_device::configure_display(config::video, *launch_session);

      // Probe encoders again before streaming to ensure our chosen
      // encoder matches the active GPU (which could have changed
      // due to hotplugging, driver crash, primary monitor change,
      // or any number of other factors).
      if (video::probe_encoders()) {
        tree.put("root.resume", 0);
        tree.put("root.<xmlattr>.status_code", 503);
        tree.put("root.<xmlattr>.status_message", "Failed to initialize video capture/encoding. Is a display connected and turned on?");

        return;
      }
    }

    auto encryption_mode = net::encryption_mode_for_address(request->remote_endpoint().address());
    if (!launch_session->rtsp_cipher && encryption_mode == config::ENCRYPTION_MODE_MANDATORY) {
      BOOST_LOG(error) << "Rejecting client that cannot comply with mandatory encryption requirement"sv;

      tree.put("root.<xmlattr>.status_code", 403);
      tree.put("root.<xmlattr>.status_message", "Encryption is mandatory for this host but unsupported by the client");
      tree.put("root.gamesession", 0);

      return;
    }

    tree.put("root.<xmlattr>.status_code", 200);
    tree.put(
      "root.sessionUrl0",
      std::format(
        "{}{}:{}",
        launch_session->rtsp_url_scheme,
        net::addr_to_url_escaped_string(request->local_endpoint().address()),
        static_cast<int>(net::map_port(rtsp_stream::RTSP_SETUP_PORT))
      )
    );
    tree.put("root.resume", 1);

    rtsp_stream::launch_session_raise(launch_session);

#if defined SUNSHINE_TRAY && SUNSHINE_TRAY >= 1
    system_tray::update_tray_client_connected(named_cert_p->name);
#endif
  }

  void cancel(resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);

    pt::ptree tree;
    auto g = util::fail_guard([&]() {
      std::ostringstream data;

      pt::write_xml(data, tree);
      response->write(data.str());
      response->close_connection_after_response = true;
    });

    auto named_cert_p = get_verified_cert(request);
    if (!(named_cert_p->perm & PERM::launch)) {
      BOOST_LOG(debug) << "Permission CancelApp denied for [" << named_cert_p->name << "] (" << (uint32_t)named_cert_p->perm << ")";

      tree.put("root.resume", 0);
      tree.put("root.<xmlattr>.status_code", 403);
      tree.put("root.<xmlattr>.status_message", "Permission denied");

      return;
    }

    tree.put("root.cancel", 1);
    tree.put("root.<xmlattr>.status_code", 200);

    rtsp_stream::terminate_sessions();

    if (proc::proc.running() > 0) {
      proc::proc.terminate();
    }

    // The config needs to be reverted regardless of whether "proc::proc.terminate()" was called or not.
    display_device::revert_configuration();
  }

  void appasset(resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);

    auto fg = util::fail_guard([&]() {
      response->write(SimpleWeb::StatusCode::server_error_internal_server_error);
      response->close_connection_after_response = true;
    });

    auto named_cert_p = get_verified_cert(request);

    if (!(named_cert_p->perm & PERM::list)) {
      BOOST_LOG(debug) << "Permission Get AppAsset denied for [" << named_cert_p->name << "] (" << (uint32_t)named_cert_p->perm << ")";

      fg.disable();
      response->write(SimpleWeb::StatusCode::client_error_unauthorized);
      response->close_connection_after_response = true;
      return;
    }

    auto args = request->parse_query_string();
    auto app_image = proc::proc.get_app_image(util::from_view(get_arg(args, "appid")));

    fg.disable();

    std::ifstream in(app_image, std::ios::binary);
    SimpleWeb::CaseInsensitiveMultimap headers;
    headers.emplace("Content-Type", "image/png");
    response->write(SimpleWeb::StatusCode::success_ok, in, headers);
    response->close_connection_after_response = true;
  }

  void getClipboard(resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);

    auto named_cert_p = get_verified_cert(request);

    if (
      !(named_cert_p->perm & PERM::_allow_view)
      || !(named_cert_p->perm & PERM::clipboard_read)
    ) {
      BOOST_LOG(debug) << "Permission Read Clipboard denied for [" << named_cert_p->name << "] (" << (uint32_t)named_cert_p->perm << ")";

      response->write(SimpleWeb::StatusCode::client_error_unauthorized);
      response->close_connection_after_response = true;
      return;
    }

    auto args = request->parse_query_string();
    auto clipboard_type = get_arg(args, "type");
    if (clipboard_type != "text"sv) {
      BOOST_LOG(debug) << "Clipboard type [" << clipboard_type << "] is not supported!";

      response->write(SimpleWeb::StatusCode::client_error_bad_request);
      response->close_connection_after_response = true;
      return;
    }

    std::list<std::string> connected_uuids = rtsp_stream::get_all_session_uuids();

    bool found = !connected_uuids.empty();

    if (found) {
      found = (std::find(connected_uuids.begin(), connected_uuids.end(), named_cert_p->uuid) != connected_uuids.end());
    }

    if (!found) {
      BOOST_LOG(debug) << "Client ["<< named_cert_p->name << "] trying to get clipboard is not connected to a stream";

      response->write(SimpleWeb::StatusCode::client_error_forbidden);
      response->close_connection_after_response = true;
      return;
    }

    std::string content = platf::get_clipboard();
    response->write(content);
    return;
  }

  void
  setClipboard(resp_https_t response, req_https_t request) {
    print_req<SunshineHTTPS>(request);

    auto named_cert_p = get_verified_cert(request);

    if (
      !(named_cert_p->perm & PERM::_allow_view)
      || !(named_cert_p->perm & PERM::clipboard_set)
    ) {
      BOOST_LOG(debug) << "Permission Write Clipboard denied for [" << named_cert_p->name << "] (" << (uint32_t)named_cert_p->perm << ")";

      response->write(SimpleWeb::StatusCode::client_error_unauthorized);
      response->close_connection_after_response = true;
      return;
    }

    auto args = request->parse_query_string();
    auto clipboard_type = get_arg(args, "type");
    if (clipboard_type != "text"sv) {
      BOOST_LOG(debug) << "Clipboard type [" << clipboard_type << "] is not supported!";

      response->write(SimpleWeb::StatusCode::client_error_bad_request);
      response->close_connection_after_response = true;
      return;
    }

    std::list<std::string> connected_uuids = rtsp_stream::get_all_session_uuids();

    bool found = !connected_uuids.empty();

    if (found) {
      found = (std::find(connected_uuids.begin(), connected_uuids.end(), named_cert_p->uuid) != connected_uuids.end());
    }

    if (!found) {
      BOOST_LOG(debug) << "Client ["<< named_cert_p->name << "] trying to set clipboard is not connected to a stream";

      response->write(SimpleWeb::StatusCode::client_error_forbidden);
      response->close_connection_after_response = true;
      return;
    }

    std::string content = request->content.string();

    bool success = platf::set_clipboard(content);

    if (!success) {
      BOOST_LOG(debug) << "Setting clipboard failed!";

      response->write(SimpleWeb::StatusCode::server_error_internal_server_error);
      response->close_connection_after_response = true;
    }

    response->write();
    return;
  }

  void setup(const std::string &pkey, const std::string &cert) {
    conf_intern.pkey = pkey;
    conf_intern.servercert = cert;
  }

  void start() {
    auto shutdown_event = mail::man->event<bool>(mail::shutdown);

    auto port_http = net::map_port(PORT_HTTP);
    auto port_https = net::map_port(PORT_HTTPS);
    auto address_family = net::af_from_enum_string(config::sunshine.address_family);

    bool clean_slate = config::sunshine.flags[config::flag::FRESH_STATE];

    if (!clean_slate) {
      load_state();
    }

    auto pkey = file_handler::read_file(config::nvhttp.pkey.c_str());
    auto cert = file_handler::read_file(config::nvhttp.cert.c_str());
    setup(pkey, cert);
    const auto attended_internal =
      std::getenv("LIGASE_ATTENDED_PAIRING_INTERNAL");
    const bool attended_test_mode =
      attended_internal != nullptr
      && std::string_view(attended_internal) == "1";
    const bool attended_enabled = true;
    if (attended_enabled) {
      attended_pairing::service_config attended_config {
        .host_unique_id = http::unique_id,
        .host_certificate_der = host_certificate_der(cert)
      };
      if (const auto value = attended_test_mode
            ? std::getenv("LIGASE_ATTENDED_PAIRING_TEST_LIFETIME_SECONDS")
            : nullptr) {
        const auto seconds = std::clamp(std::atoi(value), 1, 120);
        attended_config.request_lifetime = std::chrono::seconds(seconds);
      }
      if (const auto value = attended_test_mode
            ? std::getenv("LIGASE_ATTENDED_PAIRING_TEST_RATE_LIMIT")
            : nullptr) {
        attended_config.rate_limit =
          static_cast<std::size_t>(std::clamp(std::atoi(value), 1, 64));
      }
      attended_pairing_service =
        std::make_unique<attended_pairing::pairing_service>(
          std::move(attended_config));
      attended_pairing_router =
        std::make_unique<attended_pairing::http::router>(
          *attended_pairing_service);
    }

    // resume doesn't always get the parameter "localAudioPlayMode"
    // launch will store it in host_audio
    bool host_audio {};

    https_server_t https_server {config::nvhttp.cert, config::nvhttp.pkey};
    http_server_t http_server;

    // Verify certificates after establishing connection
    https_server.verify = [](req_https_t req, SSL *ssl) {
      crypto::x509_t x509 {
#if OPENSSL_VERSION_MAJOR >= 3
        SSL_get1_peer_certificate(ssl)
#else
        SSL_get_peer_certificate(ssl)
#endif
      };
      if (!x509) {
        BOOST_LOG(info) << "unknown -- denied"sv;
        return false;
      }

      bool verified = false;
      p_named_cert_t named_cert_p;

      auto fg = util::fail_guard([&]() {
        char subject_name[256];

        X509_NAME_oneline(X509_get_subject_name(x509.get()), subject_name, sizeof(subject_name));

        if (verified) {
          BOOST_LOG(debug) << subject_name << " -- "sv << "verified, device name: "sv << named_cert_p->name;
        } else {
          BOOST_LOG(debug) << subject_name << " -- "sv << "denied"sv;
        }

      });

      auto err_str = cert_chain.verify(x509.get(), named_cert_p);
      if (err_str) {
        BOOST_LOG(warning) << "SSL Verification error :: "sv << err_str;
        return verified;
      }

      verified = true;
      req->userp = named_cert_p;

      return true;
    };

    https_server.on_verify_failed = [](resp_https_t resp, req_https_t req) {
      pt::ptree tree;
      auto g = util::fail_guard([&]() {
        std::ostringstream data;

        pt::write_xml(data, tree);
        resp->write(data.str());
        resp->close_connection_after_response = true;
      });

      tree.put("root.<xmlattr>.status_code"s, 401);
      tree.put("root.<xmlattr>.query"s, req->path);
      tree.put("root.<xmlattr>.status_message"s, "The client is not authorized. Certificate verification failed."s);
    };

    https_server.default_resource["GET"] = not_found<SunshineHTTPS>;
    https_server.resource["^/serverinfo$"]["GET"] = serverinfo<SunshineHTTPS>;
    https_server.resource["^/pair$"]["GET"] = pair<SunshineHTTPS>;
    https_server.resource["^/applist$"]["GET"] = applist;
    https_server.resource["^/ligase/v1/sync$"]["GET"] = ligase_sync;
    https_server.resource["^/ligase/v1/streaming$"]["POST"] = ligase_update_streaming;
    https_server.resource["^/ligase/v1/library/sort$"]["POST"] = ligase_update_library_sort;
    https_server.resource["^/appasset$"]["GET"] = appasset;
    https_server.resource["^/launch$"]["GET"] = [&host_audio](auto resp, auto req) {
      launch(host_audio, resp, req);
    };
    https_server.resource["^/resume$"]["GET"] = [&host_audio](auto resp, auto req) {
      resume(host_audio, resp, req);
    };
    https_server.resource["^/cancel$"]["GET"] = cancel;
    https_server.resource["^/actions/clipboard$"]["GET"] = getClipboard;
    https_server.resource["^/actions/clipboard$"]["POST"] = setClipboard;

    https_server.config.reuse_address = true;
    https_server.config.address = net::af_to_any_address_string(address_family);
    https_server.config.port = port_https;

    http_server.default_resource["GET"] = not_found<SimpleWeb::HTTP>;
    http_server.resource["^/serverinfo$"]["GET"] = serverinfo<SimpleWeb::HTTP>;
    http_server.resource["^/pair$"]["GET"] = pair<SimpleWeb::HTTP>;
    http_server.resource["^/ligase/v1/devices$"]["GET"] = ligase_devices_local;
    http_server.resource[
      "^/ligase/v1/devices/[0-9a-f-]+/access$"]["PUT"] =
      ligase_device_access_local;
    http_server.resource[
      "^/ligase/v1/devices/[0-9a-f-]+$"]["DELETE"] =
      ligase_device_delete_local;
    http_server.resource[
      "^/ligase/v1/devices/[0-9a-f-]+/end-session-and-delete$"]["POST"] =
      ligase_device_delete_local;
    http_server.resource["^/ligase/v1/session/cancel$"]["POST"] = ligase_cancel_session_local;
    http_server.resource["^/ligase/v1/authority/readback$"]["POST"] = ligase_authority_readback_local;
    http_server.resource["^/ligase/v1/authority/reload$"]["POST"] = ligase_authority_reload_local;
    if (attended_enabled) {
      http_server.resource["^/ligase/v1/pairing/requests$"]["POST"] = attended_route;
      http_server.resource["^/ligase/v1/pairing/requests$"]["GET"] = attended_route;
      http_server.resource["^/ligase/v1/pairing/requests/[^/]+$"]["GET"] = attended_route;
      http_server.resource["^/ligase/v1/pairing/requests/[^/]+$"]["DELETE"] = attended_route;
      http_server.resource["^/ligase/v1/pairing/requests/[^/]+/envelope$"]["PUT"] = attended_route;
      http_server.resource["^/ligase/v1/pairing/requests/[^/]+/allow$"]["POST"] = attended_route;
      http_server.resource["^/ligase/v1/pairing/requests/[^/]+/reject$"]["POST"] = attended_route;
      http_server.resource[
        "^/ligase/v1/pairing/requests/[0-9a-f-]+/access$"]["PUT"] =
        ligase_pairing_access_local;
    }

    http_server.config.reuse_address = true;
    http_server.config.address = net::af_to_any_address_string(address_family);
    http_server.config.port = port_http;

    auto accept_and_run = [&](auto *http_server) {
      try {
        http_server->start();
      } catch (boost::system::system_error &err) {
        // It's possible the exception gets thrown after calling http_server->stop() from a different thread
        if (shutdown_event->peek()) {
          return;
        }

        BOOST_LOG(fatal) << "Couldn't start http server on ports ["sv << port_https << ", "sv << port_https << "]: "sv << err.what();
        shutdown_event->raise(true);
        return;
      }
    };
    std::thread ssl {accept_and_run, &https_server};
    std::thread tcp {accept_and_run, &http_server};

    // Wait for any event
    shutdown_event->view();

    if (attended_pairing_service) attended_pairing_service->clear_for_restart();
    attended_pairing_router.reset();
    attended_pairing_service.reset();
    attended_legacy_adapters.clear();
    map_id_sess.clear();

    https_server.stop();
    http_server.stop();

    ssl.join();
    tcp.join();
  }

  std::string request_otp(const std::string& passphrase, const std::string& deviceName) {
    if (passphrase.size() < 4) {
      return "";
    }

    one_time_pin = crypto::rand_alphabet(4, "0123456789"sv);
    otp_passphrase = passphrase;
    otp_device_name = deviceName;
    otp_creation_time = std::chrono::steady_clock::now();

    return one_time_pin;
  }

  void
  erase_all_clients() {
    client_t client;
    client_root = client;
    cert_chain.clear();
    save_state();
    load_state();
  }

  void stop_session(stream::session_t& session, bool graceful) {
    if (graceful) {
      stream::session::graceful_stop(session);
    } else {
      stream::session::stop(session);
    }
  }

  bool find_and_stop_session(const std::string& uuid, bool graceful) {
    auto session = rtsp_stream::find_session(uuid);
    if (session) {
      stop_session(*session, graceful);
      return true;
    }
    return false;
  }

  void update_session_info(stream::session_t& session, const std::string& name, const crypto::PERM newPerm) {
    stream::session::update_device_info(session, name, newPerm);
  }

  bool find_and_udpate_session_info(const std::string& uuid, const std::string& name, const crypto::PERM newPerm) {
    auto session = rtsp_stream::find_session(uuid);
    if (session) {
      update_session_info(*session, name, newPerm);
      return true;
    }
    return false;
  }

  bool update_device_info(
    const std::string& uuid,
    const std::string& name,
    const std::string& display_mode,
    const cmd_list_t& do_cmds,
    const cmd_list_t& undo_cmds,
    const crypto::PERM newPerm,
    const bool enable_legacy_ordering,
    const bool allow_client_commands,
    const bool always_use_virtual_display
  ) {
    find_and_udpate_session_info(uuid, name, newPerm);

    client_t &client = client_root;
    auto it = client.named_devices.begin();
    for (; it != client.named_devices.end(); ++it) {
      auto named_cert_p = *it;
      if (named_cert_p->uuid == uuid) {
        named_cert_p->name = name;
        named_cert_p->display_mode = display_mode;
        named_cert_p->perm = newPerm;
        named_cert_p->do_cmds = do_cmds;
        named_cert_p->undo_cmds = undo_cmds;
        named_cert_p->enable_legacy_ordering = enable_legacy_ordering;
        named_cert_p->allow_client_commands = allow_client_commands;
        named_cert_p->always_use_virtual_display = always_use_virtual_display;
        save_state();
        return true;
      }
    }

    return false;
  }

  bool unpair_client(const std::string_view uuid) {
    bool removed = false;
    client_t &client = client_root;
    for (auto it = client.named_devices.begin(); it != client.named_devices.end();) {
      if ((*it)->uuid == uuid) {
        it = client.named_devices.erase(it);
        removed = true;
      } else {
        ++it;
      }
    }

    save_state();
    load_state();

    if (removed) {
      auto session = rtsp_stream::find_session(uuid);
      if (session) {
        stop_session(*session, true);
      }

      if (client.named_devices.empty()) {
        proc::proc.terminate();
      }
    }

    return removed;
  }
}  // namespace nvhttp
