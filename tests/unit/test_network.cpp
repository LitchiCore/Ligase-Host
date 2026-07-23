/**
 * @file tests/unit/test_network.cpp
 * @brief Test src/network.*
 */
#include "../tests_common.h"

#include <src/network.h>

struct MdnsInstanceNameTest: testing::TestWithParam<std::tuple<std::string, std::string>> {};

TEST_P(MdnsInstanceNameTest, Run) {
  auto [input, expected] = GetParam();
  ASSERT_EQ(net::mdns_instance_name(input), expected);
}

INSTANTIATE_TEST_SUITE_P(
  MdnsInstanceNameTests,
  MdnsInstanceNameTest,
  testing::Values(
    std::make_tuple("shortname-123", "shortname-123"),
    std::make_tuple("space 123", "space-123"),
    std::make_tuple("hostname.domain.test", "hostname"),
    std::make_tuple("&", "Sunshine"),
    std::make_tuple("", "Sunshine"),
    std::make_tuple("😁", "Sunshine"),
    std::make_tuple(std::string(128, 'a'), std::string(63, 'a'))
  )
);

TEST(NetworkAddressFamilyTest, ParsesConfiguredListenerMode) {
  EXPECT_EQ(net::af_from_enum_string("ipv4"), net::af_e::IPV4);
  EXPECT_EQ(net::af_from_enum_string("both"), net::af_e::BOTH);
  EXPECT_EQ(net::af_from_enum_string("unexpected"), net::af_e::BOTH);
  EXPECT_EQ(net::af_to_any_address_string(net::af_e::IPV4), "0.0.0.0");
  EXPECT_EQ(net::af_to_any_address_string(net::af_e::BOTH), "::");
}

TEST(NetworkAddressFormattingTest, BracketsIpv6UriAuthorities) {
  EXPECT_EQ(
    net::addr_to_url_escaped_string(boost::asio::ip::make_address("192.0.2.10")),
    "192.0.2.10"
  );
  EXPECT_EQ(
    net::addr_to_url_escaped_string(boost::asio::ip::make_address("2001:db8::1")),
    "[2001:db8::1]"
  );
  EXPECT_EQ(
    net::addr_to_url_escaped_string(boost::asio::ip::make_address("::1")),
    "[::1]"
  );
}
