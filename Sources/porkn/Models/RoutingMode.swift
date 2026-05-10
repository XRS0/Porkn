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
      "Запускает sing-box и автоматически включает HTTP/HTTPS/SOCKS proxy macOS на выбранный локальный порт 127.0.0.1:2080-2090."
    case .systemTun:
      "Full VPN / TUN через NetworkExtension. Сейчас режим недоступен без Apple Developer entitlement packet-tunnel-provider."
    }
  }

  var isAvailable: Bool {
    switch self {
    case .localProxy: true
    case .systemTun: false
    }
  }

  var availabilityNote: String? {
    isAvailable
      ? nil
      : "Experimental: нужен Apple Developer ID и NetworkExtension entitlement. System routes не меняются."
  }
}
