import Foundation

enum AppSelection: Hashable, Identifiable {
  case profile(TunnelProfile.ID)
  case settings

  var id: String {
    switch self {
    case .profile(let id): "profile-\(id.uuidString)"
    case .settings: "settings"
    }
  }
}
