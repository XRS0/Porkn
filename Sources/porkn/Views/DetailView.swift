import SwiftUI

struct DetailView: View {
  @EnvironmentObject private var tunnelController: TunnelController
  @EnvironmentObject private var profileStore: ProfileStore
  @State private var isPinging = false
  @State private var routingMode: RoutingMode = .localProxy

  let profile: TunnelProfile?
  private let pingService = PingService()

  var body: some View {
    ScrollView {
      VStack(alignment: .leading, spacing: 20) {
        header

        if let profile {
          ConnectionCard(profile: profile, routingMode: routingMode)
          ProfileMetadataCard(profile: profile, isPinging: isPinging) {
            Task { await ping(profile) }
          }
          SingBoxPreviewCard(profile: profile, routingMode: $routingMode)
          RuntimeLogCard(lines: tunnelController.logLines)
          RawConfigCard(rawConfig: profile.rawConfig)
        } else {
          EmptyStateCard()
        }
      }
      .padding(28)
      .frame(maxWidth: 980, alignment: .leading)
      .frame(maxWidth: .infinity, alignment: .center)
    }
    .background(.background)
  }

  private var header: some View {
    VStack(alignment: .leading, spacing: 8) {
      Text(tunnelController.state.title)
        .font(.system(size: 36, weight: .semibold, design: .rounded))
        .foregroundStyle(tunnelController.state.isActive ? .green : .primary)
        .lineLimit(2)
        .minimumScaleFactor(0.82)
      Text(tunnelController.lastLogLine)
        .font(.callout)
        .foregroundStyle(.secondary)
        .lineLimit(3)
        .fixedSize(horizontal: false, vertical: true)
    }
    .frame(maxWidth: .infinity, alignment: .leading)
  }

  private func ping(_ profile: TunnelProfile) async {
    isPinging = true
    let value = await pingService.measure(profile: profile)
    profileStore.updatePing(for: profile.id, milliseconds: value)
    isPinging = false
  }
}

private struct ConnectionCard: View {
  @EnvironmentObject private var tunnelController: TunnelController
  let profile: TunnelProfile
  let routingMode: RoutingMode

  var body: some View {
    VStack(alignment: .leading, spacing: 18) {
      HStack(alignment: .top, spacing: 14) {
        VStack(alignment: .leading, spacing: 7) {
          Text(profile.name)
            .font(.title2.weight(.semibold))
            .lineLimit(2)
            .fixedSize(horizontal: false, vertical: true)
          Text(profile.endpoint)
            .font(.body.monospaced())
            .foregroundStyle(.secondary)
            .lineLimit(2)
            .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)

        ProtocolBadge(profile.proto)
          .fixedSize()
      }

      Button {
        Task { await tunnelController.toggle(profile: profile, mode: routingMode) }
      } label: {
        Label(
          buttonTitle,
          systemImage: tunnelController.state.isActive
            ? "power.circle.fill" : "shield.lefthalf.filled"
        )
        .font(.title3.weight(.semibold))
        .frame(maxWidth: .infinity)
        .padding(.vertical, 12)
      }
      .buttonStyle(.borderedProminent)
      .controlSize(.large)
      .tint(tunnelController.state.isActive ? .green : .blue)
      .disabled(!routingMode.isAvailable && !tunnelController.state.isActive)

      if let note = routingMode.availabilityNote {
        Text(note)
          .font(.caption)
          .foregroundStyle(.orange)
          .fixedSize(horizontal: false, vertical: true)
      }
    }
    .myTunCard(material: .regularMaterial, radius: 22, padding: 22)
  }

  private var buttonTitle: String {
    if tunnelController.state.isActive { return "Отключить" }
    if !routingMode.isAvailable { return "Недоступно" }
    return "Подключить"
  }
}

private struct ProtocolBadge: View {
  let proto: ProxyProtocol

  init(_ proto: ProxyProtocol) {
    self.proto = proto
  }

  var body: some View {
    Label(proto.displayName, systemImage: proto.systemImage)
      .font(.callout.weight(.medium))
      .padding(.horizontal, 12)
      .padding(.vertical, 8)
      .background(.thinMaterial, in: Capsule())
  }
}

private struct ProfileMetadataCard: View {
  let profile: TunnelProfile
  let isPinging: Bool
  let ping: () -> Void

  var body: some View {
    VStack(alignment: .leading, spacing: 14) {
      Text("Параметры")
        .font(.headline)

      VStack(spacing: 10) {
        MetadataRow(title: "Протокол", value: profile.proto.displayName)
        MetadataRow(title: "Сервер", value: profile.endpoint, monospaced: true)
        HStack(alignment: .firstTextBaseline, spacing: 14) {
          Text("Ping")
            .foregroundStyle(.secondary)
            .frame(width: 110, alignment: .leading)
          Text(Formatters.latency(profile.lastPingMilliseconds))
          Button(isPinging ? "Проверяю…" : "Проверить") { ping() }
            .disabled(isPinging)
          Spacer(minLength: 0)
        }
        if let transport = profile.queryItems["type"] ?? profile.queryItems["net"] {
          MetadataRow(title: "Transport", value: transport)
        }
        if let security = profile.queryItems["security"] ?? profile.queryItems["tls"] {
          MetadataRow(title: "Security", value: security)
        }
      }
    }
    .myTunCard(material: .thinMaterial, radius: 18, padding: 18)
  }
}

