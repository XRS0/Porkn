import SwiftUI

struct MainSettingsView: View {
  let connectedProfile: TunnelProfile?
  let reconnectAction: (TunnelProfile) -> Void
  let chainConnectAction: (TunnelProfile, TunnelProfile) -> Void

  var body: some View {
    ScrollView {
      VStack(alignment: .leading, spacing: 22) {
        VStack(alignment: .leading, spacing: 8) {
          Text("Settings")
            .font(.system(size: 38, weight: .semibold, design: .rounded))
          Text("Настройки porkn применяются при следующем подключении.")
            .font(.callout)
            .foregroundStyle(.secondary)
        }

        SettingsView(
          isEmbedded: true, connectedProfile: connectedProfile, reconnectAction: reconnectAction,
          chainConnectAction: chainConnectAction
        )
        .padding(0)
      }
      .padding(28)
      .frame(maxWidth: 980, alignment: .leading)
      .frame(maxWidth: .infinity, alignment: .center)
    }
    .background(.background)
  }
}
