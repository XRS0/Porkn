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
    let json = try SingBoxConfigGenerator().generate(profile: profile, mode: .localProxy)

    XCTAssertTrue(json.contains("\"type\" : \"mixed\""))
    XCTAssertTrue(json.contains("\"type\" : \"socks\""))
    XCTAssertTrue(json.contains("\"username\" : \"user\""))
  }
}
