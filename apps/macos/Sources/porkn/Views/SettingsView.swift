import AppKit
import SwiftUI

struct SettingsView: View {
  var isEmbedded = false
  var connectedProfile: TunnelProfile?
  var reconnectAction: ((TunnelProfile) -> Void)?
  @AppStorage("launchAtLogin") private var launchAtLogin = false
  @AppStorage("autoConnectLastProfile") private var autoConnectLastProfile = false
  @AppStorage(KillSwitchPolicy.storageKey) private var killSwitchEnabled = false
  @AppStorage("appLanguage") private var appLanguageRaw = AppLanguage.ru.rawValue
  @AppStorage("preferredCore") private var preferredCore = "sing-box"
  @AppStorage("subscriptionAutoRefreshInterval") private var subscriptionAutoRefreshRaw =
    SubscriptionAutoRefreshInterval.off.rawValue
  @AppStorage("refreshSubscriptionsOnLaunch") private var refreshSubscriptionsOnLaunch = false
  @AppStorage(RoutingSettings.presetStorageKey) private var routingPresetRaw =
    RoutingPreset.directRuSu.rawValue
  @AppStorage(RoutingSettings.directDomainsStorageKey) private var directDomainsText =
    RoutingSettings.defaultDirectDomainsText
  @AppStorage(RoutingSettings.proxyDomainsStorageKey) private var proxyDomainsText = ""
  @AppStorage(RoutingSettings.blockDomainsStorageKey) private var blockDomainsText = ""
  @State private var selectedTab: SettingsTab = .general
  @State private var lastAppliedRoutingSettings = RoutingSettings.current
  @State private var routingImportError: String?
  @State private var updateResult: UpdateCheckResult?
  @State private var updateError: String?
  @State private var isCheckingForUpdates = false
  @State private var isInstallingUpdate = false
  @State private var updateInstallMessage: String?

  var body: some View {
    VStack(alignment: .leading, spacing: 18) {
      SettingsSegmentedTabs(selection: $selectedTab)
        .frame(maxWidth: .infinity)

      Group {
        switch selectedTab {
        case .general:
          generalSettings
        case .routing:
          routingSettings
        }
      }
      .frame(maxWidth: .infinity, minHeight: isEmbedded ? 680 : 430, alignment: .topLeading)
    }
    .frame(maxWidth: .infinity, alignment: .topLeading)
    .padding(isEmbedded ? 0 : 20)
    .frame(minHeight: isEmbedded ? 520 : nil)
    .frame(width: isEmbedded ? nil : 720, height: isEmbedded ? nil : 560)
  }

