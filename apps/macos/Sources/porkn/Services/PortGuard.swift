import Foundation

enum PortGuard {
  static func firstAvailablePort(in range: ClosedRange<Int>, processName: String = "sing-box")
    -> Int?
  {
    for port in range {
      freeLocalPort(port, processName: processName)
      if isLocalPortFree(port) { return port }
    }
    return nil
  }

  static func isLocalPortFree(_ port: Int) -> Bool {
    pidsListening(on: port).isEmpty
  }

  static func freeLocalPort(_ port: Int, processName: String = "sing-box") {
    let pids = pidsListening(on: port)
    for pid in pids {
      guard isExpectedProcess(pid: pid, processName: processName) else { continue }
      _ = run("/bin/kill", ["-TERM", String(pid)])
    }

    if waitUntilFree(port, timeout: 1.2) { return }

    for pid in pidsListening(on: port) {
      guard isExpectedProcess(pid: pid, processName: processName) else { continue }
      _ = run("/bin/kill", ["-KILL", String(pid)])
    }

    _ = waitUntilFree(port, timeout: 1.0)
  }

  static func waitUntilFree(_ port: Int, timeout: TimeInterval) -> Bool {
    let deadline = Date().addingTimeInterval(timeout)
    while Date() < deadline {
      if pidsListening(on: port).isEmpty { return true }
      Thread.sleep(forTimeInterval: 0.05)
    }
    return pidsListening(on: port).isEmpty
  }

  private static func pidsListening(on port: Int) -> [Int32] {
    let output = run("/usr/sbin/lsof", ["-nP", "-iTCP:\(port)", "-sTCP:LISTEN", "-t"])
    return
      output
      .components(separatedBy: .newlines)
      .compactMap { Int32($0.trimmingCharacters(in: .whitespacesAndNewlines)) }
  }

  private static func isExpectedProcess(pid: Int32, processName: String) -> Bool {
    let command = run("/bin/ps", ["-p", String(pid), "-o", "comm="])
      .trimmingCharacters(in: .whitespacesAndNewlines)
    return command.hasSuffix("/\(processName)") || command == processName
  }

  @discardableResult
  private static func run(_ executable: String, _ arguments: [String]) -> String {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: executable)
    process.arguments = arguments
    let pipe = Pipe()
    process.standardOutput = pipe
    process.standardError = Pipe()
    do {
      try process.run()
      process.waitUntilExit()
      return String(data: pipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
    } catch {
      return ""
    }
  }
}
