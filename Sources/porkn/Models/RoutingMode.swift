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
      "Full VPN / TUN через PacketTunnelProvider. В этом build режим fail-fast без entitlement и не меняет system routes."
    }
  }

  var isAvailable: Bool {
    switch self {
    case .localProxy: true
    case .systemTun: NetworkExtensionSupport.isAvailable
    }
  }

  var availabilityNote: String? {
    switch self {
    case .localProxy: nil
    case .systemTun:
      NetworkExtensionSupport.unavailableReason
        ?? "Full VPN/TUN entitlement detected. PacketTunnelProvider handoff is experimental."
    }
  }
}
