import SwiftUI

struct AddSOCKSProxyView: View {
  @Environment(\.dismiss) private var dismiss
  @EnvironmentObject private var profileStore: ProfileStore

  @State private var name = "SOCKS Proxy"
  @State private var host = "127.0.0.1"
  @State private var port = "1080"
  @State private var username = ""
  @State private var password = ""
  @State private var errorMessage: String?

  var body: some View {
    Form {
      Section("SOCKS5 сервер") {
        TextField("Название", text: $name)
        TextField("Host", text: $host)
        TextField("Port", text: $port)
        TextField("Username", text: $username)
        SecureField("Password", text: $password)
      }

      if let errorMessage {
        Label(errorMessage, systemImage: "exclamationmark.triangle")
          .foregroundStyle(.red)
      }

      HStack {
        Spacer()
        Button("Отмена") { dismiss() }
        Button("Добавить") { add() }
          .buttonStyle(.borderedProminent)
      }
    }
    .padding(24)
    .frame(width: 460)
  }

  private func add() {
    guard let portNumber = Int(port), (1...65535).contains(portNumber) else {
      errorMessage = "Порт должен быть числом от 1 до 65535"
      return
    }
    let cleanHost = host.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !cleanHost.isEmpty else {
      errorMessage = "Host не должен быть пустым"
      return
    }
    profileStore.addManualSOCKS(
      name: name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "SOCKS Proxy" : name,
      host: cleanHost,
      port: portNumber,
      username: username.nilIfBlank,
      password: password.nilIfBlank
    )
    dismiss()
  }
}

extension String {
  fileprivate var nilIfBlank: String? {
    let value = trimmingCharacters(in: .whitespacesAndNewlines)
    return value.isEmpty ? nil : value
  }
}
