import Foundation

enum PorknManagedProxy {
  static let host = "127.0.0.1"
  static let port = 2080
}

struct ProxyServiceState: Codable, Equatable {
  var serviceName: String
  var web: ProxyState
  var secureWeb: ProxyState
  var socks: ProxyState
}

struct ProxyState: Codable, Equatable {
  var enabled: Bool
  var server: String
  var port: Int
  var authenticated: Bool

  func isManagedporknProxy(
    host: String = PorknManagedProxy.host, port: Int = PorknManagedProxy.port
  ) -> Bool {
    enabled && server == host && self.port == port
  }
}

struct SystemProxySnapshot: Codable, Equatable {
  var createdAt: Date
  var services: [ProxyServiceState]
}

@MainActor
final class SystemProxyManager {
  enum ProxyError: LocalizedError {
    case noNetworkServices
    case commandFailed(String)

    var errorDescription: String? {
      switch self {
      case .noNetworkServices:
        "Не нашёл активные сетевые сервисы macOS для настройки proxy"
      case .commandFailed(let message):
        message.trimmingCharacters(in: .whitespacesAndNewlines)
      }
    }
  }

  private let snapshotURL: URL

  init(snapshotURL: URL? = nil) {
    self.snapshotURL = snapshotURL ?? Self.defaultSnapshotURL
  }

  func enableSystemProxy(
    host: String = PorknManagedProxy.host, port: Int = PorknManagedProxy.port
  ) throws -> [String] {
    // If the previous app run crashed, first return macOS to a clean state.
    // This avoids saving an already-orphaned 127.0.0.1:2080 proxy as the new baseline.
    if FileManager.default.fileExists(atPath: snapshotURL.path) {
      _ = try restoreSystemProxy()
    } else {
      _ = try disableManagedProxyIfOrphaned(host: host, port: port)
    }

    let services = try activeNetworkServicesForProxy()
    guard !services.isEmpty else { throw ProxyError.noNetworkServices }

    let snapshot = SystemProxySnapshot(
      createdAt: Date(),
      services: try services.map { service in
        ProxyServiceState(
          serviceName: service,
          web: try readProxy(kind: .web, service: service),
          secureWeb: try readProxy(kind: .secureWeb, service: service),
          socks: try readProxy(kind: .socks, service: service)
        )
      }
    )
    try save(snapshot)

    for service in services {
      _ = try runNetworkSetup(["-setwebproxy", service, host, String(port), "off"])
      _ = try runNetworkSetup(["-setsecurewebproxy", service, host, String(port), "off"])
      _ = try runNetworkSetup(["-setsocksfirewallproxy", service, host, String(port), "off"])
      _ = try runNetworkSetup(["-setwebproxystate", service, "on"])
      _ = try runNetworkSetup(["-setsecurewebproxystate", service, "on"])
      _ = try runNetworkSetup(["-setsocksfirewallproxystate", service, "on"])
    }

    return services
  }

  @discardableResult
  func restoreSystemProxy() throws -> [String] {
    guard FileManager.default.fileExists(atPath: snapshotURL.path) else {
      return try disableManagedProxyIfOrphaned()
    }
    let data = try Data(contentsOf: snapshotURL)
    let snapshot = try JSONDecoder.porknProxy.decode(SystemProxySnapshot.self, from: data)

    for service in snapshot.services {
      try apply(service.web, kind: .web, service: service.serviceName)
      try apply(service.secureWeb, kind: .secureWeb, service: service.serviceName)
      try apply(service.socks, kind: .socks, service: service.serviceName)
    }

    try? FileManager.default.removeItem(at: snapshotURL)
    return snapshot.services.map(\.serviceName)
  }

  /// Fail-safe cleanup for the exact proxy endpoint owned by porkn.
  /// It deliberately does not touch VPN services/routes/DNS or arbitrary user proxies.
  @discardableResult
  func disableManagedProxyIfOrphaned(
    host: String = PorknManagedProxy.host, port: Int = PorknManagedProxy.port
  ) throws -> [String] {
    let services = try activeNetworkServicesForProxy()
    var changedServices: [String] = []

    for service in services {
      var changed = false
      let web = try readProxy(kind: .web, service: service)
      let secureWeb = try readProxy(kind: .secureWeb, service: service)
      let socks = try readProxy(kind: .socks, service: service)

      if web.isManagedporknProxy(host: host, port: port) {
        _ = try runNetworkSetup(["-setwebproxystate", service, "off"])
        changed = true
      }
      if secureWeb.isManagedporknProxy(host: host, port: port) {
        _ = try runNetworkSetup(["-setsecurewebproxystate", service, "off"])
        changed = true
      }
      if socks.isManagedporknProxy(host: host, port: port) {
        _ = try runNetworkSetup(["-setsocksfirewallproxystate", service, "off"])
        changed = true
      }

      if changed {
        changedServices.append(service)
      }
    }

    return changedServices
  }

