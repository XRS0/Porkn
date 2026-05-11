import Foundation

struct ProxyHealthCheckService {
  var ipCheckURL = URL(string: "https://api.ipify.org?format=json")!
  var timeout: TimeInterval = 8

  func check(host: String, port: Int) async -> ProxyHealthStatus {
    guard isLocalProxyListening(host: host, port: port) else {
      return .localProxyFailed("Локальный proxy \(host):\(port) не слушает")
    }

    do {
      let proxyIP = try await fetchProxyIP(host: host, port: port)
      if let proxyIP, !proxyIP.isEmpty {
        return .protected(proxyIP: proxyIP)
      }
      return .proxyReachable
    } catch {
      return .remoteCheckFailed(error.localizedDescription)
    }
  }

  func isLocalProxyListening(host: String, port: Int) -> Bool {
    let socketFD = socket(AF_INET, SOCK_STREAM, 0)
    guard socketFD >= 0 else { return false }
    defer { close(socketFD) }

    var address = sockaddr_in()
    address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
    address.sin_family = sa_family_t(AF_INET)
    address.sin_port = in_port_t(port).bigEndian
    address.sin_addr = in_addr(s_addr: inet_addr(host))

    let result = withUnsafePointer(to: &address) { pointer in
      pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockaddrPointer in
        Darwin.connect(socketFD, sockaddrPointer, socklen_t(MemoryLayout<sockaddr_in>.size))
      }
    }
    return result == 0
  }

  private func fetchProxyIP(host: String, port: Int) async throws -> String? {
    let configuration = URLSessionConfiguration.ephemeral
    configuration.timeoutIntervalForRequest = timeout
    configuration.timeoutIntervalForResource = timeout
    configuration.connectionProxyDictionary = [
      "HTTPEnable": 1,
      "HTTPProxy": host,
      "HTTPPort": port,
      "HTTPSEnable": 1,
      "HTTPSProxy": host,
      "HTTPSPort": port,
    ]

    let session = URLSession(configuration: configuration)
    let (data, response) = try await session.data(from: ipCheckURL)
    guard let httpResponse = response as? HTTPURLResponse,
      (200..<300).contains(httpResponse.statusCode)
    else {
      throw URLError(.badServerResponse)
    }

    if let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
      let ip = object["ip"] as? String
    {
      return ip
    }
    return String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines)
  }
}