  private var generalSettings: some View {
    VStack(alignment: .leading, spacing: 16) {
      VPNStyleSettingsCard(
        title: t("Интерфейс", "Interface"),
        subtitle: t("Язык приложения и локальные параметры отображения.", "Application language and local display preferences."),
        systemImage: "globe"
      ) {
        VStack(alignment: .leading, spacing: 10) {
          Picker(t("Язык", "Language"), selection: $appLanguageRaw) {
            ForEach(AppLanguage.allCases) { language in
              Text(language.title).tag(language.rawValue)
            }
          }
          .pickerStyle(.segmented)
          .labelsHidden()

          Text(t("Изменение применяется сразу для новых экранов и настроек.", "The change applies immediately to new screens and settings."))
            .font(.caption)
            .foregroundStyle(.secondary)
        }
      }

      VPNStyleSettingsCard(
        title: t("Безопасность", "Security"),
        subtitle: t("Kill Switch защищает от прямого трафика при аварийном падении runtime.", "Kill Switch protects against direct traffic if the runtime crashes unexpectedly."),
        systemImage: "lock.shield"
      ) {
        SettingToggleRow(
          title: "Kill Switch",
          subtitle: t(
            "Если sing-box неожиданно завершится во время подключения, porkn оставит macOS proxy на локальном endpoint, чтобы трафик не пошёл напрямую. Ручное отключение всё равно восстанавливает proxy.",
            "If sing-box exits unexpectedly while connected, porkn keeps macOS proxy pointed at the local endpoint to prevent direct traffic. Manual disconnect still restores proxy."
          ),
          isOn: $killSwitchEnabled
        )
      }

      VPNStyleSettingsCard(
        title: t("Запуск", "Startup"),
        subtitle: t("Поведение приложения при старте macOS и запуске porkn.", "Behavior when macOS starts and porkn opens."),
        systemImage: "power"
      ) {
        SettingToggleRow(
          title: t("Подключаться к последнему профилю", "Connect to last profile"),
          subtitle: t("Автоматически выбрать и подключить последний сервер при открытии приложения.", "Automatically select and connect the last server when the app opens."),
          isOn: $autoConnectLastProfile
        )

        SettingToggleRow(
          title: t("Запускать porkn при входе", "Launch porkn at login"),
          subtitle: t("Будет доступно после подписанного .app bundle и настройки Login Items.", "Will be available after signed .app bundle and Login Items setup."),
          isOn: $launchAtLogin,
          isDisabled: true
        )
      }

      VPNStyleSettingsCard(
        title: t("Подписки", "Subscriptions"),
        subtitle: t("Автообновление subscription URL и refresh при запуске.", "Auto-refresh subscription URLs and refresh on launch."),
        systemImage: "arrow.triangle.2.circlepath"
      ) {
        VStack(alignment: .leading, spacing: 12) {
          Picker("Auto refresh subscription", selection: $subscriptionAutoRefreshRaw) {
            ForEach(SubscriptionAutoRefreshInterval.allCases) { interval in
              Text(interval.title).tag(interval.rawValue)
            }
          }
          .pickerStyle(.segmented)
          .labelsHidden()

          SettingToggleRow(
            title: t("Обновлять при запуске", "Refresh on app launch"),
            subtitle: t("При открытии porkn сразу проверять subscription URL и показывать diff summary.", "Check subscription URLs when porkn opens and show a diff summary."),
            isOn: $refreshSubscriptionsOnLaunch
          )
        }
      }

      VPNStyleSettingsCard(
        title: t("Обновления", "Updates"),
        subtitle: t("Автообновление через GitHub Releases: porkn скачает подходящий ZIP, проверит SHA256 и заменит приложение.", "In-app updates through GitHub Releases: porkn downloads the matching ZIP, verifies SHA256 and replaces the app."),
        systemImage: "arrow.down.circle"
      ) {
        VStack(alignment: .leading, spacing: 10) {
          Button {
            Task { await checkForUpdates() }
          } label: {
            Label(isCheckingForUpdates ? t("Проверяю…", "Checking…") : t("Проверить обновления", "Check for Updates"), systemImage: "arrow.clockwise")
          }
          .disabled(isCheckingForUpdates || isInstallingUpdate)

          if let updateResult {
            VStack(alignment: .leading, spacing: 8) {
              Text(updateTitle(updateResult))
                .font(.callout.weight(.semibold))
              Text(updateDetail(updateResult))
                .font(.caption)
                .foregroundStyle(.secondary)

              if updateResult.isUpdateAvailable {
                Button {
                  Task { await installUpdate(updateResult) }
                } label: {
                  Label(
                    isInstallingUpdate ? t("Устанавливаю…", "Installing…") : updateResult.canInstall ? t("Скачать и установить", "Download & Install") : t("Открыть релиз", "Open Release"),
                    systemImage: updateResult.canInstall ? "square.and.arrow.down" : "safari"
                  )
                }
                .disabled(isInstallingUpdate)
              }

              Link(t("Открыть страницу релиза", "Open release page"), destination: updateResult.releaseURL)
                .font(.caption.weight(.medium))
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
              (updateResult.isUpdateAvailable ? Color.blue : Color.green).opacity(0.10),
              in: RoundedRectangle(cornerRadius: 14, style: .continuous))
          }

          if let updateInstallMessage {
            Label(updateInstallMessage, systemImage: "arrow.down.circle")
              .font(.caption)
              .foregroundStyle(.secondary)
          }

          if let updateError {
            Label(updateError, systemImage: "exclamationmark.triangle")
              .font(.caption)
              .foregroundStyle(.orange)
          }
        }
      }

      VPNStyleSettingsCard(
        title: t("Ядро", "Core"),
        subtitle: t("Движок, который будет запускаться внутри приложения.", "The core runtime bundled and launched by the app."),
        systemImage: "cpu"
      ) {
        VStack(alignment: .leading, spacing: 10) {
          Picker(t("Движок", "Core"), selection: $preferredCore) {
            Text("sing-box").tag("sing-box")
            Text("Xray-core").tag("xray")
          }
          .pickerStyle(.segmented)
          .labelsHidden()

          Text(
            t("Сейчас используется встроенный sing-box: он лежит внутри porkn.app и переносится вместе с приложением.", "porkn currently uses bundled sing-box inside porkn.app, so it moves with the app.")
          )
          .font(.caption)
          .foregroundStyle(.secondary)
          .fixedSize(horizontal: false, vertical: true)
        }
      }
    }
  }

