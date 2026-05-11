import Foundation

enum Formatters {
  static func latency(_ milliseconds: Int?) -> String {
    guard let milliseconds else { return "—" }
    return "\(milliseconds) ms"
  }
}