  private func apply(_ state: ProxyState, kind: ProxyKind, service: String) throws {
    switch kind {
    case .web:
      _ = try runNetworkSetup([
        "-setwebproxy", service, state.server, String(state.port),
        state.authenticated ? "on" : "off",
      ])
      _ = try runNetworkSetup(["-setwebproxystate", service, state.enabled ? "on" : "off"])
    case .secureWeb:
      _ = try runNetworkSetup([
        "-setsecurewebproxy", service, state.server, String(state.port),
        state.authenticated ? "on" : "off",
      ])
      _ = try runNetworkSetup(["-setsecurewebproxystate", service, state.enabled ? "on" : "off"])
    case .socks:
      _ = try runNetworkSetup([
        "-setsocksfirewallproxy", service, state.server, String(state.port),
        state.authenticated ? "on" : "off",
      ])
      _ = try runNetworkSetup([
        "-setsocksfirewallproxystate", service, state.enabled ? "on" : "off",
      ])
    }
  }

  private func activeNetworkServicesForProxy() throws -> [String] {
    let output = try runNetworkSetup(["-listallnetworkservices"])
    return
      output
      .components(separatedBy: .newlines)
      .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
      .filter { !$0.isEmpty }
      .filter { !$0.hasPrefix("An asterisk") }
      .filter { !$0.hasPrefix("*") }
      .filter { service in
        let lowered = service.lowercased()
        let skippedMarkers = ["vpn", "tailscale", "wireguard", "wg-", "v2raytun", "ziti"]
        return !skippedMarkers.contains { lowered.contains($0) }
      }
  }

  private func readProxy(kind: ProxyKind, service: String) throws -> ProxyState {
    let output: String
    switch kind {
    case .web:
      output = try runNetworkSetup(["-getwebproxy", service])
    case .secureWeb:
      output = try runNetworkSetup(["-getsecurewebproxy", service])
    case .socks:
      output = try runNetworkSetup(["-getsocksfirewallproxy", service])
    }

    var enabled = false
    var server = ""
    var port = 0
    var authenticated = false

    for line in output.components(separatedBy: .newlines) {
      let parts = line.split(separator: ":", maxSplits: 1).map {
        $0.trimmingCharacters(in: .whitespacesAndNewlines)
      }
      guard parts.count == 2 else { continue }
      switch parts[0] {
      case "Enabled": enabled = parts[1].lowercased() == "yes"
      case "Server": server = parts[1]
      case "Port": port = Int(parts[1]) ?? 0
      case "Authenticated Proxy Enabled":
        authenticated = parts[1] == "1" || parts[1].lowercased() == "yes"
      default: break
      }
    }

    return ProxyState(enabled: enabled, server: server, port: port, authenticated: authenticated)
  }

  private func runNetworkSetup(_ arguments: [String]) throws -> String {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/sbin/networksetup")
    process.arguments = arguments

    let stdout = Pipe()
    let stderr = Pipe()
    process.standardOutput = stdout
    process.standardError = stderr

    try process.run()
    process.waitUntilExit()

    let out = String(data: stdout.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
    let err = String(data: stderr.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""

    guard process.terminationStatus == 0 else {
      throw ProxyError.commandFailed(err.isEmpty ? out : err)
    }
    return out
  }

  private func save(_ snapshot: SystemProxySnapshot) throws {
    try FileManager.default.createDirectory(
      at: snapshotURL.deletingLastPathComponent(), withIntermediateDirectories: true)
    let data = try JSONEncoder.porknProxy.encode(snapshot)
    try data.write(to: snapshotURL, options: .atomic)
  }

  private enum ProxyKind {
    case web
    case secureWeb
    case socks
  }

  private static var defaultSnapshotURL: URL {
    FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
      .appendingPathComponent("porkn", isDirectory: true)
      .appendingPathComponent("Runtime", isDirectory: true)
      .appendingPathComponent("system-proxy-snapshot.json")
  }
}

extension JSONEncoder {
  fileprivate static var porknProxy: JSONEncoder {
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    encoder.dateEncodingStrategy = .iso8601
    return encoder
  }
}

extension JSONDecoder {
  fileprivate static var porknProxy: JSONDecoder {
    let decoder = JSONDecoder()
    decoder.dateDecodingStrategy = .iso8601
    return decoder
  }
}
