import AppKit
import SwiftUI

@main
struct PorknApp: App {
  @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
  @StateObject private var profileStore = ProfileStore()
  @StateObject private var tunnelController = TunnelController()

  var body: some Scene {
    WindowGroup("porkn", id: "main") {
      ContentView()
        .environmentObject(profileStore)
        .environmentObject(tunnelController)
        .frame(minWidth: 980, minHeight: 640)
    }
    .commands {
      CommandGroup(after: .newItem) {
        Button("Импортировать конфиг…") {
          NotificationCenter.default.post(name: .showImportSheet, object: nil)
        }
        .keyboardShortcut("i", modifiers: [.command])
      }
    }

    Settings {
      SettingsView()
        .environmentObject(profileStore)
        .environmentObject(tunnelController)
    }

    MenuBarExtra("porkn", systemImage: tunnelController.state.isActive ? "shield.fill" : "shield") {
      MenuBarContentView()
        .environmentObject(profileStore)
        .environmentObject(tunnelController)
    }
  }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
  func applicationDidFinishLaunching(_ notification: Notification) {
    NSApp.setActivationPolicy(.regular)
    NSApp.activate(ignoringOtherApps: true)

    _ = try? SystemProxyManager().restoreSystemProxy()
  }

  func applicationWillTerminate(_ notification: Notification) {
    _ = try? SystemProxyManager().restoreSystemProxy()
  }
}

extension Notification.Name {
  static let showImportSheet = Notification.Name("porkn.showImportSheet")
  static let showSOCKSSheet = Notification.Name("porkn.showSOCKSSheet")
}
