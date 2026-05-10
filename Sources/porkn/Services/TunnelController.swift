import Foundation

@MainActor
final class TunnelController: ObservableObject {
  @Published private(set) var state: ConnectionState = .disconnected
  @Published private(set) var lastLogLine: String = "Готово к подключению"
  @Published private(set) var logLines: [String] = []
  @Published private(set) var runtimeInfo: SingBoxRuntimeInfo?
  @Published private(set) var proxiedServices: [String] = []

  private let processManager = SingBoxProcessManager()
  private let systemProxyManager = SystemProxyManager()
  private var activeRunID: UUID?

  func toggle(profile: TunnelProfile?, mode: RoutingMode = .localProxy) async {
    if state.isActive {
      await disconnect()
    } else if let profile {
      await connect(profile, mode: mode)
    } else {
      fail("Добавь или выбери конфиг перед подключением")
    }
  }

  func connect(_ profile: TunnelProfile, mode: RoutingMode = .localProxy) async {
    guard mode.isAvailable else {
      fail(mode.availabilityNote ?? "Этот режим подключения пока недоступен")
      return
    }

    state = .connecting(profile)
    logLines.removeAll()
    appendLog("Подготавливаю \(profile.proto.displayName) конфиг для \(profile.endpoint)")

    do {
      let runID = UUID()
      activeRunID = runID
      let info = try processManager.start(profile: profile, mode: mode) { [weak self] line in
        self?.appendLog(line)
      } onExit: { [weak self] status in
        self?.handleProcessExit(status: status, runID: runID)
      }
      runtimeInfo = info
      switch mode {
      case .localProxy:
        proxiedServices = try systemProxyManager.enableSystemProxy(
          host: info.localProxyHost, port: info.localProxyPort)
        appendLog("macOS system proxy включён для: \(proxiedServices.joined(separator: ", "))")
        appendLog("sing-box запущен. Системный proxy: \(info.localProxyEndpoint)")
      case .systemTun:
        appendLog("sing-box запущен в TUN-режиме")
      }
      state = .connected(profile, connectedAt: Date())
    } catch {
      activeRunID = nil
      processManager.stop()
      _ = try? systemProxyManager.restoreSystemProxy()
      proxiedServices = []
      fail(error.localizedDescription)
    }
  }

  func disconnect() async {
    state = .disconnecting
    activeRunID = nil
    appendLog("Отключение sing-box")
    let restored = (try? systemProxyManager.restoreSystemProxy()) ?? []
    if !restored.isEmpty {
      appendLog("macOS system proxy восстановлен для: \(restored.joined(separator: ", "))")
    }
    proxiedServices = []
    processManager.stop()
    runtimeInfo = nil
    state = .disconnected
    appendLog("Отключено")
  }

  func switchTo(profile: TunnelProfile, mode: RoutingMode = .localProxy, force: Bool = false) async
  {
    if currentProfileID == profile.id, state.isActive, !force {
      return
    }

    if state.isActive || state == .disconnecting {
      appendLog("Переключение на \(profile.name)")
      await disconnect()
    }

    await connect(profile, mode: mode)
  }

  func currentProfile(in profiles: [TunnelProfile]) -> TunnelProfile? {
    guard let currentProfileID else { return nil }
    return profiles.first { $0.id == currentProfileID }
  }

  var currentProfileID: TunnelProfile.ID? {
    switch state {
    case .connecting(let profile), .connected(let profile, _):
      profile.id
    default:
      nil
    }
  }

  private func handleProcessExit(status: Int32, runID: UUID) {
    guard activeRunID == runID else { return }
    let wasDisconnecting = state == .disconnecting
    let wasConnected: Bool
    if case .connected = state {
      wasConnected = true
    } else {
      wasConnected = false
    }
    guard wasConnected || wasDisconnecting else { return }

    appendLog("sing-box завершился с кодом \(status)")
    let restored = (try? systemProxyManager.restoreSystemProxy()) ?? []
    if !restored.isEmpty {
      appendLog("macOS system proxy восстановлен для: \(restored.joined(separator: ", "))")
    }
    proxiedServices = []
    if !wasDisconnecting {
      state = status == 0 ? .disconnected : .failed("sing-box завершился с кодом \(status)")
    }
    runtimeInfo = nil
    activeRunID = nil
  }

  private func fail(_ message: String) {
    state = .failed(message)
    appendLog(message)
    runtimeInfo = nil
    activeRunID = nil
    proxiedServices = []
  }

  private func appendLog(_ line: String) {
    let safeLine = SensitiveRedactor.redact(line)
    lastLogLine = safeLine
    logLines.append(safeLine)
    if logLines.count > 200 {
      logLines.removeFirst(logLines.count - 200)
    }
  }
}
