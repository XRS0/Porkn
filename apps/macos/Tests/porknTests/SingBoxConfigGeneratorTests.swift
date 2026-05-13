import XCTest

@testable import porkn

final class SingBoxConfigGeneratorTests: XCTestCase {
  func testGeneratesVLESSRealityTunConfig() throws {
    let profile = try ConfigParser().parseOne(
      "vless://uuid@example.com:443?security=reality&type=tcp&sni=example.com&fp=chrome&pbk=public&sid=short#VLESS"
    )
    let json = try SingBoxConfigGenerator().generate(profile: profile, mode: .systemTun)

    XCTAssertTrue(json.contains("\"type\" : \"tun\""))
    XCTAssertTrue(json.contains("\"type\" : \"vless\""))
    XCTAssertTrue(json.contains("\"reality\""))
    XCTAssertTrue(json.contains("\"public_key\" : \"public\""))
  }

  func testGeneratesSOCKSLocalProxyConfig() throws {
    let profile = try ConfigParser().parseOne("socks://user:pass@127.0.0.1:1080#Local")
    let json = try SingBoxConfigGenerator().generate(
      profile: profile, mode: .localProxy, localProxyPort: 2081)

    XCTAssertTrue(json.contains("\"type\" : \"mixed\""))
    XCTAssertTrue(json.contains("\"listen_port\" : 2081"))
    XCTAssertTrue(json.contains("\"type\" : \"socks\""))
    XCTAssertTrue(json.contains("\"username\" : \"user\""))
  }
  func testGeneratesChainedOutboundsWithDetour() throws {
    let entry = try ConfigParser().parseOne(
      "socks://127.0.0.1:1080#Entry")
    let exit = try ConfigParser().parseOne(
      "vless://uuid@example.com:443?security=reality&type=tcp&sni=example.com&fp=chrome&pbk=public&sid=short#Exit")

    let config = try SingBoxConfigGenerator().generateObject(
      profile: exit, mode: .localProxy, chainEntryProfile: entry)
    let outbounds = try XCTUnwrap(config["outbounds"] as? [[String: Any]])
    let entryOutbound: [String: Any] = try XCTUnwrap(outbounds.first { ($0["tag"] as? String) == "chain-entry" })
    let exitOutbound: [String: Any] = try XCTUnwrap(outbounds.first { ($0["tag"] as? String) == "proxy-out" })

    XCTAssertEqual(entryOutbound["type"] as? String, "socks")
    XCTAssertEqual(exitOutbound["type"] as? String, "vless")
    XCTAssertEqual(exitOutbound["detour"] as? String, "chain-entry")
  }

}
