import XCTest

@testable import porkn

final class NetworkExtensionSupportTests: XCTestCase {
  func testTunConfigContainsPacketTunnelReadyInbound() throws {
    let profile = try ConfigParser().parseOne(
      "vless://uuid@example.com:443?security=reality&type=tcp&sni=example.com&fp=chrome&pbk=public&sid=short#VLESS"
    )
    let json = try SingBoxConfigGenerator().generate(profile: profile, mode: .systemTun)

    XCTAssertTrue(json.contains("\"type\" : \"tun\""))
    XCTAssertTrue(json.contains("\"tag\" : \"tun-in\""))
    XCTAssertTrue(json.contains("\"auto_route\" : true"))
    XCTAssertTrue(json.contains("\"strict_route\" : true"))
    XCTAssertTrue(json.contains("\"stack\" : \"system\""))
  }

  func testNetworkExtensionUnavailableByDefault() {
    XCTAssertFalse(NetworkExtensionSupport.isAvailable)
    XCTAssertTrue(NetworkExtensionSupport.unavailableReason?.contains("NetworkExtension") == true)
  }

  func testPacketTunnelConfigHandoffWritesJSON() throws {
    let directory = FileManager.default.temporaryDirectory
      .appendingPathComponent("porkn-handoff-\(UUID().uuidString)", isDirectory: true)
    let handoff = PacketTunnelConfigHandoff(
      profileID: UUID(), profileName: "Test", generatedConfig: "{}", generatedAt: Date())

    let url = try handoff.write(to: directory)
    XCTAssertEqual(url.lastPathComponent, PacketTunnelConfigHandoff.fileName)
    XCTAssertTrue(FileManager.default.fileExists(atPath: url.path))
  }
}
