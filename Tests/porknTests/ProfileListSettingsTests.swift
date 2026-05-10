import Foundation
import XCTest

@testable import porkn

@MainActor
final class ProfileListSettingsTests: XCTestCase {
  func testTunnelProfileDecodesMissingFavoriteFieldsWithDefaults() throws {
    let json = """
      {
        "id": "00000000-0000-0000-0000-000000000001",
        "name": "Legacy",
        "proto": "socks",
        "serverHost": "legacy.example.com",
        "serverPort": 1080,
        "rawConfig": "socks://legacy.example.com:1080",
        "queryItems": {},
        "createdAt": "2026-05-10T00:00:00Z"
      }
      """

    let decoder = JSONDecoder()
    decoder.dateDecodingStrategy = .iso8601
    let profile = try decoder.decode(TunnelProfile.self, from: Data(json.utf8))

    XCTAssertFalse(profile.isFavorite)
    XCTAssertNil(profile.lastUsedAt)
  }

  func testFavoritesSearchAndSortModes() throws {
    let store = ProfileStore(profilesFileURL: tempURL("profiles"), subscriptionsFileURL: tempURL("subs"))
    try store.importConfig(
      """
      socks://alpha.example.com:1080#Alpha
      socks://beta.example.com:1080#Beta
      socks://gamma.example.com:1080#Gamma
      """
    )

    guard let alpha = store.profiles.first(where: { $0.name == "Alpha" }),
      let beta = store.profiles.first(where: { $0.name == "Beta" }),
      let gamma = store.profiles.first(where: { $0.name == "Gamma" })
    else {
      return XCTFail("Expected imported profiles")
    }

    store.toggleFavorite(beta)
    store.updatePing(for: alpha.id, milliseconds: 90)
    store.updatePing(for: beta.id, milliseconds: 30)
    store.markUsed(gamma.id, at: Date(timeIntervalSince1970: 300))
    store.markUsed(alpha.id, at: Date(timeIntervalSince1970: 200))

    XCTAssertEqual(
      store.filteredProfiles(searchText: "beta", favoritesOnly: false, sortMode: .name).map(\.name),
      ["Beta"])
    XCTAssertEqual(
      store.filteredProfiles(searchText: "", favoritesOnly: true, sortMode: .name).map(\.name),
      ["Beta"])
    XCTAssertEqual(
      store.filteredProfiles(searchText: "", favoritesOnly: false, sortMode: .fastestFirst).map(\.name).prefix(2),
      ["Beta", "Alpha"])
    XCTAssertEqual(
      store.filteredProfiles(searchText: "", favoritesOnly: false, sortMode: .recentlyUsed).first?.name,
      "Gamma")
  }

  func testSubscriptionRefreshSummaryCountsAddedUpdatedRemoved() throws {
    let store = ProfileStore(profilesFileURL: tempURL("profiles"), subscriptionsFileURL: tempURL("subs"))
    let url = URL(string: "https://example.com/sub")!
    try store.importConfig(url.absoluteString)
    guard let subscription = store.subscriptions.first else { return XCTFail("Expected subscription") }

    let first = try store.refresh(
      subscription: subscription,
      body: """
      socks://one.example.com:1080#One
      socks://two.example.com:1080#Two
      """
    ).summary

    XCTAssertEqual(first.added, 2)
    XCTAssertEqual(first.updated, 0)
    XCTAssertEqual(first.removed, 0)
    XCTAssertEqual(first.total, 2)

    let second = try store.refresh(
      subscription: subscription,
      body: """
      socks://one.example.com:1080#One%20Renamed
      socks://three.example.com:1080#Three
      """
    ).summary

    XCTAssertEqual(second.added, 1)
    XCTAssertEqual(second.updated, 1)
    XCTAssertEqual(second.removed, 1)
    XCTAssertEqual(second.total, 2)
    XCTAssertEqual(store.lastRefreshSummary, second)
  }

  private func tempURL(_ name: String) -> URL {
    FileManager.default.temporaryDirectory
      .appendingPathComponent("porkn-tests-\(UUID().uuidString)-\(name).json")
  }
}
