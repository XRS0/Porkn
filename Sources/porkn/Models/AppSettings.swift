import Foundation

enum AppLanguage: String, CaseIterable, Identifiable {
  case ru
  case en

  var id: String { rawValue }

  var title: String {
    switch self {
    case .ru: "Русский"
    case .en: "English"
    }
  }

  static var current: AppLanguage {
    AppLanguage(rawValue: UserDefaults.standard.string(forKey: "appLanguage") ?? "") ?? .ru
  }
}

enum L10n {
  static func text(_ ru: String, _ en: String, language: AppLanguage = .current) -> String {
    switch language {
    case .ru: ru
    case .en: en
    }
  }
}

enum KillSwitchPolicy {
  static let storageKey = "killSwitchEnabled"

  static var isEnabled: Bool {
    UserDefaults.standard.bool(forKey: storageKey)
  }

  static func shouldPreserveSystemProxyOnUnexpectedExit(
    isEnabled: Bool = Self.isEnabled, wasDisconnecting: Bool, wasConnectedOrSwitching: Bool
  ) -> Bool {
    isEnabled && !wasDisconnecting && wasConnectedOrSwitching
  }
}
