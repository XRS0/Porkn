import AppKit
import CryptoKit
import Foundation

struct GitHubReleaseAsset: Decodable, Equatable {
  let name: String
  let browserDownloadURL: URL

  enum CodingKeys: String, CodingKey {
    case name
    case browserDownloadURL = "browser_download_url"
  }
}

struct GitHubRelease: Decodable, Equatable {
  let tagName: String
  let htmlURL: URL
  let name: String?
  let prerelease: Bool
  let draft: Bool
  let assets: [GitHubReleaseAsset]?

  enum CodingKeys: String, CodingKey {
    case tagName = "tag_name"
    case htmlURL = "html_url"
    case name
    case prerelease
    case draft
    case assets
  }
}

struct UpdateCheckResult: Equatable {
  let currentVersion: String
  let latestVersion: String
  let releaseURL: URL
  let isUpdateAvailable: Bool
  let assetName: String?
  let assetURL: URL?
  let sha256: String?
  let sha256SumsURL: URL?

  var canInstall: Bool { isUpdateAvailable && assetURL != nil }

  var title: String {
    isUpdateAvailable ? "Update available: \(latestVersion)" : "porkn is up to date"
  }

  var detail: String {
    isUpdateAvailable
      ? "Installed \(currentVersion), latest \(latestVersion)"
      : "Installed \(currentVersion), latest \(latestVersion)"
  }
}

struct UpdateInstallResult: Equatable {
  let version: String
  let downloadedArchiveURL: URL
  let installerScriptURL: URL
}

struct UpdateCheckService {
  var owner = "XRS0"
  var repo = "Porkn"

