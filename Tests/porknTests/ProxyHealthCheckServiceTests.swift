import XCTest

@testable import porkn

final class ProxyHealthCheckServiceTests: XCTestCase {
  func testLocalProxyListeningDetection() throws {
    let listener = try TCPTestListener(port: 0)
    let service = ProxyHealthCheckService(timeout: 0.2)

    XCTAssertTrue(service.isLocalProxyListening(host: "127.0.0.1", port: listener.port))
    XCTAssertFalse(service.isLocalProxyListening(host: "127.0.0.1", port: listener.port + 1))
  }

  func testHealthStatusPresentation() {
    XCTAssertTrue(ProxyHealthStatus.protected(proxyIP: "1.2.3.4").isHealthy)
    XCTAssertFalse(ProxyHealthStatus.localProxyFailed("no listener").isHealthy)
    XCTAssertEqual(ProxyHealthStatus.protected(proxyIP: "1.2.3.4").detail, "Proxy IP: 1.2.3.4")
  }
}
