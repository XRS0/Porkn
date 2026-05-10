import XCTest

@testable import porkn

final class SystemProxyManagerTests: XCTestCase {
  func testManagedProxyDetectionOnlyMatchesEnabledPorknEndpoint() {
    XCTAssertTrue(
      ProxyState(enabled: true, server: "127.0.0.1", port: 2080, authenticated: false)
        .isManagedPorknProxy()
    )
    XCTAssertFalse(
      ProxyState(enabled: false, server: "127.0.0.1", port: 2080, authenticated: false)
        .isManagedPorknProxy()
    )
    XCTAssertTrue(
      ProxyState(enabled: true, server: "127.0.0.1", port: 2081, authenticated: false)
        .isManagedPorknProxy()
    )
    XCTAssertFalse(
      ProxyState(enabled: true, server: "127.0.0.1", port: 2091, authenticated: false)
        .isManagedPorknProxy()
    )
    XCTAssertFalse(
      ProxyState(enabled: true, server: "proxy.example.com", port: 2080, authenticated: false)
        .isManagedPorknProxy()
    )
  }
}
