import XCTest

@testable import porkn

final class ConfigParserTests: XCTestCase {
  func testParsesSubscriptionURL() throws {
    let payload = try ConfigParser().parsePayload("https://sub.example.com/api/token#Personal")
    guard case .subscription(let subscription) = payload else {
      return XCTFail("Expected subscription")
    }
    XCTAssertEqual(subscription.name, "Personal")
    XCTAssertEqual(subscription.url.host(), "sub.example.com")
  }

  func testParsesSOCKSURL() throws {
    let profile = try ConfigParser().parseOne("socks://user:pass@127.0.0.1:1080#Local")

    XCTAssertEqual(profile.proto, .socks)
    XCTAssertEqual(profile.name, "Local")
    XCTAssertEqual(profile.primaryUser, "user")
    XCTAssertEqual(profile.secret, "pass")
    XCTAssertEqual(profile.endpoint, "127.0.0.1:1080")
  }

  func testParsesVlessURL() throws {
    let profile = try ConfigParser().parseOne(
      "vless://00000000-0000-0000-0000-000000000000@example.com:443?security=reality&type=tcp&sni=example.com&fp=chrome&pbk=public&sid=short#Main%20Server"
    )

    XCTAssertEqual(profile.proto, .vless)
    XCTAssertEqual(profile.name, "Main Server")
    XCTAssertEqual(profile.serverHost, "example.com")
    XCTAssertEqual(profile.serverPort, 443)
    XCTAssertEqual(profile.primaryUser, "00000000-0000-0000-0000-000000000000")
    XCTAssertEqual(profile.queryItems["security"], "reality")
    XCTAssertEqual(profile.queryItems["type"], "tcp")
  }

  func testParsesTrojanURL() throws {
    let profile = try ConfigParser().parseOne(
      "trojan://password@vpn.example.org:443?security=tls#EU")

    XCTAssertEqual(profile.proto, .trojan)
    XCTAssertEqual(profile.name, "EU")
    XCTAssertEqual(profile.endpoint, "vpn.example.org:443")
  }

  func testParsesVMessBase64Payload() throws {
    let json =
      "{\"v\":\"2\",\"ps\":\"VMess Test\",\"add\":\"vmess.example.com\",\"port\":\"443\",\"id\":\"abc\",\"net\":\"ws\",\"tls\":\"tls\"}"
    let encoded = Data(json.utf8).base64EncodedString()
    let profile = try ConfigParser().parseOne("vmess://\(encoded)")

    XCTAssertEqual(profile.proto, .vmess)
    XCTAssertEqual(profile.name, "VMess Test")
    XCTAssertEqual(profile.serverHost, "vmess.example.com")
    XCTAssertEqual(profile.serverPort, 443)
    XCTAssertEqual(profile.primaryUser, "abc")
    XCTAssertEqual(profile.queryItems["net"], "ws")
  }

  func testParsesManyLines() throws {
    let profiles = try ConfigParser().parseMany(
      """
      vless://id@one.example.com:443#One
      trojan://pass@two.example.com:8443#Two
      """)

    XCTAssertEqual(profiles.count, 2)
    XCTAssertEqual(profiles.map(\.name), ["One", "Two"])
  }
}
