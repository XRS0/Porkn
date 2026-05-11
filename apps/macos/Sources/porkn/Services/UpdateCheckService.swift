import Foundation

struct GitHubRelease: Decodable, Equatable {
  let tagName: String
  let htmlURL: URL
  let name: String?
  let prerelease: Bool
  let draft: Bool

  enum CodingKeys: String, CodingKey {
    case tagName = "tag_name"
    case htmlURL = "html_url"
    case name
    case prerelease
    case draft
  }
}

struct UpdateCheckResult: Equatable {
  let currentVersion: String
  let latestVersion: String
  let releaseURL: URL
  let isUpdateAvailable: Bool

  var title: String {
    isUpdateAvailable ? "Update available: \(latestVersion)" : "porkn is up to date"
  }

  var detail: String {
    isUpdateAvailable
      ? "Installed \(currentVersion), latest \(latestVersion)"
      : "Installed \(currentVersion), latest \(latestVersion)"
  }
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

  static func parseLatestRelease(data: Data, currentVersion: String) throws -> UpdateCheckResult {
    let release = try JSONDecoder().decode(GitHubRelease.self, from: data)
    let latest = normalizedVersion(release.tagName)
    let current = normalizedVersion(currentVersion)
    return UpdateCheckResult(
      currentVersion: current,
      latestVersion: latest,
      releaseURL: release.htmlURL,
      isUpdateAvailable: compareVersions(latest, current) == .orderedDescending
    )
  }

  static var currentAppVersion: String {
    Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.1.0"
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
}
