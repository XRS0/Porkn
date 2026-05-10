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
  var isFavorite: Bool
  var lastUsedAt: Date?

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
    lastPingMilliseconds: Int? = nil,
    isFavorite: Bool = false,
    lastUsedAt: Date? = nil
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
    self.isFavorite = isFavorite
    self.lastUsedAt = lastUsedAt
  }

  enum CodingKeys: String, CodingKey {
    case id
    case name
    case proto
    case serverHost
    case serverPort
    case rawConfig
    case username
    case password
    case queryItems
    case subscriptionID
    case subscriptionKey
    case createdAt
    case lastPingMilliseconds
    case isFavorite
    case lastUsedAt
  }

  init(from decoder: Decoder) throws {
    let container = try decoder.container(keyedBy: CodingKeys.self)
    id = try container.decode(UUID.self, forKey: .id)
    name = try container.decode(String.self, forKey: .name)
    proto = try container.decode(ProxyProtocol.self, forKey: .proto)
    serverHost = try container.decode(String.self, forKey: .serverHost)
    serverPort = try container.decodeIfPresent(Int.self, forKey: .serverPort)
    rawConfig = try container.decode(String.self, forKey: .rawConfig)
    username = try container.decodeIfPresent(String.self, forKey: .username)
    password = try container.decodeIfPresent(String.self, forKey: .password)
    queryItems = try container.decodeIfPresent([String: String].self, forKey: .queryItems) ?? [:]
    subscriptionID = try container.decodeIfPresent(UUID.self, forKey: .subscriptionID)
    subscriptionKey = try container.decodeIfPresent(String.self, forKey: .subscriptionKey)
    createdAt = try container.decodeIfPresent(Date.self, forKey: .createdAt) ?? Date()
    lastPingMilliseconds = try container.decodeIfPresent(Int.self, forKey: .lastPingMilliseconds)
    isFavorite = try container.decodeIfPresent(Bool.self, forKey: .isFavorite) ?? false
    lastUsedAt = try container.decodeIfPresent(Date.self, forKey: .lastUsedAt)
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
