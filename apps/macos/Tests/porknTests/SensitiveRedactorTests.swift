import XCTest

@testable import porkn

final class SensitiveRedactorTests: XCTestCase {
  func testRedactsProxyUrlUserInfoAndUUID() {
    let raw = "vless://00000000-0000-0000-0000-000000000000@example.com:443?security=reality#Main"
    let redacted = SensitiveRedactor.redact(raw)

    XCTAssertFalse(redacted.contains("00000000-0000-0000-0000-000000000000"))
    XCTAssertTrue(redacted.contains("vless://••••@example.com:443"))
  }

  func testRedactsJsonSecretsAndSubscriptionTokens() {
    let raw = """
      {"uuid":"00000000-0000-0000-0000-000000000000","password":"secret"}
      https://sub.example.com/path?token=super-token&name=ok
      """
    let redacted = SensitiveRedactor.redact(raw)

    XCTAssertFalse(redacted.contains("super-token"))
    XCTAssertFalse(redacted.contains("secret"))
    XCTAssertFalse(redacted.contains("00000000-0000-0000-0000-000000000000"))
    XCTAssertTrue(redacted.contains("token=••••"))
    XCTAssertTrue(redacted.contains("\"password\":\"••••\""))
  }
}
