import Foundation

@MainActor
final class TunnelController: ObservableObject {
  @Published private(set) var state: ConnectionState = .disconnected
  @Published private(set) var lastLogLine: String = "Готово к подключению"
  @Published private(set) var logLines: [String] = []
  @Published private(set) var runtimeInfo: SingBoxRuntimeInfo?
  @Published private(set) var proxiedServices: [String] = []
  @Published private(set) var healthStatus: ProxyHealthStatus = .notChecked

  private let processManager = SingBoxProcessManager()
  private let systemProxyManager = SystemProxyManager()
  private let healthCheckService = ProxyHealthCheckService()
  private var activeRunID: UUID?
  private var transitionID: UUID?

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
    let transitionID = beginTransition()
    defer { endTransition(transitionID) }

    await connectWithinCurrentTransition(profile, mode: mode, transitionID: transitionID)
  }

  func disconnect() async {
    let transitionID = beginTransition()
    defer { endTransition(transitionID) }

    state = .disconnecting
    stopRuntimeAndRestoreProxy(logPrefix: "Отключение sing-box")
    guard isCurrentTransition(transitionID) else { return }
    state = .disconnected
    appendLog("Отключено")
  }

  func switchTo(profile: TunnelProfile, mode: RoutingMode = .localProxy, force: Bool = false) async
  {
    if currentProfileID == profile.id, state.isActive, !force {
      return
    }

    let transitionID = beginTransition()
    defer { endTransition(transitionID) }

    let previousProfile = currentProfile
    if let previousProfile, state.isActive || state.isTransitioning {
      state = .switching(from: previousProfile, to: profile)
      appendLog("Переключение с \(previousProfile.name) на \(profile.name)")
      stopRuntimeAndRestoreProxy(logPrefix: "Останавливаю старое подключение")
    }

    guard isCurrentTransition(transitionID) else { return }
    await connectWithinCurrentTransition(profile, mode: mode, transitionID: transitionID)
  }

  func currentProfile(in profiles: [TunnelProfile]) -> TunnelProfile? {
    guard let currentProfileID else { return nil }
    return profiles.first { $0.id == currentProfileID }
  }

  var currentProfileID: TunnelProfile.ID? {
    currentProfile?.id
  }

  private var currentProfile: TunnelProfile? {
    switch state {
    case .connecting(let profile), .connected(let profile, _):
      profile
    case .switching(_, let target):
      target
    default:
      nil
    }
  }

  private func connectWithinCurrentTransition(
    _ profile: TunnelProfile, mode: RoutingMode, transitionID: UUID
  ) async {
    guard mode.isAvailable else {
      fail(mode.availabilityNote ?? "Этот режим подключения пока недоступен")
      return
    }

    state = state.isTransitioning ? state : .connecting(profile)
    if case .switching = state {
      appendLog("Подготавливаю \(profile.proto.displayName) конфиг для \(profile.endpoint)")
    } else {
      logLines.removeAll()
      appendLog("Подготавливаю \(profile.proto.displayName) конфиг для \(profile.endpoint)")
    }

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
      guard isCurrentTransition(transitionID) else {
        processManager.stop()
        _ = try? systemProxyManager.restoreSystemProxy()
        return
      }
      state = .connected(profile, connectedAt: Date())
      if mode == .localProxy {
        runHealthCheck(host: info.localProxyHost, port: info.localProxyPort, runID: runID)
      }
    } catch {
      activeRunID = nil
      processManager.stop()
      _ = try? systemProxyManager.restoreSystemProxy()
      proxiedServices = []
      fail(error.localizedDescription)
    }
  }

  private func stopRuntimeAndRestoreProxy(logPrefix: String) {
    activeRunID = nil
    appendLog(logPrefix)
    let restored = (try? systemProxyManager.restoreSystemProxy()) ?? []
    if !restored.isEmpty {
      appendLog("macOS system proxy восстановлен для: \(restored.joined(separator: ", "))")
    }
    proxiedServices = []
    healthStatus = .notChecked
    processManager.stop()
    runtimeInfo = nil
  }

  private func beginTransition() -> UUID {
    let id = UUID()
    transitionID = id
    return id
  }

  private func endTransition(_ id: UUID) {
    if transitionID == id { transitionID = nil }
  }

  private func isCurrentTransition(_ id: UUID) -> Bool {
    transitionID == id
  }

  private func handleProcessExit(status: Int32, runID: UUID) {
    guard activeRunID == runID else { return }
    let wasDisconnecting = state == .disconnecting
    let wasConnectedOrSwitching: Bool
    switch state {
    case .connected, .switching:
      wasConnectedOrSwitching = true
    default:
      wasConnectedOrSwitching = false
    }
    guard wasConnectedOrSwitching || wasDisconnecting else { return }

    appendLog("sing-box завершился с кодом \(status)")
    let restored = (try? systemProxyManager.restoreSystemProxy()) ?? []
    if !restored.isEmpty {
      appendLog("macOS system proxy восстановлен для: \(restored.joined(separator: ", "))")
    }
    proxiedServices = []
    healthStatus = .notChecked
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
    healthStatus = .notChecked
  }

  private func runHealthCheck(host: String, port: Int, runID: UUID) {
    healthStatus = .checking
    appendLog("Проверяю proxy health для \(host):\(port)")
    Task { [weak self] in
      guard let self else { return }
      let status = await healthCheckService.check(host: host, port: port)
      await MainActor.run {
        guard self.activeRunID == runID else { return }
        self.healthStatus = status
        self.appendLog("Health check: \(status.title) — \(status.detail)")
      }
    }
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
