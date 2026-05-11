import Foundation

enum ProxyProtocol: String, Codable, CaseIterable, Identifiable {
  case vless
  case vmess
  case trojan
  case shadowsocks = "ss"
  case hysteria2
  case tuic
  case socks
  case unknown

  var id: String { rawValue }

  var displayName: String {
    switch self {
    case .vless: "VLESS"
    case .vmess: "VMess"
    case .trojan: "Trojan"
    case .shadowsocks: "Shadowsocks"
    case .hysteria2: "Hysteria2"
    case .tuic: "TUIC"
    case .socks: "SOCKS"
    case .unknown: "Unknown"
    }
  }

  var systemImage: String {
    switch self {
    case .vless: "bolt.horizontal.circle"
    case .vmess: "network"
    case .trojan: "shield.lefthalf.filled"
    case .shadowsocks: "moon.stars"
    case .hysteria2: "hare"
    case .tuic: "speedometer"
    case .socks: "point.3.connected.trianglepath.dotted"
    case .unknown: "questionmark.circle"
    }
  }
}
