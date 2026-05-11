import Foundation

enum SubscriptionAutoRefreshInterval: String, CaseIterable, Identifiable {
  case off
  case sixHours
  case twelveHours
  case daily

  var id: String { rawValue }

  var title: String {
    switch self {
    case .off: "Off"
    case .sixHours: "Every 6 hours"
    case .twelveHours: "Every 12 hours"
    case .daily: "Daily"
    }
  }

  var timeInterval: TimeInterval? {
    switch self {
    case .off: nil
    case .sixHours: 6 * 60 * 60
    case .twelveHours: 12 * 60 * 60
    case .daily: 24 * 60 * 60
    }
  }
}

enum ProfileSortMode: String, CaseIterable, Identifiable {
  case favoritesFirst
  case fastestFirst
  case name
  case recentlyUsed

  var id: String { rawValue }

  var title: String {
    switch self {
    case .favoritesFirst: "Favorites first"
    case .fastestFirst: "Fastest first"
    case .name: "Name"
    case .recentlyUsed: "Recently used"
    }
  }
}

struct SubscriptionRefreshSummary: Codable, Equatable {
  var added: Int
  var updated: Int
  var removed: Int
  var total: Int
  var subscriptionName: String
  var refreshedAt: Date

  var shortText: String {
    "\(subscriptionName): +\(added) / ~\(updated) / -\(removed), total \(total)"
  }
}