  private var appLanguage: AppLanguage {
    AppLanguage(rawValue: appLanguageRaw) ?? .ru
  }

  private func t(_ ru: String, _ en: String) -> String {
    L10n.text(ru, en, language: appLanguage)
  }

  private func checkForUpdates() async {
    isCheckingForUpdates = true
    updateError = nil
    updateInstallMessage = nil
    defer { isCheckingForUpdates = false }
    do {
      updateResult = try await UpdateCheckService().check()
    } catch {
      updateError = t("Не удалось проверить обновления", "Failed to check updates") + ": \(error.localizedDescription)"
    }
  }

  private func installUpdate(_ result: UpdateCheckResult) async {
    guard result.canInstall else {
      NSWorkspace.shared.open(result.releaseURL)
      return
    }

    isInstallingUpdate = true
    updateError = nil
    defer { isInstallingUpdate = false }
    do {
      _ = try await UpdateCheckService().downloadAndInstall(result) { message in
        Task { @MainActor in
          updateInstallMessage = localizeUpdateProgress(message)
        }
      }
      NSApp.terminate(nil)
    } catch {
      updateError = t("Не удалось установить обновление", "Failed to install update") + ": \(error.localizedDescription)"
    }
  }

  private func updateTitle(_ result: UpdateCheckResult) -> String {
    result.isUpdateAvailable
      ? t("Доступно обновление: \(result.latestVersion)", "Update available: \(result.latestVersion)")
      : t("porkn обновлён до последней версии", "porkn is up to date")
  }

  private func updateDetail(_ result: UpdateCheckResult) -> String {
    if result.isUpdateAvailable {
      return t(
        "Установлено: \(result.currentVersion). Последняя версия: \(result.latestVersion)." + (result.canInstall ? " Asset: \(result.assetName ?? "ZIP")." : " Asset для этого Mac не найден."),
        "Installed: \(result.currentVersion). Latest: \(result.latestVersion)." + (result.canInstall ? " Asset: \(result.assetName ?? "ZIP")." : " Asset for this Mac was not found.")
      )
    }
    return t("Установлено: \(result.currentVersion).", "Installed: \(result.currentVersion).")
  }

  private func localizeUpdateProgress(_ message: String) -> String {
    switch message {
    case "Downloading update package…":
      t("Скачиваю пакет обновления…", "Downloading update package…")
    case "Verifying SHA256 checksum…":
      t("Проверяю SHA256 checksum…", "Verifying SHA256 checksum…")
    case "Extracting update package…":
      t("Распаковываю обновление…", "Extracting update package…")
    case "Starting updater and closing porkn…":
      t("Запускаю updater и закрываю porkn…", "Starting updater and closing porkn…")
    default:
      message
    }
  }

