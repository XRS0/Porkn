import Foundation

enum ConnectionState: Equatable {
  case disconnected
  case connecting(TunnelProfile)
  case connected(TunnelProfile, connectedAt: Date)
  case disconnecting
  case failed(String)

  var title: String {
    switch self {
    case .disconnected: "Не подключено"
    case .connecting: "Подключение…"
    case .connected: "Защищено"
    case .disconnecting: "Отключение…"
    case .failed: "Ошибка"
    }
  }

  var isActive: Bool {
    if case .connected = self { return true }
    return false
  }
}
