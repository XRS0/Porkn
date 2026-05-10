import Foundation

struct SingBoxRuntimeInfo: Equatable {
  let binaryURL: URL
  let configURL: URL
  let mode: RoutingMode
}

@MainActor
final class SingBoxProcessManager {
  enum ManagerError: LocalizedError {
    case binaryNotFound
    case processAlreadyRunning
    case launchFailed(String)

    var errorDescription: String? {
      switch self {
      case .binaryNotFound:
        "sing-box не найден. Установи его через Homebrew: brew install sing-box"
      case .processAlreadyRunning:
        "sing-box уже запущен этим приложением"
      case .launchFailed(let message):
        "Не удалось запустить sing-box: \(message)"
      }
    }
  }

  private var process: Process?
  private var stdoutPipe: Pipe?
  private var stderrPipe: Pipe?
  private let generator = SingBoxConfigGenerator()
  private let runtimeDirectory: URL

  var isRunning: Bool {
    process?.isRunning == true
  }

  init(runtimeDirectory: URL? = nil) {
    self.runtimeDirectory = runtimeDirectory ?? Self.defaultRuntimeDirectory
  }

  func start(
    profile: TunnelProfile, mode: RoutingMode, onLog: @escaping @MainActor (String) -> Void,
    onExit: @escaping @MainActor (Int32) -> Void
  ) throws -> SingBoxRuntimeInfo {
    guard process?.isRunning != true else { throw ManagerError.processAlreadyRunning }
    PortGuard.freeLocalPort(2080)
    guard let binaryURL = Self.resolveBinaryURL() else { throw ManagerError.binaryNotFound }

    try FileManager.default.createDirectory(at: runtimeDirectory, withIntermediateDirectories: true)
    let configURL = runtimeDirectory.appendingPathComponent("active-sing-box.json")
    let config = try generator.generate(profile: profile, mode: mode)
    try config.write(to: configURL, atomically: true, encoding: .utf8)

    let stdout = Pipe()
    let stderr = Pipe()
    let proc = Process()
    proc.executableURL = binaryURL
    proc.arguments = ["run", "-c", configURL.path]
    proc.standardOutput = stdout
    proc.standardError = stderr
    proc.terminationHandler = { process in
      let status = process.terminationStatus
      Task { @MainActor in
        onExit(status)
      }
    }

    attachLogHandler(stdout.fileHandleForReading, prefix: "", onLog: onLog)
    attachLogHandler(stderr.fileHandleForReading, prefix: "", onLog: onLog)

    do {
      try proc.run()
    } catch {
      stdout.fileHandleForReading.readabilityHandler = nil
      stderr.fileHandleForReading.readabilityHandler = nil
      throw ManagerError.launchFailed(error.localizedDescription)
    }

    process = proc
    stdoutPipe = stdout
    stderrPipe = stderr
    return SingBoxRuntimeInfo(binaryURL: binaryURL, configURL: configURL, mode: mode)
  }

  func stop() {
    stdoutPipe?.fileHandleForReading.readabilityHandler = nil
    stderrPipe?.fileHandleForReading.readabilityHandler = nil

    guard let process else {
      cleanup()
      return
    }

    if process.isRunning {
      process.terminate()
      for _ in 0..<20 where process.isRunning {
        Thread.sleep(forTimeInterval: 0.05)
      }
      if process.isRunning {
        process.interrupt()
        for _ in 0..<10 where process.isRunning {
          Thread.sleep(forTimeInterval: 0.05)
        }
      }
    }

    PortGuard.freeLocalPort(2080)
    cleanup()
  }

  private func cleanup() {
    process = nil
    stdoutPipe = nil
    stderrPipe = nil
  }

  private func attachLogHandler(
    _ handle: FileHandle, prefix: String, onLog: @escaping @MainActor (String) -> Void
  ) {
    handle.readabilityHandler = { fileHandle in
      let data = fileHandle.availableData
      guard !data.isEmpty, let text = String(data: data, encoding: .utf8) else { return }
      let lines =
        text
        .components(separatedBy: .newlines)
        .map { $0.removingANSIEscapeSequences.trimmingCharacters(in: .whitespacesAndNewlines) }
        .filter { !$0.isEmpty }
      for line in lines {
        Task { @MainActor in
          onLog(prefix + line)
        }
      }
    }
  }

  static func resolveBinaryURL() -> URL? {
    let fileManager = FileManager.default

    // Portable app path: porkn.app/Contents/Resources/bin/sing-box.
    if let resourceURL = Bundle.main.resourceURL {
      let bundled = resourceURL.appendingPathComponent("bin/sing-box")
      if fileManager.isExecutableFile(atPath: bundled.path) {
        return bundled
      }
    }

    // SwiftPM resource bundle path, useful when running from build products.
    #if SWIFT_PACKAGE
      let packageBundled = Bundle.module.resourceURL?.appendingPathComponent("bin/sing-box")
      if let packageBundled, fileManager.isExecutableFile(atPath: packageBundled.path) {
        return packageBundled
      }
    #endif

    // Development fallback only. Release/portable builds should use bundled binary above.
    let candidates = [
      "/opt/homebrew/bin/sing-box",
      "/usr/local/bin/sing-box",
      "/usr/bin/sing-box",
      "/bin/sing-box",
    ]

    if let found = candidates.first(where: { fileManager.isExecutableFile(atPath: $0) }) {
      return URL(fileURLWithPath: found)
    }

    let path = ProcessInfo.processInfo.environment["PATH"] ?? ""
    for directory in path.split(separator: ":") {
      let candidate = URL(fileURLWithPath: String(directory)).appendingPathComponent("sing-box")
        .path
      if fileManager.isExecutableFile(atPath: candidate) {
        return URL(fileURLWithPath: candidate)
      }
    }

    return nil
  }

  private static var defaultRuntimeDirectory: URL {
    FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
      .appendingPathComponent("porkn", isDirectory: true)
      .appendingPathComponent("Runtime", isDirectory: true)
  }
}

extension String {
  fileprivate var removingANSIEscapeSequences: String {
    replacingOccurrences(
      of: "\u{001B}\\[[0-9;]*[A-Za-z]",
      with: "",
      options: .regularExpression
    )
  }
}
