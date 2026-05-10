import Foundation

struct TunnelProfile: Identifiable, Codable, Hashable {
  var id: UUID
  var name: String
  var proto: ProxyProtocol
  var serverHost: String
  var serverPort: Int?
  var rawConfig: String
  var username: String?
  var password: String?
  var queryItems: [String: String]
  var subscriptionID: UUID?
  var subscriptionKey: String?
  var createdAt: Date
  var lastPingMilliseconds: Int?

  init(
    id: UUID = UUID(),
    name: String,
    proto: ProxyProtocol,
    serverHost: String,
    serverPort: Int?,
    rawConfig: String,
    username: String? = nil,
    password: String? = nil,
    queryItems: [String: String] = [:],
    subscriptionID: UUID? = nil,
    subscriptionKey: String? = nil,
    createdAt: Date = Date(),
    lastPingMilliseconds: Int? = nil
  ) {
    self.id = id
    self.name = name
    self.proto = proto
    self.serverHost = serverHost
    self.serverPort = serverPort
    self.rawConfig = rawConfig
    self.username = username
    self.password = password
    self.queryItems = queryItems
    self.subscriptionID = subscriptionID
    self.subscriptionKey = subscriptionKey
    self.createdAt = createdAt
    self.lastPingMilliseconds = lastPingMilliseconds
  }

  var primaryUser: String? {
    username?.removingPercentEncoding
  }

  var secret: String? {
    password?.removingPercentEncoding
  }

  var stableKey: String {
    if let subscriptionKey, !subscriptionKey.isEmpty { return subscriptionKey }
    return Self.makeStableKey(
      proto: proto,
      serverHost: serverHost,
      serverPort: serverPort,
      username: username,
      rawConfig: rawConfig
    )
  }

  static func makeStableKey(
    proto: ProxyProtocol, serverHost: String, serverPort: Int?, username: String?, rawConfig: String
  ) -> String {
    let normalizedUser = username?.removingPercentEncoding?.lowercased() ?? ""
    let normalizedHost = serverHost.lowercased()
    let normalizedPort = serverPort.map(String.init) ?? ""
    if !normalizedHost.isEmpty, normalizedHost != "unknown" {
      return [proto.rawValue, normalizedUser, normalizedHost, normalizedPort].joined(separator: "|")
    }
    return [proto.rawValue, rawConfig.normalizedConfigForIdentity].joined(separator: "|")
  }

  var endpoint: String {
    if let serverPort {
      "\(serverHost):\(serverPort)"
    } else {
      serverHost
    }
  }
}

extension String {
  fileprivate var normalizedConfigForIdentity: String {
    trimmingCharacters(in: .whitespacesAndNewlines)
      .replacingOccurrences(of: #"#.*$"#, with: "", options: .regularExpression)
      .lowercased()
  }
}
