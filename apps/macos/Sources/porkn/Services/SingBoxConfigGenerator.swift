import Foundation

struct SingBoxConfigGenerator {
  enum GeneratorError: LocalizedError {
    case missingPort(TunnelProfile)
    case unsupportedProtocol(ProxyProtocol)
    case missingUserID(TunnelProfile)

    var errorDescription: String? {
      switch self {
      case .missingPort(let profile): "Для \(profile.name) не указан порт"
      case .unsupportedProtocol(let proto):
        "Генератор sing-box пока не поддерживает \(proto.displayName)"
      case .missingUserID(let profile): "Для \(profile.name) не найден UUID/user id"
      }
    }
  }

  func generate(
    profile: TunnelProfile, mode: RoutingMode, routingSettings: RoutingSettings = .current,
    localProxyPort: Int = PorknManagedProxy.defaultPort, chainEntryProfile: TunnelProfile? = nil
  ) throws -> String {
    let object = try generateObject(
      profile: profile, mode: mode, routingSettings: routingSettings, localProxyPort: localProxyPort,
      chainEntryProfile: chainEntryProfile
    )
    let data = try JSONSerialization.data(
      withJSONObject: object, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
    return String(data: data, encoding: .utf8) ?? "{}"
  }

  func generateObject(
    profile: TunnelProfile, mode: RoutingMode, routingSettings: RoutingSettings = .current,
    localProxyPort: Int = PorknManagedProxy.defaultPort, chainEntryProfile: TunnelProfile? = nil
  ) throws -> [String: Any] {
    let routeRules = routeRules(mode: mode, routingSettings: routingSettings)
    let outbounds = try outbounds(profile: profile, chainEntryProfile: chainEntryProfile)

    return [
      "log": [
        "level": "info",
        "timestamp": true,
      ],
      "dns": [
        "servers": [
          [
            "type": "https",
            "tag": "cloudflare",
            "server": "1.1.1.1",
            "server_port": 443,
            "path": "/dns-query",
            "tls": [
              "server_name": "cloudflare-dns.com"
            ],
          ],
          [
            "type": "local",
            "tag": "local",
          ],
        ],
        "final": "cloudflare",
      ],
      "inbounds": [inbound(mode: mode, localProxyPort: localProxyPort)],
      "outbounds": outbounds,
      "route": [
        "auto_detect_interface": true,
        "default_domain_resolver": [
          "server": "local",
          "strategy": "prefer_ipv4",
        ],
        "rules": routeRules,
        "final": "proxy-out",
      ],
    ]
  }

  private func routeRules(mode: RoutingMode, routingSettings: RoutingSettings) -> [[String: Any]] {
    var rules: [[String: Any]] = [
      [
        "inbound": inboundTag(mode: mode),
        "action": "sniff",
        "timeout": "1s",
      ]
    ]

    rules.append(contentsOf: routingSettings.routeRules)

    return rules
  }

  private func inboundTag(mode: RoutingMode) -> String {
    switch mode {
    case .localProxy: "mixed-in"
    case .systemTun: "tun-in"
    }
  }

  private func inbound(mode: RoutingMode, localProxyPort: Int) -> [String: Any] {
    switch mode {
    case .localProxy:
      return [
        "type": "mixed",
        "tag": "mixed-in",
        "listen": "127.0.0.1",
        "listen_port": localProxyPort,
      ]
    case .systemTun:
      return [
        "type": "tun",
        "tag": "tun-in",
        "interface_name": "porkn0",
        "address": ["172.19.0.1/30"],
        "auto_route": true,
        "strict_route": true,
        "stack": "system",
        "mtu": 9000,
      ]
    }
  }

  private func outbounds(profile: TunnelProfile, chainEntryProfile: TunnelProfile?) throws -> [[String: Any]] {
    var result: [[String: Any]] = []
    if let chainEntryProfile {
      result.append(try outbound(profile: chainEntryProfile, tag: "chain-entry"))
      result.append(try outbound(profile: profile, tag: "proxy-out", detour: "chain-entry"))
    } else {
      result.append(try outbound(profile: profile, tag: "proxy-out"))
    }
    result.append(["type": "direct", "tag": "direct"])
    result.append(["type": "block", "tag": "block"])
    return result
  }

  private func outbound(profile: TunnelProfile, tag: String, detour: String? = nil) throws -> [String: Any] {
    guard let port = profile.serverPort else { throw GeneratorError.missingPort(profile) }

    switch profile.proto {
    case .vless:
      guard let uuid = profile.primaryUser, !uuid.isEmpty else {
        throw GeneratorError.missingUserID(profile)
      }
      var object: [String: Any] = [
        "type": "vless",
        "tag": tag,
        "server": profile.serverHost,
        "server_port": port,
        "uuid": uuid,
        "network": profile.queryItems["network"] ?? "tcp",
      ]
      if let flow = profile.queryItems["flow"], !flow.isEmpty {
        object["flow"] = flow
      }
      object["packet_encoding"] = profile.queryItems["packetEncoding"] ?? "xudp"
      if let tls = tlsObject(profile: profile) {
        object["tls"] = tls
      }
      if let transport = transportObject(profile: profile) {
        object["transport"] = transport
      }
      if let detour { object["detour"] = detour }
      return object

    case .socks:
      var object: [String: Any] = [
        "type": "socks",
        "tag": tag,
        "server": profile.serverHost,
        "server_port": port,
        "version": profile.queryItems["version"] ?? "5",
        "network": "tcp",
      ]
      if let username = profile.primaryUser, !username.isEmpty { object["username"] = username }
      if let password = profile.secret, !password.isEmpty { object["password"] = password }
      if let detour { object["detour"] = detour }
      return object

    case .trojan:
      guard let password = profile.primaryUser, !password.isEmpty else {
        throw GeneratorError.missingUserID(profile)
      }
      var object: [String: Any] = [
        "type": "trojan",
        "tag": tag,
        "server": profile.serverHost,
        "server_port": port,
        "password": password,
      ]
      if let tls = tlsObject(profile: profile, defaultEnabled: true) {
        object["tls"] = tls
      }
      if let detour { object["detour"] = detour }
      return object

    default:
      throw GeneratorError.unsupportedProtocol(profile.proto)
    }
  }

  private func tlsObject(profile: TunnelProfile, defaultEnabled: Bool = false) -> [String: Any]? {
    let security = profile.queryItems["security"] ?? profile.queryItems["tls"]
    let enabled = defaultEnabled || security == "tls" || security == "reality"
    guard enabled else { return nil }

    var tls: [String: Any] = [
      "enabled": true,
      "server_name": profile.queryItems["sni"] ?? profile.queryItems["peer"] ?? profile.serverHost,
    ]

    if let fingerprint = profile.queryItems["fp"], !fingerprint.isEmpty {
      tls["utls"] = ["enabled": true, "fingerprint": fingerprint]
    }

    if security == "reality" {
      var reality: [String: Any] = ["enabled": true]
      if let publicKey = profile.queryItems["pbk"] { reality["public_key"] = publicKey }
      if let shortID = profile.queryItems["sid"] { reality["short_id"] = shortID }
      tls["reality"] = reality
    }

    if profile.queryItems["allowInsecure"] == "1" || profile.queryItems["insecure"] == "1" {
      tls["insecure"] = true
    }

    return tls
  }

  private func transportObject(profile: TunnelProfile) -> [String: Any]? {
    let type = profile.queryItems["type"] ?? profile.queryItems["net"]
    guard let type, !type.isEmpty, type != "tcp" else { return nil }

    var object: [String: Any] = ["type": type]
    if let path = profile.queryItems["path"], !path.isEmpty { object["path"] = path }
    if let host = profile.queryItems["host"], !host.isEmpty {
      object["headers"] = ["Host": host]
    }
    if let serviceName = profile.queryItems["serviceName"], !serviceName.isEmpty {
      object["service_name"] = serviceName
    }
    return object
  }
}
