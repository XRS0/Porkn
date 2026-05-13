import AppKit
import SwiftUI

struct ContentView: View {
  @EnvironmentObject private var profileStore: ProfileStore
  @EnvironmentObject private var tunnelController: TunnelController
  @State private var showingImport = false
  @State private var showingSOCKSForm = false
  @State private var selection: AppSelection?
  @State private var lastSelectedProfileID: TunnelProfile.ID?
  @AppStorage("hasCompletedOnboarding") private var hasCompletedOnboarding = false
  @AppStorage("subscriptionAutoRefreshInterval") private var subscriptionAutoRefreshRaw =
    SubscriptionAutoRefreshInterval.off.rawValue
  @AppStorage("refreshSubscriptionsOnLaunch") private var refreshSubscriptionsOnLaunch = false

  var body: some View {
    NavigationSplitView {
      SidebarView(selection: $selection)
        .navigationSplitViewColumnWidth(min: 250, ideal: 290, max: 360)
    } detail: {
      switch selection {
      case .settings:
        MainSettingsView(
          connectedProfile: tunnelController.currentProfile(in: profileStore.profiles),
          reconnectAction: { profile in
            Task { await tunnelController.switchTo(profile: profile, mode: .localProxy, force: true) }
          },
          chainConnectAction: { entry, exit in
            Task { await tunnelController.connectChain(entryProfile: entry, exitProfile: exit) }
          }
        )
      case .profile(let id):
        DetailView(profile: profileStore.profiles.first { $0.id == id })
      case nil:
        DetailView(profile: profileStore.selectedProfile)
      }
    }
    .toolbar {
      ToolbarItemGroup(placement: .primaryAction) {
        Button {
          showingImport = true
        } label: {
          Label("Импорт", systemImage: "plus")
        }

        Button {
          showingSOCKSForm = true
        } label: {
          Label("SOCKS", systemImage: "point.3.connected.trianglepath.dotted")
        }

        Button(role: .destructive) {
          if case .profile(let id) = selection,
            let profile = profileStore.profiles.first(where: { $0.id == id })
          {
            profileStore.delete(profile)
            selection = profileStore.profiles.first.map { .profile($0.id) }
          } else {
            profileStore.deleteSelected()
          }
        } label: {
          Label("Удалить", systemImage: "trash")
        }
        .disabled(!isProfileSelected)
      }
    }
    .sheet(isPresented: $showingImport) {
      ImportConfigView()
        .environmentObject(profileStore)
    }
    .sheet(isPresented: $showingSOCKSForm) {
      AddSOCKSProxyView()
        .environmentObject(profileStore)
    }
    .onReceive(NotificationCenter.default.publisher(for: .showImportSheet)) { _ in
      showingImport = true
    }
    .onReceive(NotificationCenter.default.publisher(for: .showSOCKSSheet)) { _ in
      showingSOCKSForm = true
    }
    .sheet(isPresented: Binding(get: { !hasCompletedOnboarding }, set: { if !$0 { hasCompletedOnboarding = true } })) {
      OnboardingView(
        importAction: {
          hasCompletedOnboarding = true
          showingImport = true
        },
        socksAction: {
          hasCompletedOnboarding = true
          showingSOCKSForm = true
        },
        skipAction: { hasCompletedOnboarding = true }
      )
    }
    .onAppear {
      if selection == nil, let first = profileStore.profiles.first {
        selection = .profile(first.id)
        lastSelectedProfileID = first.id
      }
      Task {
        await profileStore.refreshSubscriptionsIfNeeded(
          interval: SubscriptionAutoRefreshInterval(rawValue: subscriptionAutoRefreshRaw) ?? .off,
          refreshOnLaunch: refreshSubscriptionsOnLaunch)
      }
    }
    .onChange(of: profileStore.selectedProfileID) { _, newValue in
      if selection == nil, let newValue {
        selection = .profile(newValue)
        lastSelectedProfileID = newValue
      }
    }
    .onChange(of: selection) { oldValue, newValue in
      handleSelectionChange(from: oldValue, to: newValue)
    }
  }

  private var isProfileSelected: Bool {
    if case .profile = selection { return true }
    return selection == nil && profileStore.selectedProfile != nil
  }

  private func handleSelectionChange(from oldValue: AppSelection?, to newValue: AppSelection?) {
    guard case .profile(let newID) = newValue else { return }
    guard lastSelectedProfileID != newID else { return }
    lastSelectedProfileID = newID
    profileStore.selectedProfileID = newID
    profileStore.markUsed(newID)

    if tunnelController.state.isActive,
      UserDefaults.standard.bool(forKey: "vpnChainEnabled"),
      let entryID = UUID(uuidString: UserDefaults.standard.string(forKey: "vpnChainEntryProfileID") ?? ""),
      let exitID = UUID(uuidString: UserDefaults.standard.string(forKey: "vpnChainExitProfileID") ?? ""),
      newID == exitID,
      let entry = profileStore.profiles.first(where: { $0.id == entryID }),
      let exit = profileStore.profiles.first(where: { $0.id == exitID })
    {
      Task { await tunnelController.connectChain(entryProfile: entry, exitProfile: exit) }
      return
    }

    guard tunnelController.state.isActive,
      tunnelController.currentProfileID != newID,
      let profile = profileStore.profiles.first(where: { $0.id == newID })
    else {
      return
    }

    Task {
      await tunnelController.switchTo(profile: profile, mode: .localProxy)
    }
  }
}


private struct OnboardingView: View {
  let importAction: () -> Void
  let socksAction: () -> Void
  let skipAction: () -> Void

  var body: some View {
    VStack(alignment: .leading, spacing: 22) {
      HStack(spacing: 14) {
        Image(nsImage: NSApp.applicationIconImage)
          .resizable()
          .frame(width: 58, height: 58)
          .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        VStack(alignment: .leading, spacing: 4) {
          Text("Добро пожаловать в porkn")
            .font(.title2.weight(.semibold))
          Text("Импортируй подписку или добавь SOCKS, выбери сервер и подключайся.")
            .foregroundStyle(.secondary)
        }
      }

      VStack(alignment: .leading, spacing: 12) {
        OnboardingStep(index: 1, title: "Импортируй subscription URL", detail: "porkn обновит профили и покажет diff summary.")
        OnboardingStep(index: 2, title: "Проверь ping и выбери сервер", detail: "Используй Ping All и Auto fastest в sidebar.")
        OnboardingStep(index: 3, title: "Настрой routing", detail: "Direct / Proxy / Block domains доступны в Settings.")
      }

      HStack {
        Button("Позже") { skipAction() }
        Spacer()
        Button("Add SOCKS") { socksAction() }
        Button("Import Subscription") { importAction() }
          .buttonStyle(.borderedProminent)
      }
    }
    .padding(28)
    .frame(width: 560)
  }
}

private struct OnboardingStep: View {
  let index: Int
  let title: String
  let detail: String

  var body: some View {
    HStack(alignment: .top, spacing: 12) {
      Text("\(index)")
        .font(.caption.weight(.bold))
        .foregroundStyle(.white)
        .frame(width: 24, height: 24)
        .background(Color.accentColor, in: Circle())
      VStack(alignment: .leading, spacing: 3) {
        Text(title)
          .font(.callout.weight(.semibold))
        Text(detail)
          .font(.caption)
          .foregroundStyle(.secondary)
      }
    }
  }
}
