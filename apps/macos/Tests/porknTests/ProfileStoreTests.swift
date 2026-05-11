import Foundation
import XCTest

@testable import porkn

@MainActor
final class ProfileStoreTests: XCTestCase {
  func testManualImportUpsertsSameProfileInsteadOfDuplicating() throws {
    let dir = FileManager.default.temporaryDirectory.appendingPathComponent(
      UUID().uuidString, isDirectory: true)
    try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    let store = ProfileStore(
      profilesFileURL: dir.appendingPathComponent("profiles.json"),
      subscriptionsFileURL: dir.appendingPathComponent("subscriptions.json")
    )

    let first = "vless://id@example.com:443?security=reality&type=tcp#Old"
    let second = "vless://id@example.com:443?security=reality&type=tcp#New"
    try store.importConfig(first)
    let firstID = try XCTUnwrap(store.profiles.first?.id)
    try store.importConfig(second)

    XCTAssertEqual(store.profiles.count, 1)
    XCTAssertEqual(store.profiles.first?.id, firstID)
    XCTAssertEqual(store.profiles.first?.name, "New")
  }
}
