import Foundation

struct NetworkExtensionSupport {
  enum Status: Equatable {
    case unavailable(reason: String)
    case available
  }

  static let appGroupIdentifier = "group.app.porkn.shared"
  static let providerBundleIdentifier = "app.porkn.desktop.PacketTunnelProvider"

  static var status: Status {
    #if DEBUG_NETWORK_EXTENSION_AVAILABLE
      return .available
    #else
      return .unavailable(
        reason:
          "Full VPN/TUN requires a signed PacketTunnelProvider target with NetworkExtension packet-tunnel-provider entitlement. This build keeps system routes unchanged."
      )
    #endif
  }

  static var isAvailable: Bool {
    if case .available = status { return true }
    return false
  }

  static var unavailableReason: String? {
    if case .unavailable(let reason) = status { return reason }
    return nil
  }
}

struct PacketTunnelConfigHandoff: Codable, Equatable {
  var profileID: UUID
  var profileName: String
  var generatedConfig: String
  var generatedAt: Date

  static let fileName = "packet-tunnel-sing-box.json"

  func write(to directory: URL) throws -> URL {
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let url = directory.appendingPathComponent(Self.fileName)
    try JSONEncoder.packetTunnel.encode(self).write(to: url, options: .atomic)
    return url
  }
}

extension JSONEncoder {
  fileprivate static var packetTunnel: JSONEncoder {
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    encoder.dateEncodingStrategy = .iso8601
    return encoder
  }
}
