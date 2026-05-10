import Foundation

enum ProxyHealthStatus: Equatable {
  case notChecked
  case checking
  case protected(proxyIP: String?)
  case proxyReachable
  case remoteCheckFailed(String)
  case localProxyFailed(String)

  var title: String {
    switch self {
    case .notChecked: "Не проверено"
    case .checking: "Проверка…"
    case .protected: "Protected"
    case .proxyReachable: "Proxy reachable"
    case .remoteCheckFailed: "Remote check failed"
    case .localProxyFailed: "Local proxy failed"
    }
  }

  var detail: String {
    switch self {
    case .notChecked:
      "Health check запустится после подключения."
    case .checking:
      "Проверяю локальный proxy и внешний IP через proxy."
    case .protected(let proxyIP):
      proxyIP.map { "Proxy IP: \($0)" } ?? "Proxy доступен и отвечает через внешний endpoint."
    case .proxyReachable:
      "Локальный proxy доступен, но внешний IP endpoint не вернул IP."
    case .remoteCheckFailed(let message):
      message
    case .localProxyFailed(let message):
      message
    }
  }

  var isHealthy: Bool {
    switch self {
    case .protected, .proxyReachable: true
    default: false
    }
  }
}
