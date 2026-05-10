import SwiftUI

struct ContentView: View {
  @EnvironmentObject private var profileStore: ProfileStore
  @EnvironmentObject private var tunnelController: TunnelController
  @State private var showingImport = false
  @State private var showingSOCKSForm = false
  @State private var selection: AppSelection?
  @State private var lastSelectedProfileID: TunnelProfile.ID?

  var body: some View {
    NavigationSplitView {
      SidebarView(selection: $selection)
        .navigationSplitViewColumnWidth(min: 250, ideal: 290, max: 360)
    } detail: {
      switch selection {
      case .settings:
        MainSettingsView(
          connectedProfile: tunnelController.currentProfile(in: profileStore.profiles)
        ) { profile in
          Task { await tunnelController.switchTo(profile: profile, mode: .localProxy, force: true) }
        }
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
    .onAppear {
      if selection == nil, let first = profileStore.profiles.first {
        selection = .profile(first.id)
        lastSelectedProfileID = first.id
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
