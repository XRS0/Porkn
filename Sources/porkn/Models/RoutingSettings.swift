import Foundation

struct RoutingSettings: Codable, Equatable {
  static let directDomainsStorageKey = "routing.directDomains"

  var directDomainsText: String

  static let defaultDirectDomainsText = """
    *.ru
    *.su
    """

  static var current: RoutingSettings {
    let stored = UserDefaults.standard.string(forKey: directDomainsStorageKey)
    return RoutingSettings(directDomainsText: stored ?? defaultDirectDomainsText)
  }

  var directDomainRule: [String: Any]? {
    let parsed = DomainRuleParser.parse(directDomainsText)
    guard !parsed.domainSuffix.isEmpty || !parsed.domain.isEmpty else { return nil }

    var rule: [String: Any] = ["outbound": "direct"]
    if !parsed.domainSuffix.isEmpty {
      rule["domain_suffix"] = parsed.domainSuffix
    }
    if !parsed.domain.isEmpty {
      rule["domain"] = parsed.domain
    }
    return rule
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
