import Foundation

@MainActor
final class ProfileStore: ObservableObject {
  @Published private(set) var profiles: [TunnelProfile] = []
  @Published private(set) var subscriptions: [Subscription] = []
  @Published var selectedProfileID: TunnelProfile.ID?

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
    let (data, _) = try await URLSession.shared.data(from: subscription.url)
    guard let body = String(data: data, encoding: .utf8) else { return [] }
    let imported = try parser.parseMany(body)
      .map { profile in
        var profile = profile
        profile.subscriptionID = subscription.id
        profile.subscriptionKey = profile.stableKey
        return profile
      }
    upsertSubscriptionProfiles(imported, subscriptionID: subscription.id)
    if let index = subscriptions.firstIndex(where: { $0.id == subscription.id }) {
      subscriptions[index].lastRefreshAt = Date()
      subscriptions[index].lastImportedCount = imported.count
    }
    save()
    return imported
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

  private func upsertSubscriptionProfiles(_ imported: [TunnelProfile], subscriptionID: UUID) {
    let importedKeys = Set(imported.map(\.stableKey))
    var existingByKey: [String: Int] = [:]
    for (index, profile) in profiles.enumerated() where profile.subscriptionID == subscriptionID {
      existingByKey[profile.stableKey] = index
    }

    var inserted: [TunnelProfile] = []
    for profile in imported {
      if let index = existingByKey[profile.stableKey] {
        profiles[index] = mergedProfile(existing: profiles[index], incoming: profile)
      } else {
        profiles.append(profile)
        inserted.append(profile)
      }
    }

    profiles.removeAll { profile in
      profile.subscriptionID == subscriptionID && !importedKeys.contains(profile.stableKey)
    }

    if let selectedProfileID, !profiles.contains(where: { $0.id == selectedProfileID }) {
      self.selectedProfileID = profiles.first?.id
    }
    if selectedProfileID == nil { selectedProfileID = inserted.first?.id ?? imported.first?.id }
  }

  private func mergedProfile(existing: TunnelProfile, incoming: TunnelProfile) -> TunnelProfile {
    var merged = incoming
    merged.id = existing.id
    merged.createdAt = existing.createdAt
    merged.lastPingMilliseconds = existing.lastPingMilliseconds
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

  func updatePing(for profileID: TunnelProfile.ID, milliseconds: Int) {
    guard let index = profiles.firstIndex(where: { $0.id == profileID }) else { return }
    profiles[index].lastPingMilliseconds = milliseconds
    save()
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
