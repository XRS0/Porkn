import Foundation
import XCTest

@testable import porkn

@MainActor
final class ProfileStorePingTests: XCTestCase {
  func testSelectFastestProfileUsesLowestSavedPing() throws {
    let dir = FileManager.default.temporaryDirectory.appendingPathComponent(
      UUID().uuidString, isDirectory: true)
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    let store = ProfileStore(
      profilesFileURL: dir.appendingPathComponent("profiles.json"),
      subscriptionsFileURL: dir.appendingPathComponent("subscriptions.json")
    )

    try store.importConfig("socks://127.0.0.1:1080#Slow\nsocks://127.0.0.1:1081#Fast")
    let slow = try XCTUnwrap(store.profiles.first { $0.name == "Slow" })
    let fast = try XCTUnwrap(store.profiles.first { $0.name == "Fast" })
    store.updatePing(for: slow.id, milliseconds: 250)
    store.updatePing(for: fast.id, milliseconds: 40)

    let selected = store.selectFastestProfile()

    XCTAssertEqual(selected?.id, fast.id)
    XCTAssertEqual(store.selectedProfileID, fast.id)
  }
}
