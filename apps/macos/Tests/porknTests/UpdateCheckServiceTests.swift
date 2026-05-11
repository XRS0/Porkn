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
        "prerelease": false,
        "assets": [
          {
            "name": "porkn-macos-arm64.zip",
            "browser_download_url": "https://github.com/XRS0/Porkn/releases/download/v1.2.0/porkn-macos-arm64.zip"
          },
          {
            "name": "porkn-macos-x86_64.zip",
            "browser_download_url": "https://github.com/XRS0/Porkn/releases/download/v1.2.0/porkn-macos-x86_64.zip"
          },
          {
            "name": "SHA256SUMS.txt",
            "browser_download_url": "https://github.com/XRS0/Porkn/releases/download/v1.2.0/SHA256SUMS.txt"
          }
        ]
      }
      """

    let result = try UpdateCheckService.parseLatestRelease(
      data: Data(json.utf8), currentVersion: "1.1.9")

    XCTAssertTrue(result.isUpdateAvailable)
    XCTAssertEqual(result.latestVersion, "1.2.0")
    XCTAssertTrue(result.canInstall)
    XCTAssertNotNil(result.sha256SumsURL)
  }

  func testParsesSha256ForAssetName() {
    let sums = "abc123  /tmp/release/porkn-macos-arm64.zip\nffff  porkn-macos-x86_64.zip"
    XCTAssertEqual(UpdateCheckService.parseSha256(sums, assetName: "porkn-macos-arm64.zip"), "abc123")
  }

  func testVersionCompareHandlesVPrefixAndPatchNumbers() {
    XCTAssertEqual(UpdateCheckService.compareVersions("v1.10.0", "1.2.9"), .orderedDescending)
    XCTAssertEqual(UpdateCheckService.compareVersions("1.0", "1.0.0"), .orderedSame)
    XCTAssertEqual(UpdateCheckService.compareVersions("0.9.9", "1.0.0"), .orderedAscending)
  }
}
