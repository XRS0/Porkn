import Foundation

enum RoutingMode: String, CaseIterable, Identifiable {
  case localProxy
  case systemTun

  var id: String { rawValue }

  var title: String {
    switch self {
    case .localProxy: "System proxy (как v2RayTun)"
    case .systemTun: "System VPN / TUN"
    }
  }

  var detail: String {
    switch self {
    case .localProxy:
      "Запускает sing-box и автоматически включает HTTP/HTTPS/SOCKS proxy macOS на 127.0.0.1:2080."
    case .systemTun:
      "Перехватывает системный трафик через TUN. На macOS потребует повышенных прав/NetworkExtension."
    }
  }
}