  private var routingSettings: some View {
    VStack(alignment: .leading, spacing: 16) {
      VPNStyleSettingsCard(
        title: "Routing preset",
        subtitle: "Быстрый выбор базовой стратегии маршрутизации.",
        systemImage: "point.topleft.down.curvedto.point.bottomright.up"
      ) {
        Picker("Routing preset", selection: $routingPresetRaw) {
          ForEach(RoutingPreset.allCases) { preset in
            Text(preset.title).tag(preset.rawValue)
          }
        }
        .pickerStyle(.segmented)
        .labelsHidden()

        Text(currentRoutingPreset.detail)
          .font(.caption)
          .foregroundStyle(.secondary)
          .fixedSize(horizontal: false, vertical: true)

        if hasPendingRoutingChanges {
          Label(
            "Routing changes are pending. Нажми Apply & Reconnect для активного подключения.",
            systemImage: "exclamationmark.circle.fill"
          )
          .font(.caption.weight(.medium))
          .foregroundStyle(.orange)
        }
      }

      VPNStyleSettingsCard(
        title: "Domain groups",
        subtitle: "Direct, Proxy и Block правила генерируются в sing-box route rules.",
        systemImage: "list.bullet.rectangle"
      ) {
        VStack(spacing: 14) {
          DomainGroupEditor(
            title: "Direct domains",
            subtitle: "Идут напрямую в обход proxy",
            text: $directDomainsText
          )
          DomainGroupEditor(
            title: "Proxy domains",
            subtitle: "Явно идут через proxy-out",
            text: $proxyDomainsText
          )
          DomainGroupEditor(
            title: "Block domains",
            subtitle: "Блокируются outbound block",
            text: $blockDomainsText
          )
        }
      }

      VPNStyleSettingsCard(
        title: "Быстрые пресеты",
        subtitle: "Добавь частые правила одной кнопкой.",
        systemImage: "wand.and.stars"
      ) {
        VStack(alignment: .leading, spacing: 12) {
          LazyVGrid(
            columns: [GridItem(.adaptive(minimum: 190), spacing: 10)], alignment: .leading,
            spacing: 10
          ) {
            PresetButton(
              title: "RU/SU зоны", subtitle: "*.ru, *.su → direct",
              systemImage: "globe.europe.africa"
            ) {
              routingPresetRaw = RoutingPreset.directRuSu.rawValue
              directDomainsText = appendingDomains(["*.ru", "*.su"], to: directDomainsText)
            }
            PresetButton(
              title: "Продуктовые", subtitle: "x.com, YouTube, Google", systemImage: "app.badge"
            ) {
              routingPresetRaw = RoutingPreset.directSelected.rawValue
              directDomainsText = appendingDomains(
                [
                  "x.com", "twitter.com", "instagram.com", "facebook.com", "youtube.com",
                  "google.com",
                ], to: directDomainsText)
            }
            PresetButton(
              title: "Bypass LAN", subtitle: "Private IP → direct", systemImage: "network"
            ) {
              routingPresetRaw = RoutingPreset.bypassLan.rawValue
            }
            PresetButton(
              title: "Сбросить", subtitle: "Вернуть дефолт", systemImage: "arrow.counterclockwise",
              role: .destructive
            ) {
              routingPresetRaw = RoutingPreset.directRuSu.rawValue
              directDomainsText = RoutingSettings.defaultDirectDomainsText
              proxyDomainsText = ""
              blockDomainsText = ""
            }
          }

          VStack(alignment: .leading, spacing: 8) {
            HelpChip("*.ru или .ru — все домены зоны .ru")
            HelpChip("x.com — домен и все его поддомены")
            HelpChip("Можно разделять запятыми, пробелами или с новой строки")
          }
        }
      }

      VPNStyleSettingsCard(
        title: "Import / Export",
        subtitle: "Перенос routing settings между устройствами через JSON.",
        systemImage: "square.and.arrow.up.on.square"
      ) {
        HStack(spacing: 10) {
          Button("Copy JSON") { exportRoutingToClipboard() }
          Button("Import from Clipboard") { importRoutingFromClipboard() }
        }
        .buttonStyle(.bordered)

        if let routingImportError {
          Text(routingImportError)
            .font(.caption)
            .foregroundStyle(.red)
        }
      }

      VPNStyleSettingsCard(
        title: "Применить изменения",
        subtitle: reconnectSubtitle,
        systemImage: "arrow.triangle.2.circlepath"
      ) {
        Button {
          lastAppliedRoutingSettings = currentRoutingSettings
          if let connectedProfile {
            reconnectAction?(connectedProfile)
          }
        } label: {
          Label(
            connectedProfile == nil ? "Apply on Next Connect" : "Apply & Reconnect",
            systemImage: "arrow.triangle.2.circlepath"
          )
          .font(.title3.weight(.semibold))
          .frame(maxWidth: .infinity)
          .padding(.vertical, 12)
        }
        .buttonStyle(.borderedProminent)
        .controlSize(.large)
        .tint(connectedProfile == nil ? .secondary : .green)
        .disabled(connectedProfile == nil)
      }

      VPNStyleSettingsCard(
        title: "Предпросмотр правил",
        subtitle: "Так porkn добавит routing в generated sing-box config.",
        systemImage: "doc.text.magnifyingglass"
      ) {
        RoutingPreview(settings: currentRoutingSettings)
      }
    }
  }

