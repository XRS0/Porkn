import SwiftUI

struct MenuBarContentView: View {
  @EnvironmentObject private var profileStore: ProfileStore
  @EnvironmentObject private var tunnelController: TunnelController

  var body: some View {
    if let profile = profileStore.selectedProfile {
      Text(profile.name)
      Text(tunnelController.state.title)
      Divider()
      Button(tunnelController.state.isActive ? "Отключить" : "Подключить") {
        Task { await tunnelController.toggle(profile: profile) }
      }
    } else {
      Text("Нет профиля")
      Button("Импортировать…") {
        NotificationCenter.default.post(name: .showImportSheet, object: nil)
      }
    }

    Divider()
    Button("Открыть porkn") {
      NSApp.activate(ignoringOtherApps: true)
    }
    Button("Выход") {
      NSApp.terminate(nil)
    }
  }
}
