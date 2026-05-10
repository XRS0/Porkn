import Foundation

struct Subscription: Identifiable, Codable, Hashable {
  var id: UUID
  var name: String
  var url: URL
  var createdAt: Date
  var lastRefreshAt: Date?
  var lastImportedCount: Int

  init(
    id: UUID = UUID(),
    name: String,
    url: URL,
    createdAt: Date = Date(),
    lastRefreshAt: Date? = nil,
    lastImportedCount: Int = 0
  ) {
    self.id = id
    self.name = name
    self.url = url
    self.createdAt = createdAt
    self.lastRefreshAt = lastRefreshAt
    self.lastImportedCount = lastImportedCount
  }
}

enum ImportPayload: Equatable {
  case profiles([TunnelProfile])
  case subscription(Subscription)
}