  private var currentRoutingPreset: RoutingPreset {
    RoutingPreset(rawValue: routingPresetRaw) ?? .directRuSu
  }

  private var currentRoutingSettings: RoutingSettings {
    RoutingSettings(
      preset: currentRoutingPreset,
      directDomainsText: directDomainsText,
      proxyDomainsText: proxyDomainsText,
      blockDomainsText: blockDomainsText
    )
  }

  private var hasPendingRoutingChanges: Bool {
    currentRoutingSettings != lastAppliedRoutingSettings
  }

  private var reconnectSubtitle: String {
    if let connectedProfile {
      return
        "Сейчас подключён \(connectedProfile.name). Нажми кнопку, чтобы пересобрать sing-box config и применить routing без ручного отключения."
    }
    return
      "Сейчас нет активного подключения. Новые правила автоматически применятся при следующем подключении."
  }

  private func appendingDomains(_ domains: [String], to text: String) -> String {
    var parsed = DomainRuleParser.parse(text).domainSuffix
    for domain in domains {
      let normalized = DomainRuleParser.parse(domain).domainSuffix
      for item in normalized where !parsed.contains(item) {
        parsed.append(item)
      }
    }
    return parsed.map { "*.\($0)" }.joined(separator: "\n")
  }

  private func exportRoutingToClipboard() {
    routingImportError = nil
    do {
      let json = try currentRoutingSettings.exportJSONString()
      NSPasteboard.general.clearContents()
      NSPasteboard.general.setString(json, forType: .string)
    } catch {
      routingImportError = error.localizedDescription
    }
  }

  private func importRoutingFromClipboard() {
    routingImportError = nil
    guard let text = NSPasteboard.general.string(forType: .string), !text.isEmpty else {
      routingImportError = "Clipboard is empty"
      return
    }
    do {
      let settings = try RoutingSettings.importJSONString(text)
      routingPresetRaw = settings.preset.rawValue
      directDomainsText = settings.directDomainsText
      proxyDomainsText = settings.proxyDomainsText
      blockDomainsText = settings.blockDomainsText
    } catch {
      routingImportError = "Invalid routing JSON: \(error.localizedDescription)"
    }
  }
}

private enum SettingsTab: String, CaseIterable, Identifiable {
  case general
  case routing

  var id: String { rawValue }

  var title: String {
    switch self {
    case .general: "Основные"
    case .routing: "Routing"
    }
  }

  var systemImage: String {
    switch self {
    case .general: "gearshape"
    case .routing: "point.topleft.down.curvedto.point.bottomright.up"
    }
  }
}

private struct SettingsSegmentedTabs: View {
  @Binding var selection: SettingsTab

  var body: some View {
    HStack(spacing: 10) {
      ForEach(SettingsTab.allCases) { tab in
        Button {
          selection = tab
        } label: {
          Label(tab.title, systemImage: tab.systemImage)
            .font(.callout.weight(.semibold))
            .frame(maxWidth: .infinity, minHeight: 42)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .frame(maxWidth: .infinity, minHeight: 42)
        .contentShape(Rectangle())
        .foregroundStyle(selection == tab ? .white : .primary)
        .background(
          selection == tab ? Color.accentColor : Color.clear,
          in: RoundedRectangle(cornerRadius: 13, style: .continuous)
        )
        .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 13, style: .continuous))
      }
    }
    .frame(height: 58)
    .myTunCard(material: .regularMaterial, radius: 18, padding: 8)
  }
}

private struct VPNStyleSettingsCard<Content: View>: View {
  let title: String
  let subtitle: String
  let systemImage: String
  @ViewBuilder let content: Content

  var body: some View {
    VStack(alignment: .leading, spacing: 16) {
      HStack(alignment: .top, spacing: 12) {
        Image(systemName: systemImage)
          .font(.title3.weight(.semibold))
          .foregroundStyle(.secondary)
          .frame(width: 28, height: 28)
          .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 9, style: .continuous))

        VStack(alignment: .leading, spacing: 4) {
          Text(title)
            .font(.headline)
            .lineLimit(2)
          Text(subtitle)
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
      }

      content
    }
    .myTunCard(material: .thinMaterial, radius: 18, padding: 18)
  }
}

