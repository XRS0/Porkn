import Foundation

struct ConfigParser {
  enum ParserError: LocalizedError, Equatable {
    case emptyInput
    case unsupportedFormat(String)
    case invalidVMessPayload
    case invalidSubscriptionURL

    var errorDescription: String? {
      switch self {
      case .emptyInput: "Пустой конфиг"
      case .unsupportedFormat(let value): "Неподдерживаемый формат: \(value)"
      case .invalidVMessPayload: "Некорректный VMess payload"
      case .invalidSubscriptionURL: "Некорректный subscription URL"
      }
    }
  }

  func parsePayload(_ input: String) throws -> ImportPayload {
    let trimmed = input.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !trimmed.isEmpty else { throw ParserError.emptyInput }

    if let subscription = try parseSubscriptionIfNeeded(trimmed) {
      return .subscription(subscription)
    }

    return .profiles(try parseMany(trimmed))
  }

  func parseMany(_ input: String) throws -> [TunnelProfile] {
    let trimmed = input.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !trimmed.isEmpty else { throw ParserError.emptyInput }

    if let decodedSubscription = decodeBase64Subscription(trimmed) {
      return try parseMany(decodedSubscription)
    }

    let candidates =
      trimmed
      .components(separatedBy: CharacterSet.newlines)
      .flatMap { $0.components(separatedBy: "\r") }
      .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
      .filter { !$0.isEmpty && !$0.hasPrefix("#") }

    if candidates.count > 1 {
      return try candidates.map(parseOne)
    }

    return [try parseOne(trimmed)]
  }

  func parseOne(_ raw: String) throws -> TunnelProfile {
    let raw = raw.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !raw.isEmpty else { throw ParserError.emptyInput }

    if raw.lowercased().hasPrefix("vmess://") {
      return try parseVMess(raw)
    }

    guard let components = URLComponents(string: raw), let scheme = components.scheme?.lowercased()
    else {
      throw ParserError.unsupportedFormat(raw.prefixDescription)
    }

    let proto =
      ProxyProtocol(rawValue: scheme) ?? (scheme == "shadowsocks" ? .shadowsocks : .unknown)
    guard proto != .unknown else { throw ParserError.unsupportedFormat(scheme) }

    let query = Dictionary(
      uniqueKeysWithValues: components.queryItems?.compactMap { item in
        item.value.map { (item.name, $0) }
      } ?? [])
    let host = components.host ?? hostFromOpaqueURL(raw) ?? "unknown"
    let port = components.port ?? portFromOpaqueURL(raw)
    let name = decodedFragment(components.fragment) ?? defaultName(for: proto, host: host)

    return TunnelProfile(
      name: name,
      proto: proto,
      serverHost: host,
      serverPort: port,
      rawConfig: raw,
      username: components.user,
      password: components.password,
      queryItems: query
    )
  }

  private func parseSubscriptionIfNeeded(_ raw: String) throws -> Subscription? {
    guard let components = URLComponents(string: raw),
      let scheme = components.scheme?.lowercased(),
      ["http", "https"].contains(scheme)
    else {
      return nil
    }
    guard let url = components.url, let host = components.host else {
      throw ParserError.invalidSubscriptionURL
    }
    let name = decodedFragment(components.fragment) ?? "Subscription · \(host)"
    return Subscription(name: name, url: url)
  }

  private func parseVMess(_ raw: String) throws -> TunnelProfile {
    let payload = String(raw.dropFirst("vmess://".count))
    guard let data = Data(base64URLOrStandardEncoded: payload),
      let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
    else {
      throw ParserError.invalidVMessPayload
    }

    let host = object["add"] as? String ?? object["host"] as? String ?? "unknown"
    let portString = object["port"] as? String
    let portNumber = object["port"] as? Int
    let name = object["ps"] as? String ?? defaultName(for: .vmess, host: host)

    var query: [String: String] = [:]
    for (key, value) in object {
      if let text = value as? String { query[key] = text }
      if let number = value as? Int { query[key] = String(number) }
    }

    return TunnelProfile(
      name: name,
      proto: .vmess,
      serverHost: host,
      serverPort: portNumber ?? portString.flatMap(Int.init),
      rawConfig: raw,
      username: object["id"] as? String,
      password: object["aid"] as? String,
      queryItems: query
    )
  }

  private func decodeBase64Subscription(_ raw: String) -> String? {
    guard !raw.contains("://"), let data = Data(base64URLOrStandardEncoded: raw),
      let text = String(data: data, encoding: .utf8), text.contains("://")
    else {
      return nil
    }
    return text
  }

  private func decodedFragment(_ fragment: String?) -> String? {
    fragment?.removingPercentEncoding?.nilIfEmpty
  }

  private func defaultName(for proto: ProxyProtocol, host: String?) -> String {
    if let host, !host.isEmpty { return "\(proto.displayName) · \(host)" }
    return proto.displayName
  }

  private func hostFromOpaqueURL(_ raw: String) -> String? {
    guard let at = raw.lastIndex(of: "@") else { return nil }
    let afterAt = raw[raw.index(after: at)...]
    let hostPort = afterAt.split(separator: "?", maxSplits: 1).first?.split(
      separator: "#", maxSplits: 1
    ).first
    return hostPort?.split(separator: ":", maxSplits: 1).first.map(String.init)
  }

  private func portFromOpaqueURL(_ raw: String) -> Int? {
    guard let at = raw.lastIndex(of: "@") else { return nil }
    let afterAt = raw[raw.index(after: at)...]
    let hostPort = afterAt.split(separator: "?", maxSplits: 1).first?.split(
      separator: "#", maxSplits: 1
    ).first
    guard let portPart = hostPort?.split(separator: ":", maxSplits: 1).dropFirst().first else {
      return nil
    }
    return Int(portPart)
  }
}

extension String {
  fileprivate var nilIfEmpty: String? { isEmpty ? nil : self }
  fileprivate var prefixDescription: String { String(prefix(64)) }
}

extension Data {
  fileprivate init?(base64URLOrStandardEncoded string: String) {
    var normalized = string.replacingOccurrences(of: "-", with: "+").replacingOccurrences(
      of: "_", with: "/")
    let padding = normalized.count % 4
    if padding > 0 { normalized += String(repeating: "=", count: 4 - padding) }
    self.init(base64Encoded: normalized)
  }
}
