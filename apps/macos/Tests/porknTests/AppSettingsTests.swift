import XCTest

@testable import porkn

final class AppSettingsTests: XCTestCase {
  func testKillSwitchPreservesProxyOnlyOnUnexpectedActiveExit() {
    XCTAssertTrue(
      KillSwitchPolicy.shouldPreserveSystemProxyOnUnexpectedExit(
        isEnabled: true, wasDisconnecting: false, wasConnectedOrSwitching: true))
    XCTAssertFalse(
      KillSwitchPolicy.shouldPreserveSystemProxyOnUnexpectedExit(
        isEnabled: true, wasDisconnecting: true, wasConnectedOrSwitching: true))
    XCTAssertFalse(
      KillSwitchPolicy.shouldPreserveSystemProxyOnUnexpectedExit(
        isEnabled: false, wasDisconnecting: false, wasConnectedOrSwitching: true))
    XCTAssertFalse(
      KillSwitchPolicy.shouldPreserveSystemProxyOnUnexpectedExit(
        isEnabled: true, wasDisconnecting: false, wasConnectedOrSwitching: false))
  }

  func testLanguageTextSelection() {
    XCTAssertEqual(L10n.text("Привет", "Hello", language: .ru), "Привет")
    XCTAssertEqual(L10n.text("Привет", "Hello", language: .en), "Hello")
    XCTAssertEqual(AppLanguage.en.title, "English")
    XCTAssertEqual(AppLanguage.ru.title, "Русский")
  }
}
