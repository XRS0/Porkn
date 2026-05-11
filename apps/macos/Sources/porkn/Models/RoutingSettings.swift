import Foundation

struct RoutingSettings: Codable, Equatable {
  static let presetStorageKey = "routing.preset"
  static let directDomainsStorageKey = "routing.directDomains"
  static let proxyDomainsStorageKey = "routing.proxyDomains"
  static let blockDomainsStorageKey = "routing.blockDomains"

  var preset: RoutingPreset
  var directDomainsText: String
  var proxyDomainsText: String
  var blockDomainsText: String

  init(
    preset: RoutingPreset = .directSelected,
    directDomainsText: String = Self.defaultDirectDomainsText,
    proxyDomainsText: String = "",
    blockDomainsText: String = ""
  ) {
    self.preset = preset
    self.directDomainsText = directDomainsText
    self.proxyDomainsText = proxyDomainsText
    self.blockDomainsText = blockDomainsText
  }

  static let defaultDirectDomainsText = """
    *.ru
    *.su
    """

  static var current: RoutingSettings {
    let presetRaw = UserDefaults.standard.string(forKey: presetStorageKey)
    let preset = RoutingPreset(rawValue: presetRaw ?? "") ?? .directRuSu
    let storedDirect = UserDefaults.standard.string(forKey: directDomainsStorageKey)
    let storedProxy = UserDefaults.standard.string(forKey: proxyDomainsStorageKey)
    let storedBlock = UserDefaults.standard.string(forKey: blockDomainsStorageKey)
    return RoutingSettings(
      preset: preset,
      directDomainsText: storedDirect ?? defaultDirectDomainsText,
      proxyDomainsText: storedProxy ?? "",
      blockDomainsText: storedBlock ?? ""
    )
  }

  var routeRules: [[String: Any]] {
    var rules: [[String: Any]] = []

    if preset == .bypassLan || preset == .directRuSu || preset == .directSelected
      || preset == .custom
    {
      if preset == .bypassLan {
        rules.append(["ip_is_private": true, "outbound": "direct"])
      }
    }

    if let block = domainRule(text: blockDomainsText, outbound: "block") {
      rules.append(block)
    }
    if let direct = domainRule(text: effectiveDirectDomainsText, outbound: "direct") {
      rules.append(direct)
    }
    if let proxy = domainRule(text: proxyDomainsText, outbound: "proxy-out") {
      rules.append(proxy)
    }

    return rules
  }

  var directDomainRule: [String: Any]? {
    domainRule(text: effectiveDirectDomainsText, outbound: "direct")
  }

  var effectiveDirectDomainsText: String {
    switch preset {
    case .proxyAll:
      ""
    case .directRuSu:
      Self.defaultDirectDomainsText
    case .directSelected, .custom, .bypassLan:
      directDomainsText
    }
  }

  func exportJSONString() throws -> String {
    let data = try JSONEncoder.routing.encode(self)
    return String(data: data, encoding: .utf8) ?? "{}"
  }

  static func importJSONString(_ text: String) throws -> RoutingSettings {
    let data = Data(text.utf8)
    return try JSONDecoder.routing.decode(RoutingSettings.self, from: data)
  }

  private func domainRule(text: String, outbound: String) -> [String: Any]? {
    let parsed = DomainRuleParser.parse(text)
    guard !parsed.domainSuffix.isEmpty || !parsed.domain.isEmpty else { return nil }

    var rule: [String: Any] = ["outbound": outbound]
    if !parsed.domainSuffix.isEmpty {
      rule["domain_suffix"] = parsed.domainSuffix
    }
    if !parsed.domain.isEmpty {
      rule["domain"] = parsed.domain
    }
    return rule
  }
}

enum RoutingPreset: String, Codable, CaseIterable, Identifiable {
  case proxyAll
  case directRuSu
  case directSelected
  case bypassLan
  case custom

  var id: String { rawValue }

  var title: String {
    switch self {
    case .proxyAll: "Proxy all"
    case .directRuSu: "Direct RU/SU"
    case .directSelected: "Direct selected"
    case .bypassLan: "Bypass LAN"
    case .custom: "Custom"
    }
  }

  var detail: String {
    switch self {
    case .proxyAll: "Весь трафик идёт через proxy-out, кроме служебных правил sing-box."
    case .directRuSu: "*.ru и *.su идут напрямую, остальное через proxy."
    case .directSelected: "Direct domains идут напрямую, proxy/block группы применяются отдельно."
    case .bypassLan: "Локальные/private IP идут напрямую плюс пользовательские direct domains."
    case .custom: "Полный ручной контроль direct/proxy/block domain groups."
    }
  }
}

struct ParsedDomainRules: Equatable {
  var domain: [String]
  var domainSuffix: [String]
}

enum DomainRuleParser {
  static func parse(_ text: String) -> ParsedDomainRules {
    let exact: [String] = []
    var suffix = OrderedUniqueStrings()

    for rawToken in tokens(from: text) {
      guard let normalized = normalize(rawToken) else { continue }
      switch normalized.kind {
      case .exact:
        // Product domains should usually include subdomains too, so store them as suffix rules.
        suffix.append(normalized.value)
      case .suffix:
        suffix.append(normalized.value)
      }
    }

    return ParsedDomainRules(domain: exact, domainSuffix: suffix.values)
  }

  private static func tokens(from text: String) -> [String] {
    text
      .replacingOccurrences(of: "，", with: ",")
      .components(separatedBy: CharacterSet(charactersIn: ",\n; "))
      .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
      .filter { !$0.isEmpty && !$0.hasPrefix("#") }
  }

  private static func normalize(_ token: String) -> NormalizedDomain? {
    var value = token.lowercased()
    value = value.replacingOccurrences(of: "http://", with: "")
    value = value.replacingOccurrences(of: "https://", with: "")
    value = value.split(separator: "/", maxSplits: 1).first.map(String.init) ?? value
    value = value.split(separator: ":", maxSplits: 1).first.map(String.init) ?? value
    value = value.trimmingCharacters(in: CharacterSet(charactersIn: "."))

    guard !value.isEmpty else { return nil }

    if value.hasPrefix("*.") {
      let suffix = String(value.dropFirst(2)).trimmingCharacters(
        in: CharacterSet(charactersIn: "."))
      return suffix.isEmpty ? nil : NormalizedDomain(kind: .suffix, value: suffix)
    }

    if token.hasPrefix(".") {
      return NormalizedDomain(kind: .suffix, value: value)
    }

    return NormalizedDomain(kind: .exact, value: value)
  }

  private enum DomainKind {
    case exact
    case suffix
  }

  private struct NormalizedDomain {
    var kind: DomainKind
    var value: String
  }
}

private struct OrderedUniqueStrings {
  private(set) var values: [String] = []
  private var seen: Set<String> = []

  mutating func append(_ value: String) {
    guard !seen.contains(value) else { return }
    seen.insert(value)
    values.append(value)
  }
}

extension JSONEncoder {
  fileprivate static var routing: JSONEncoder {
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    return encoder
  }
}

extension JSONDecoder {
  fileprivate static var routing: JSONDecoder { JSONDecoder() }
}