private struct MetadataRow: View {
  let title: String
  let value: String
  var monospaced = false

  var body: some View {
    HStack(alignment: .firstTextBaseline, spacing: 14) {
      Text(title)
        .foregroundStyle(.secondary)
        .frame(width: 110, alignment: .leading)
      Text(value)
        .font(monospaced ? .body.monospaced() : .body)
        .lineLimit(3)
        .fixedSize(horizontal: false, vertical: true)
        .textSelection(.enabled)
      Spacer(minLength: 0)
    }
  }
}

private struct SingBoxPreviewCard: View {
  let profile: TunnelProfile
  @Binding var routingMode: RoutingMode
  @State private var revealConfig = false
  @State private var revealSensitive = false
  private let generator = SingBoxConfigGenerator()

  var body: some View {
    VStack(alignment: .leading, spacing: 12) {
      Text("Сценарий подключения")
        .font(.headline)

      Picker("Режим", selection: $routingMode) {
        ForEach(RoutingMode.allCases) { mode in
          Text(mode.title).tag(mode).disabled(!mode.isAvailable)
        }
      }
      .pickerStyle(.segmented)

      Text(routingMode.detail)
        .font(.caption)
        .foregroundStyle(.secondary)
        .lineLimit(3)
        .fixedSize(horizontal: false, vertical: true)

      if let availabilityNote = routingMode.availabilityNote {
        Label(availabilityNote, systemImage: "exclamationmark.triangle.fill")
          .font(.caption.weight(.medium))
          .foregroundStyle(.orange)
          .fixedSize(horizontal: false, vertical: true)
      }

      DisclosureGroup("Предпросмотр sing-box JSON", isExpanded: $revealConfig) {
        Button(revealSensitive ? "Скрыть секреты" : "Reveal sensitive data") {
          revealSensitive.toggle()
        }
        .font(.caption.weight(.medium))
        .padding(.top, 8)

        ScrollView(.horizontal) {
          Text(configPreview)
            .font(.caption.monospaced())
            .textSelection(.enabled)
            .padding(.top, 8)
        }
        .frame(maxHeight: 260)
      }
    }
    .myTunCard(material: .thinMaterial, radius: 18, padding: 18)
  }

  private var configPreview: String {
    do {
      let generated = try generator.generate(profile: profile, mode: routingMode)
      return revealSensitive ? generated : SensitiveRedactor.redact(generated)
    } catch {
      return "// \(error.localizedDescription)"
    }
  }
}

private struct RuntimeLogCard: View {
  let lines: [String]
  @State private var revealLogs = true

  var body: some View {
    DisclosureGroup("Логи подключения", isExpanded: $revealLogs) {
      if lines.isEmpty {
        Text("Логи появятся после запуска sing-box.")
          .font(.caption)
          .foregroundStyle(.secondary)
          .padding(.top, 8)
      } else {
        ScrollView([.vertical, .horizontal]) {
          Text(lines.suffix(80).joined(separator: "\n"))
            .font(.caption.monospaced())
            .textSelection(.enabled)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(minHeight: 96, maxHeight: 190)
        .padding(.top, 8)
      }
    }
    .myTunCard(material: .thinMaterial, radius: 18, padding: 18)
  }
}

private struct RawConfigCard: View {
  let rawConfig: String
  @State private var revealRaw = false
  @State private var revealSensitive = false

  var body: some View {
    DisclosureGroup("Исходный конфиг", isExpanded: $revealRaw) {
      Button(revealSensitive ? "Скрыть секреты" : "Reveal sensitive data") {
        revealSensitive.toggle()
      }
      .font(.caption.weight(.medium))
      .padding(.top, 8)

      ScrollView(.horizontal) {
        Text(displayedRawConfig)
          .font(.caption.monospaced())
          .textSelection(.enabled)
          .padding(.top, 8)
      }
      .frame(maxHeight: 160)
    }
    .myTunCard(material: .thinMaterial, radius: 18, padding: 18)
  }

  private var displayedRawConfig: String {
    revealSensitive ? rawConfig : SensitiveRedactor.redact(rawConfig)
  }
}

private struct EmptyStateCard: View {
  var body: some View {
    ContentUnavailableView(
      "Начни с импорта конфига",
      systemImage: "shield.slash",
      description: Text(
        "Поддержим subscription URL, SOCKS proxy, VLESS/Xray-compatible конфиги и затем подключим реальный tunnel через sing-box/NetworkExtension."
      )
    )
    .frame(maxWidth: .infinity, minHeight: 360)
    .myTunCard(material: .regularMaterial, radius: 24, padding: 0)
  }
}
