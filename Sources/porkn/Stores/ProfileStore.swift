import Foundation

@MainActor
final class ProfileStore: ObservableObject {
  @Published private(set) var profiles: [TunnelProfile] = []
  @Published private(set) var subscriptions: [Subscription] = []
  @Published var selectedProfileID: TunnelProfile.ID?
  @Published private(set) var isPingingAll = false
  @Published private(set) var lastRefreshSummary: SubscriptionRefreshSummary?
  @Published private(set) var isRefreshingSubscriptions = false

  private let parser = ConfigParser()
  private let profilesFileURL: URL
  private let subscriptionsFileURL: URL

  init(profilesFileURL: URL? = nil, subscriptionsFileURL: URL? = nil) {
    self.profilesFileURL = profilesFileURL ?? Self.defaultProfilesFileURL
    self.subscriptionsFileURL = subscriptionsFileURL ?? Self.defaultSubscriptionsFileURL
    load()
  }

  var selectedProfile: TunnelProfile? {
    get { profiles.first { $0.id == selectedProfileID } }
    set { selectedProfileID = newValue?.id }
  }

  @discardableResult
  func importConfig(_ text: String) throws -> ImportPayload {
    let payload = try parser.parsePayload(text)
    switch payload {
    case .profiles(let imported):
      upsertManualProfiles(imported)
    case .subscription(let subscription):
      upsertSubscription(subscription)
    }
    save()
    return payload
  }

  func refresh(_ subscription: Subscription) async throws -> [TunnelProfile] {
    let result = try await refreshWithSummary(subscription)
    return result.profiles
  }

  func refreshWithSummary(_ subscription: Subscription) async throws -> (profiles: [TunnelProfile], summary: SubscriptionRefreshSummary) {
    let (data, _) = try await URLSession.shared.data(from: subscription.url)
    guard let body = String(data: data, encoding: .utf8) else {
      let summary = SubscriptionRefreshSummary(
        added: 0, updated: 0, removed: 0, total: 0, subscriptionName: subscription.name, refreshedAt: Date())
      lastRefreshSummary = summary
      return ([], summary)
    }
    return try refresh(subscription: subscription, body: body)
  }

  func refresh(subscription: Subscription, body: String) throws -> (profiles: [TunnelProfile], summary: SubscriptionRefreshSummary) {
    let imported = try parser.parseMany(body)
      .map { profile in
        var profile = profile
        profile.subscriptionID = subscription.id
        profile.subscriptionKey = profile.stableKey
        return profile
      }
    let diff = upsertSubscriptionProfiles(imported, subscriptionID: subscription.id)
    let refreshedAt = Date()
    if let index = subscriptions.firstIndex(where: { $0.id == subscription.id }) {
      subscriptions[index].lastRefreshAt = refreshedAt
      subscriptions[index].lastImportedCount = imported.count
    }
    let summary = SubscriptionRefreshSummary(
      added: diff.added, updated: diff.updated, removed: diff.removed, total: imported.count,
      subscriptionName: subscription.name, refreshedAt: refreshedAt)
    lastRefreshSummary = summary
    save()
    return (imported, summary)
  }

  func refreshSubscriptionsIfNeeded(interval: SubscriptionAutoRefreshInterval, refreshOnLaunch: Bool = false) async {
    guard !isRefreshingSubscriptions, interval != .off || refreshOnLaunch else { return }
    isRefreshingSubscriptions = true
    defer { isRefreshingSubscriptions = false }

    for subscription in subscriptions where shouldRefresh(subscription, interval: interval, refreshOnLaunch: refreshOnLaunch) {
      _ = try? await refreshWithSummary(subscription)
    }
  }

  private func upsertSubscription(_ subscription: Subscription) {
    if let index = subscriptions.firstIndex(where: { $0.url == subscription.url }) {
      let existing = subscriptions[index]
      subscriptions[index] = Subscription(
        id: existing.id,
        name: subscription.name,
        url: subscription.url,
        createdAt: existing.createdAt,
        lastRefreshAt: existing.lastRefreshAt,
        lastImportedCount: existing.lastImportedCount
      )
    } else {
      subscriptions.append(subscription)
    }
  }

