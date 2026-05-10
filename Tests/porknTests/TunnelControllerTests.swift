import XCTest

@testable import porkn

@MainActor
final class TunnelControllerTests: XCTestCase {
  func testSystemTunModeFailsBeforeStartingRuntime() async throws {
    let profile = try ConfigParser().parseOne(
      "vless://00000000-0000-0000-0000-000000000000@example.com:443?security=reality&type=tcp#VLESS"
    )
    let controller = TunnelController()

    await controller.connect(profile, mode: .systemTun)

    guard case .failed(let message) = controller.state else {
      return XCTFail("Expected unavailable Full VPN/TUN to fail before runtime start")
    }
    XCTAssertTrue(message.contains("NetworkExtension") || message.contains("недоступен"))
    XCTAssertNil(controller.runtimeInfo)
    XCTAssertTrue(controller.proxiedServices.isEmpty)
  }
}
