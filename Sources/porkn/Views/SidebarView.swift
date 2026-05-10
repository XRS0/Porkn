import SwiftUI

struct SidebarView: View {
  @EnvironmentObject private var profileStore: ProfileStore
  @EnvironmentObject private var tunnelController: TunnelController
  @Binding var selection: AppSelection?
  @AppStorage("profileSortMode") private var sortModeRaw = ProfileSortMode.favoritesFirst.rawValue
  @AppStorage("favoritesOnly") private var favoritesOnly = false
  @State private var searchText = ""

  private var sortMode: ProfileSortMode {
    get { ProfileSortMode(rawValue: sortModeRaw) ?? .favoritesFirst }
    nonmutating set { sortModeRaw = newValue.rawValue }
  }

  private var visibleProfiles: [TunnelProfile] {
    profileStore.filteredProfiles(searchText: searchText, favoritesOnly: favoritesOnly, sortMode: sortMode)
  }

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
                  Task { _ = try? await profileStore.refreshWithSummary(subscription) }
                }
                Button(role: .destructive) {
                  profileStore.delete(subscription)
                } label: {
                  Label("Удалить", systemImage: "trash")
                }
              }
          }
        }

        if let summary = profileStore.lastRefreshSummary {
          RefreshSummaryRow(summary: summary)
        }
      }

      Section("Профили") {
        profileActions

        if profileStore.profiles.isEmpty {
          EmptyProfilesSidebarRow()
        } else if visibleProfiles.isEmpty {
          Text("Ничего не найдено")
            .font(.caption)
            .foregroundStyle(.secondary)
            .padding(.vertical, 10)
        } else {
          ForEach(visibleProfiles) { profile in
            ProfileRow(
              profile: profile,
              subscriptionName: profileStore.subscriptionName(for: profile),
              isConnected: tunnelController.currentProfileID == profile.id
                && tunnelController.state.isActive
            )
            .tag(AppSelection.profile(profile.id))
            .contextMenu {
              Button {
                profileStore.toggleFavorite(profile)
              } label: {
                Label(
                  profile.isFavorite ? "Убрать из Favorites" : "Добавить в Favorites",
                  systemImage: profile.isFavorite ? "star.slash" : "star")
              }
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
    .safeAreaInset(edge: .top) {
      profileSearchControls
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .background(.bar)
    }
    .listStyle(.sidebar)
    .navigationTitle("porkn")
  }

  private var profileSearchControls: some View {
    VStack(spacing: 8) {
      TextField("Search name, host, protocol…", text: $searchText)
        .textFieldStyle(.roundedBorder)

      HStack(spacing: 8) {
        Toggle(isOn: $favoritesOnly) {
          Label("Favorites", systemImage: "star.fill")
        }
        .toggleStyle(.button)
        .controlSize(.small)

        Picker("Sort", selection: Binding(get: { sortMode }, set: { sortMode = $0 })) {
          ForEach(ProfileSortMode.allCases) { mode in
            Text(mode.title).tag(mode)
          }
        }
        .labelsHidden()
        .controlSize(.small)
        .frame(maxWidth: .infinity)
      }
    }
  }

  private var profileActions: some View {
    HStack(spacing: 8) {
      Button {
        Task { await profileStore.pingAll() }
      } label: {
        Label(
          profileStore.isPingingAll ? "Ping…" : "Ping All",
          systemImage: "antenna.radiowaves.left.and.right")
      }
      .disabled(profileStore.profiles.isEmpty || profileStore.isPingingAll)

      Button {
        if let fastest = profileStore.selectFastestProfile() {
          selection = .profile(fastest.id)
        }
      } label: {
        Label("Auto fastest", systemImage: "bolt.fill")
      }
      .disabled(!profileStore.profiles.contains { $0.lastPingMilliseconds != nil })
    }
    .font(.caption.weight(.medium))
    .buttonStyle(.borderless)
    .padding(.vertical, 4)
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
        if let lastRefreshAt = subscription.lastRefreshAt {
          Text("Last refresh: \(lastRefreshAt.formatted(date: .abbreviated, time: .shortened))")
            .font(.caption2)
            .foregroundStyle(.secondary)
        }
      }
    }
  }
}

private struct RefreshSummaryRow: View {
  let summary: SubscriptionRefreshSummary

  var body: some View {
    Label(summary.shortText, systemImage: "checkmark.seal")
      .font(.caption2.weight(.medium))
      .foregroundStyle(.green)
      .lineLimit(3)
      .fixedSize(horizontal: false, vertical: true)
      .padding(.vertical, 6)
  }
}

private struct EmptyProfilesSidebarRow: View {
  var body: some View {
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
  }
}

private struct ProfileRow: View {
  let profile: TunnelProfile
  let subscriptionName: String
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
          if profile.isFavorite {
            Image(systemName: "star.fill")
              .font(.caption2)
              .foregroundStyle(.yellow)
          }
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

        HStack(spacing: 6) {
          Text("\(profile.proto.displayName) · \(profile.endpoint)")
            .lineLimit(1)
            .truncationMode(.middle)

          Spacer(minLength: 4)

          Text(Formatters.latency(profile.lastPingMilliseconds))
            .font(.caption2.monospacedDigit())
            .foregroundStyle(profile.lastPingMilliseconds == nil ? Color.secondary : Color.blue)
            .fixedSize()
        }
        .font(.caption)
        .foregroundStyle(isConnected ? .green : .secondary)

        Text(subscriptionName)
          .font(.caption2)
          .foregroundStyle(.tertiary)
          .lineLimit(1)
      }
    }
  }
}