  private func upsertManualProfiles(_ imported: [TunnelProfile]) {
    var inserted: [TunnelProfile] = []
    for profile in imported {
      if let index = profiles.firstIndex(where: {
        $0.subscriptionID == nil && $0.stableKey == profile.stableKey
      }) {
        profiles[index] = mergedProfile(existing: profiles[index], incoming: profile)
      } else {
        profiles.append(profile)
        inserted.append(profile)
      }
    }
    if selectedProfileID == nil { selectedProfileID = inserted.first?.id ?? imported.first?.id }
  }

  @discardableResult
  private func upsertSubscriptionProfiles(_ imported: [TunnelProfile], subscriptionID: UUID) -> (added: Int, updated: Int, removed: Int) {
    let importedKeys = Set(imported.map(\.stableKey))
    var existingByKey: [String: Int] = [:]
    for (index, profile) in profiles.enumerated() where profile.subscriptionID == subscriptionID {
      existingByKey[profile.stableKey] = index
    }

    var inserted: [TunnelProfile] = []
    var updated = 0
    for profile in imported {
      if let index = existingByKey[profile.stableKey] {
        profiles[index] = mergedProfile(existing: profiles[index], incoming: profile)
        updated += 1
      } else {
        profiles.append(profile)
        inserted.append(profile)
      }
    }

    let removed = profiles.filter { profile in
      profile.subscriptionID == subscriptionID && !importedKeys.contains(profile.stableKey)
    }.count
    profiles.removeAll { profile in
      profile.subscriptionID == subscriptionID && !importedKeys.contains(profile.stableKey)
    }

    if let selectedProfileID, !profiles.contains(where: { $0.id == selectedProfileID }) {
      self.selectedProfileID = profiles.first?.id
    }
    if selectedProfileID == nil { selectedProfileID = inserted.first?.id ?? imported.first?.id }
    return (inserted.count, updated, removed)
  }

  private func mergedProfile(existing: TunnelProfile, incoming: TunnelProfile) -> TunnelProfile {
    var merged = incoming
    merged.id = existing.id
    merged.createdAt = existing.createdAt
    merged.lastPingMilliseconds = existing.lastPingMilliseconds
    merged.isFavorite = existing.isFavorite
    merged.lastUsedAt = existing.lastUsedAt
    if merged.subscriptionID == nil { merged.subscriptionID = existing.subscriptionID }
    if merged.subscriptionKey == nil {
      merged.subscriptionKey = existing.subscriptionKey ?? incoming.stableKey
    }
    return merged
  }

  func deleteSelected() {
    guard let selectedProfileID else { return }
    profiles.removeAll { $0.id == selectedProfileID }
    self.selectedProfileID = profiles.first?.id
    save()
  }

  func delete(_ profile: TunnelProfile) {
    profiles.removeAll { $0.id == profile.id }
    if selectedProfileID == profile.id { selectedProfileID = profiles.first?.id }
    save()
  }

  func delete(_ subscription: Subscription) {
    subscriptions.removeAll { $0.id == subscription.id }
    profiles.removeAll { $0.subscriptionID == subscription.id }
    if let selectedProfileID, !profiles.contains(where: { $0.id == selectedProfileID }) {
      self.selectedProfileID = profiles.first?.id
    }
    save()
  }

  func addManualSOCKS(name: String, host: String, port: Int, username: String?, password: String?) {
    let auth =
      username.flatMap { user in
        password.map { "\(user):\($0)@" } ?? "\(user)@"
      } ?? ""
    let raw =
      "socks://\(auth)\(host):\(port)#\(name.addingPercentEncoding(withAllowedCharacters: .urlFragmentAllowed) ?? name)"
    let profile = TunnelProfile(
      name: name,
      proto: .socks,
      serverHost: host,
      serverPort: port,
      rawConfig: raw,
      username: username,
      password: password,
      queryItems: ["version": "5"]
    )
    profiles.append(profile)
    selectedProfileID = profile.id
    save()
  }

  func toggleFavorite(_ profile: TunnelProfile) {
    guard let index = profiles.firstIndex(where: { $0.id == profile.id }) else { return }
    profiles[index].isFavorite.toggle()
    save()
  }

  func markUsed(_ profileID: TunnelProfile.ID, at date: Date = Date()) {
    guard let index = profiles.firstIndex(where: { $0.id == profileID }) else { return }
    profiles[index].lastUsedAt = date
    save()
  }

