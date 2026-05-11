import XCTest

@testable import porkn

final class ConnectionStateTests: XCTestCase {
  func testSwitchingStateExposesTransitionTitleAndBusyFlag() throws {
    let first = try ConfigParser().parseOne("socks://127.0.0.1:1080#First")
    let second = try ConfigParser().parseOne("socks://127.0.0.1:1081#Second")
    let state = ConnectionState.switching(from: first, to: second)

    XCTAssertTrue(state.isTransitioning)
    XCTAssertFalse(state.isActive)
    XCTAssertEqual(state.title, "Переключение на Second…")
  }
}
