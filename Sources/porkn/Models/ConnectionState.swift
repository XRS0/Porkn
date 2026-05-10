import Foundation

enum ConnectionState: Equatable {
  case disconnected
  case connecting(TunnelProfile)
  case connected(TunnelProfile, connectedAt: Date)
  case switching(from: TunnelProfile, to: TunnelProfile)
  case disconnecting
  case failed(String)

  var title: String {
    switch self {
    case .disconnected: "Не подключено"
    case .connecting: "Подключение…"
    case .connected: "Защищено"
    case .switching(_, let target): "Переключение на \(target.name)…"
    case .disconnecting: "Отключение…"
    case .failed: "Ошибка"
    }
  }

  var isActive: Bool {
    if case .connected = self { return true }
    return false
  }

  var isTransitioning: Bool {
    switch self {
    case .connecting, .switching, .disconnecting: true
    default: false
    }
  }
}
