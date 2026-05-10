import SwiftUI

struct SidebarView: View {
  @EnvironmentObject private var profileStore: ProfileStore
  @EnvironmentObject private var tunnelController: TunnelController
  @Binding var selection: AppSelection?

  var body: some View {
    List(selection: $selection) {
      Section("Подписки") {
        if profileStore.subscriptions.isEmpty {
          Text("Нет subscription URL")
            .font(.caption)
            .foregroundStyle(.secondary)
            .lineLimit(2)
        } else {
          ForEach(profileStore.subscriptions) { subscription in
            SubscriptionRow(subscription: subscription)
              .contextMenu {
                Button("Обновить") {
                  Task { _ = try? await profileStore.refresh(subscription) }
                }
                Button(role: .destructive) {
                  profileStore.delete(subscription)
                } label: {
                  Label("Удалить", systemImage: "trash")
                }
              }
          }
        }
      }

      Section("Профили") {
        if profileStore.profiles.isEmpty {
          VStack(alignment: .leading, spacing: 8) {
            Label("Нет конфигов", systemImage: "link.badge.plus")
              .font(.callout.weight(.medium))
            Text("Импортируй subscription URL, VLESS, VMess, Trojan, SS или SOCKS.")
              .font(.caption)
              .foregroundStyle(.secondary)
              .lineLimit(3)
              .fixedSize(horizontal: false, vertical: true)
          }
          .padding(.vertical, 12)
        } else {
          ForEach(profileStore.profiles) { profile in
            ProfileRow(
              profile: profile,
              isConnected: tunnelController.currentProfileID == profile.id
                && tunnelController.state.isActive
            )
            .tag(AppSelection.profile(profile.id))
            .contextMenu {
              Button(role: .destructive) {
                profileStore.delete(profile)
              } label: {
                Label("Удалить", systemImage: "trash")
              }
            }
          }
        }
      }

      Section("Приложение") {
        Label("Settings", systemImage: "gearshape")
          .tag(AppSelection.settings)
      }
    }
    .listStyle(.sidebar)
    .navigationTitle("porkn")
  }
}

private struct SubscriptionRow: View {
  let subscription: Subscription

  var body: some View {
    HStack(spacing: 10) {
      Image(systemName: "arrow.triangle.2.circlepath")
        .foregroundStyle(.secondary)
        .frame(width: 18)
      VStack(alignment: .leading, spacing: 2) {
        Text(subscription.name)
          .lineLimit(2)
          .fixedSize(horizontal: false, vertical: true)
        Text(subscription.url.host() ?? subscription.url.absoluteString)
          .font(.caption)
          .foregroundStyle(.secondary)
          .lineLimit(1)
          .truncationMode(.middle)
      }
    }
  }
}

private struct ProfileRow: View {
  let profile: TunnelProfile
  let isConnected: Bool

  var body: some View {
    HStack(spacing: 10) {
      ZStack(alignment: .bottomTrailing) {
        Image(systemName: profile.proto.systemImage)
          .foregroundStyle(isConnected ? .green : .secondary)
          .frame(width: 20)

        if isConnected {
          Circle()
            .fill(.green)
            .frame(width: 7, height: 7)
            .offset(x: 4, y: 3)
        }
      }
      .frame(width: 22)

      VStack(alignment: .leading, spacing: 3) {
        HStack(spacing: 6) {
          Text(profile.name)
            .lineLimit(2)
            .fixedSize(horizontal: false, vertical: true)

          if isConnected {
            Text("Connected")
              .font(.caption2.weight(.semibold))
              .foregroundStyle(.green)
              .padding(.horizontal, 6)
              .padding(.vertical, 2)
              .background(.green.opacity(0.12), in: Capsule())
              .fixedSize()
          }
        }

        Text("\(profile.proto.displayName) · \(profile.endpoint)")
          .font(.caption)
          .foregroundStyle(isConnected ? .green : .secondary)
          .lineLimit(1)
          .truncationMode(.middle)
      }
    }
  }
}
