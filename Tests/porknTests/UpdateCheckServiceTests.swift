import XCTest

@testable import porkn

final class UpdateCheckServiceTests: XCTestCase {
  func testParsesLatestReleaseAndDetectsUpdate() throws {
    let json = """
      {
        "tag_name": "v1.2.0",
        "html_url": "https://github.com/XRS0/Porkn/releases/tag/v1.2.0",
        "name": "v1.2.0",
        "draft": false,
        "prerelease": false
      }
      """

    let result = try UpdateCheckService.parseLatestRelease(
      data: Data(json.utf8), currentVersion: "1.1.9")

    XCTAssertTrue(result.isUpdateAvailable)
    XCTAssertEqual(result.latestVersion, "1.2.0")
  }

  func testVersionCompareHandlesVPrefixAndPatchNumbers() {
    XCTAssertEqual(UpdateCheckService.compareVersions("v1.10.0", "1.2.9"), .orderedDescending)
    XCTAssertEqual(UpdateCheckService.compareVersions("1.0", "1.0.0"), .orderedSame)
    XCTAssertEqual(UpdateCheckService.compareVersions("0.9.9", "1.0.0"), .orderedAscending)
  }
}
