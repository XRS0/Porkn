import SwiftUI

struct ImportConfigView: View {
  @Environment(\.dismiss) private var dismiss
  @EnvironmentObject private var profileStore: ProfileStore
  @State private var configText = ""
  @State private var errorMessage: String?

  var body: some View {
    VStack(alignment: .leading, spacing: 18) {
      VStack(alignment: .leading, spacing: 6) {
        Text("Импорт конфига")
          .font(.title2.weight(.semibold))
        Text(
          "Вставь subscription URL, VLESS/Xray ссылку, SOCKS URL или несколько профилей построчно. Настоящие ключи лучше не коммитить в проект."
        )
        .foregroundStyle(.secondary)
      }

      TextEditor(text: $configText)
        .font(.body.monospaced())
        .frame(minHeight: 220)
        .padding(6)
        .background(.quaternary.opacity(0.25), in: RoundedRectangle(cornerRadius: 12))

      if let errorMessage {
        Label(errorMessage, systemImage: "exclamationmark.triangle")
          .foregroundStyle(.red)
      }

      HStack {
        Button("Вставить из буфера") {
          configText = NSPasteboard.general.string(forType: .string) ?? configText
        }

        Spacer()

        Button("Отмена") { dismiss() }
        Button("Импортировать") { importConfig() }
          .buttonStyle(.borderedProminent)
          .disabled(configText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
      }
    }
    .padding(24)
    .frame(width: 640)
  }

  private func importConfig() {
    do {
      let payload = try profileStore.importConfig(configText)
      if case .subscription(let subscription) = payload {
        Task { _ = try? await profileStore.refresh(subscription) }
      }
      dismiss()
    } catch {
      errorMessage = error.localizedDescription
    }
  }
}