  func check(currentVersion: String = Self.currentAppVersion) async throws -> UpdateCheckResult {
    let url = URL(string: "https://api.github.com/repos/\(owner)/\(repo)/releases/latest")!
    var request = URLRequest(url: url)
    request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
    request.setValue("porkn", forHTTPHeaderField: "User-Agent")
    let (data, response) = try await URLSession.shared.data(for: request)
    if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
      throw URLError(.badServerResponse)
    }
    return try Self.parseLatestRelease(data: data, currentVersion: currentVersion)
  }

  func downloadAndInstall(_ result: UpdateCheckResult, progress: @escaping @Sendable (String) -> Void) async throws -> UpdateInstallResult {
    guard let assetURL = result.assetURL else {
      throw CocoaError(.fileNoSuchFile, userInfo: [NSLocalizedDescriptionKey: "No installable macOS update asset was found in the latest release."])
    }

    let fileManager = FileManager.default
    let updateDirectory = try Self.updateDirectory(version: result.latestVersion)
    let archiveURL = updateDirectory.appendingPathComponent(result.assetName ?? Self.preferredMacAssetName, isDirectory: false)
    let extractDirectory = updateDirectory.appendingPathComponent("extracted", isDirectory: true)
    let scriptURL = updateDirectory.appendingPathComponent("install-porkn-update.sh", isDirectory: false)

    try? fileManager.removeItem(at: extractDirectory)
    try fileManager.createDirectory(at: updateDirectory, withIntermediateDirectories: true)

    progress("Downloading update package…")
    let (downloadedURL, response) = try await URLSession.shared.download(from: assetURL)
    if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
      throw URLError(.badServerResponse)
    }
    try? fileManager.removeItem(at: archiveURL)
    try fileManager.moveItem(at: downloadedURL, to: archiveURL)

    var expectedSha = result.sha256
    if expectedSha == nil, let sha256SumsURL = result.sha256SumsURL {
      let (sumsData, sumsResponse) = try await URLSession.shared.data(from: sha256SumsURL)
      if let http = sumsResponse as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
        throw URLError(.badServerResponse)
      }
      expectedSha = Self.parseSha256(String(decoding: sumsData, as: UTF8.self), assetName: result.assetName ?? Self.preferredMacAssetName)
    }

    if let expectedSha, !expectedSha.isEmpty {
      progress("Verifying SHA256 checksum…")
      let actualSha = try Self.sha256Hex(for: archiveURL)
      guard actualSha.caseInsensitiveCompare(expectedSha) == .orderedSame else {
        throw CocoaError(.fileReadCorruptFile, userInfo: [NSLocalizedDescriptionKey: "Update checksum mismatch. Expected \(expectedSha), got \(actualSha)."])
      }
    }

    progress("Extracting update package…")
    try fileManager.createDirectory(at: extractDirectory, withIntermediateDirectories: true)
    try Self.run("/usr/bin/ditto", arguments: ["-x", "-k", archiveURL.path, extractDirectory.path])

    let newAppURL = try Self.findExtractedApp(in: extractDirectory)
    let currentAppURL = Self.currentApplicationURL
    let installTargetURL = currentAppURL.pathExtension == "app"
      ? currentAppURL
      : fileManager.homeDirectoryForCurrentUser.appendingPathComponent("Applications/porkn.app", isDirectory: true)

    try writeInstallerScript(scriptURL: scriptURL, newAppURL: newAppURL, targetAppURL: installTargetURL, processIdentifier: ProcessInfo.processInfo.processIdentifier)
    try Self.run("/bin/chmod", arguments: ["+x", scriptURL.path])

    progress("Starting updater and closing porkn…")
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/bin/sh")
    process.arguments = [scriptURL.path]
    try process.run()

    return UpdateInstallResult(version: result.latestVersion, downloadedArchiveURL: archiveURL, installerScriptURL: scriptURL)
  }

  static func parseLatestRelease(data: Data, currentVersion: String) throws -> UpdateCheckResult {
    let release = try JSONDecoder().decode(GitHubRelease.self, from: data)
    let latest = normalizedVersion(release.tagName)
    let current = normalizedVersion(currentVersion)
    let assetName = preferredMacAssetName
    let assets = release.assets ?? []
    let asset = assets.first { $0.name == assetName }
    let shaAsset = assets.first { $0.name == "SHA256SUMS.txt" }
    let sha: String? = nil
    return UpdateCheckResult(
      currentVersion: current,
      latestVersion: latest,
      releaseURL: release.htmlURL,
      isUpdateAvailable: compareVersions(latest, current) == .orderedDescending,
      assetName: asset?.name,
      assetURL: asset?.browserDownloadURL,
      sha256: sha,
      sha256SumsURL: shaAsset?.browserDownloadURL
    )
  }

  static var currentAppVersion: String {
    Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.1.0"
  }

  static var preferredMacAssetName: String {
    #if arch(arm64)
      return "porkn-macos-arm64.zip"
    #else
      return "porkn-macos-x86_64.zip"
    #endif
  }

  static var currentApplicationURL: URL {
    var url = Bundle.main.bundleURL
    while url.pathExtension != "app", url.path != "/" {
      url.deleteLastPathComponent()
    }
    return url
  }

  static func normalizedVersion(_ version: String) -> String {
    version.trimmingCharacters(in: .whitespacesAndNewlines)
      .replacingOccurrences(of: #"^[vV]"#, with: "", options: .regularExpression)
  }

  static func compareVersions(_ lhs: String, _ rhs: String) -> ComparisonResult {
    let left = normalizedVersion(lhs).split(separator: ".").map { Int($0) ?? 0 }
    let right = normalizedVersion(rhs).split(separator: ".").map { Int($0) ?? 0 }
    let count = max(left.count, right.count)
    for index in 0..<count {
      let l = index < left.count ? left[index] : 0
      let r = index < right.count ? right[index] : 0
      if l < r { return .orderedAscending }
      if l > r { return .orderedDescending }
    }
    return .orderedSame
  }

  static func parseSha256(_ sums: String, assetName: String) -> String? {
    for line in sums.split(whereSeparator: \.isNewline) {
      let parts = line.split(whereSeparator: \.isWhitespace)
      guard parts.count >= 2 else { continue }
      if URL(fileURLWithPath: String(parts.last!)).lastPathComponent == assetName {
        return String(parts[0])
      }
    }
    return nil
  }

  private static func updateDirectory(version: String) throws -> URL {
    let base = try FileManager.default.url(
      for: .applicationSupportDirectory,
      in: .userDomainMask,
      appropriateFor: nil,
      create: true
    )
    let directory = base.appendingPathComponent("porkn/Updates/\(version)", isDirectory: true)
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    return directory
  }

  private static func sha256Hex(for url: URL) throws -> String {
    let data = try Data(contentsOf: url)
    return SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
  }

  private static func findExtractedApp(in directory: URL) throws -> URL {
    let enumerator = FileManager.default.enumerator(at: directory, includingPropertiesForKeys: nil)!
    for case let url as URL in enumerator where url.pathExtension == "app" && url.lastPathComponent.lowercased().contains("porkn") {
      return url
    }
    throw CocoaError(.fileNoSuchFile, userInfo: [NSLocalizedDescriptionKey: "Downloaded update does not contain porkn.app."])
  }

  private static func run(_ executable: String, arguments: [String]) throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: executable)
    process.arguments = arguments
    try process.run()
    process.waitUntilExit()
    if process.terminationStatus != 0 {
      throw CocoaError(.executableLoad, userInfo: [NSLocalizedDescriptionKey: "Command failed: \(executable) \(arguments.joined(separator: " "))"])
    }
  }

  private func writeInstallerScript(scriptURL: URL, newAppURL: URL, targetAppURL: URL, processIdentifier: Int32) throws {
    let script = """
#!/bin/sh
set -e
NEW_APP='\(Self.shellEscape(newAppURL.path))'
TARGET_APP='\(Self.shellEscape(targetAppURL.path))'
PID_TO_WAIT='\(processIdentifier)'
while kill -0 "$PID_TO_WAIT" 2>/dev/null; do
  sleep 0.2
done
mkdir -p "$(dirname "$TARGET_APP")"
rm -rf "$TARGET_APP"
/usr/bin/ditto "$NEW_APP" "$TARGET_APP"
/usr/bin/open "$TARGET_APP"
"""
    try script.write(to: scriptURL, atomically: true, encoding: .utf8)
  }

  private static func shellEscape(_ value: String) -> String {
    value.replacingOccurrences(of: "'", with: "'\\''")
  }
}