  func filteredProfiles(searchText: String, favoritesOnly: Bool, sortMode: ProfileSortMode) -> [TunnelProfile] {
    let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    var result = profiles.filter { profile in
      (!favoritesOnly || profile.isFavorite)
        && (query.isEmpty
          || profile.name.lowercased().contains(query)
          || profile.serverHost.lowercased().contains(query)
          || profile.proto.displayName.lowercased().contains(query)
          || subscriptionName(for: profile).lowercased().contains(query))
    }

    result.sort { left, right in
      switch sortMode {
      case .favoritesFirst:
        if left.isFavorite != right.isFavorite { return left.isFavorite && !right.isFavorite }
        return left.name.localizedCaseInsensitiveCompare(right.name) == .orderedAscending
      case .fastestFirst:
        switch (left.lastPingMilliseconds, right.lastPingMilliseconds) {
        case let (l?, r?) where l != r: return l < r
        case (_?, nil): return true
        case (nil, _?): return false
        default: return left.name.localizedCaseInsensitiveCompare(right.name) == .orderedAscending
        }
      case .name:
        return left.name.localizedCaseInsensitiveCompare(right.name) == .orderedAscending
      case .recentlyUsed:
        return (left.lastUsedAt ?? .distantPast) > (right.lastUsedAt ?? .distantPast)
      }
    }
    return result
  }

  func subscriptionName(for profile: TunnelProfile) -> String {
    guard let subscriptionID = profile.subscriptionID else { return "Manual" }
    return subscriptions.first { $0.id == subscriptionID }?.name ?? "Subscription"
  }

  func updatePing(for profileID: TunnelProfile.ID, milliseconds: Int?) {
    guard let index = profiles.firstIndex(where: { $0.id == profileID }) else { return }
    profiles[index].lastPingMilliseconds = milliseconds
    save()
  }

  func pingAll(using pingService: PingService = PingService()) async {
    guard !isPingingAll else { return }
    isPingingAll = true
    defer { isPingingAll = false }

    let snapshot = profiles
    await withTaskGroup(of: (TunnelProfile.ID, Int?).self) { group in
      for profile in snapshot {
        group.addTask {
          let value = await pingService.measure(profile: profile)
          return (profile.id, value)
        }
      }

      for await (profileID, value) in group {
        updatePing(for: profileID, milliseconds: value)
      }
    }
  }

  @discardableResult
  func selectFastestProfile() -> TunnelProfile? {
    guard
      let fastest =
        profiles
        .filter({ $0.lastPingMilliseconds != nil })
        .min(by: { ($0.lastPingMilliseconds ?? Int.max) < ($1.lastPingMilliseconds ?? Int.max) })
    else { return nil }
    selectedProfileID = fastest.id
    return fastest
  }

  private func shouldRefresh(
    _ subscription: Subscription, interval: SubscriptionAutoRefreshInterval, refreshOnLaunch: Bool
  ) -> Bool {
    if refreshOnLaunch { return true }
    guard let seconds = interval.timeInterval else { return false }
    guard let lastRefreshAt = subscription.lastRefreshAt else { return true }
    return Date().timeIntervalSince(lastRefreshAt) >= seconds
  }

  func load() {
    profiles =
      (try? JSONDecoder.porkn.decode([TunnelProfile].self, from: Data(contentsOf: profilesFileURL)))
      ?? []
    subscriptions =
      (try? JSONDecoder.porkn.decode(
        [Subscription].self, from: Data(contentsOf: subscriptionsFileURL))) ?? []
    selectedProfileID = profiles.first?.id
  }

  func save() {
    do {
      try FileManager.default.createDirectory(
        at: profilesFileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
      try JSONEncoder.porkn.encode(profiles).write(to: profilesFileURL, options: .atomic)
      try JSONEncoder.porkn.encode(subscriptions).write(to: subscriptionsFileURL, options: .atomic)
    } catch {
      assertionFailure("Failed to save profiles: \(error)")
    }
  }

  private static var supportDirectory: URL {
    FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
      .appendingPathComponent("porkn", isDirectory: true)
  }

  private static var defaultProfilesFileURL: URL {
    supportDirectory.appendingPathComponent("profiles.json")
  }

  private static var defaultSubscriptionsFileURL: URL {
    supportDirectory.appendingPathComponent("subscriptions.json")
  }
}

extension JSONEncoder {
  fileprivate static var porkn: JSONEncoder {
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    encoder.dateEncodingStrategy = .iso8601
    return encoder
  }
}

extension JSONDecoder {
  fileprivate static var porkn: JSONDecoder {
    let decoder = JSONDecoder()
    decoder.dateDecodingStrategy = .iso8601
    return decoder
  }
}
