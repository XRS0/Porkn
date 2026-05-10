import Foundation

enum SensitiveRedactor {
  static func redact(_ text: String) -> String {
    var result = text

    result = replacing(
      #"(?i)(vless|vmess|trojan|ss|hysteria2|tuic|socks)://([^@\s]+)@"#,
      in: result,
      template: "$1://••••@"
    )
    result = replacing(
      #"(?i)([?&#](?:token|access_token|key|password|passwd|uuid|id|secret)=)([^&#\s]+)"#,
      in: result,
      template: "$1••••"
    )
    result = replacing(
      #"(?i)("(?:uuid|password|passwd|secret|token|access_token|public_key|short_id)"\s*:\s*")[^"]+(")"#,
      in: result,
      template: "$1••••$2"
    )
    result = replacing(
      #"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b"#,
      in: result,
      template: "••••-uuid"
    )

    return result
  }

  private static func replacing(_ pattern: String, in text: String, template: String) -> String {
    guard let regex = try? NSRegularExpression(pattern: pattern) else { return text }
    let range = NSRange(text.startIndex..<text.endIndex, in: text)
    return regex.stringByReplacingMatches(in: text, range: range, withTemplate: template)
  }
}