private struct SettingToggleRow: View {
  let title: String
  let subtitle: String
  @Binding var isOn: Bool
  var isDisabled = false

  var body: some View {
    HStack(alignment: .center, spacing: 12) {
      VStack(alignment: .leading, spacing: 3) {
        Text(title)
          .font(.callout.weight(.medium))
          .lineLimit(2)
        Text(subtitle)
          .font(.caption)
          .foregroundStyle(.secondary)
          .fixedSize(horizontal: false, vertical: true)
      }
      Spacer(minLength: 16)
      Toggle("", isOn: $isOn)
        .labelsHidden()
        .toggleStyle(.switch)
        .disabled(isDisabled)
    }
    .padding(12)
    .background(
      .background.opacity(0.42), in: RoundedRectangle(cornerRadius: 14, style: .continuous)
    )
    .opacity(isDisabled ? 0.62 : 1)
  }
}

private struct PresetButton: View {
  let title: String
  let subtitle: String
  let systemImage: String
  var role: ButtonRole?
  let action: () -> Void

  var body: some View {
    Button(role: role, action: action) {
      HStack(spacing: 10) {
        Image(systemName: systemImage)
          .frame(width: 22)
        VStack(alignment: .leading, spacing: 2) {
          Text(title)
            .font(.callout.weight(.semibold))
          Text(subtitle)
            .font(.caption)
            .foregroundStyle(.secondary)
            .lineLimit(1)
        }
        Spacer(minLength: 0)
      }
      .padding(12)
      .frame(maxWidth: .infinity, minHeight: 58, alignment: .leading)
      .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }
    .buttonStyle(.plain)
  }
}

private struct HelpChip: View {
  let text: String
  init(_ text: String) { self.text = text }

  var body: some View {
    Label(text, systemImage: "checkmark.circle")
      .font(.caption)
      .foregroundStyle(.secondary)
      .lineLimit(2)
      .fixedSize(horizontal: false, vertical: true)
      .padding(.horizontal, 10)
      .padding(.vertical, 7)
      .frame(maxWidth: .infinity, alignment: .leading)
      .background(
        .background.opacity(0.36), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
  }
}

private struct DomainGroupEditor: View {
  let title: String
  let subtitle: String
  @Binding var text: String

  var body: some View {
    VStack(alignment: .leading, spacing: 8) {
      HStack(alignment: .firstTextBaseline) {
        VStack(alignment: .leading, spacing: 2) {
          Text(title)
            .font(.callout.weight(.semibold))
          Text(subtitle)
            .font(.caption)
            .foregroundStyle(.secondary)
        }
        Spacer()
        Text("\(DomainRuleParser.parse(text).domainSuffix.count) rules")
          .font(.caption2.monospacedDigit())
          .foregroundStyle(.secondary)
      }

      TextEditor(text: $text)
        .font(.body.monospaced())
        .scrollContentBackground(.hidden)
        .frame(minHeight: 90)
        .padding(10)
        .background(
          .background.opacity(0.52), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }
  }
}

private struct RoutingPreview: View {
  let settings: RoutingSettings

  var body: some View {
    VStack(alignment: .leading, spacing: 10) {
      PreviewRow(title: "preset", value: settings.preset.title)
      PreviewRow(
        title: "direct domain_suffix", value: previewDomains(settings.effectiveDirectDomainsText))
      PreviewRow(title: "proxy domain_suffix", value: previewDomains(settings.proxyDomainsText))
      PreviewRow(title: "block domain_suffix", value: previewDomains(settings.blockDomainsText))
      PreviewRow(title: "route rules", value: "\(settings.routeRules.count) custom rules")
    }
  }

  private func previewDomains(_ text: String) -> String {
    let parsed = DomainRuleParser.parse(text).domainSuffix
    return parsed.isEmpty ? "—" : parsed.joined(separator: ", ")
  }
}

private struct PreviewRow: View {
  let title: String
  let value: String

  var body: some View {
    VStack(alignment: .leading, spacing: 5) {
      Text(title)
        .font(.caption.weight(.semibold))
        .foregroundStyle(.secondary)
      Text(value)
        .font(.caption.monospaced())
        .textSelection(.enabled)
        .lineLimit(4)
        .fixedSize(horizontal: false, vertical: true)
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
          .background.opacity(0.42), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
  }
}
