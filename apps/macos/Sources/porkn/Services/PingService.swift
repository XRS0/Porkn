import Darwin
import Foundation

struct PingService {
  var timeout: TimeInterval = 3

  func measure(profile: TunnelProfile) async -> Int? {
    guard let port = profile.serverPort else { return nil }
    return await measure(host: profile.serverHost, port: port)
  }

  func measure(host: String, port: Int) async -> Int? {
    await Task.detached(priority: .utility) {
      blockingTCPConnectLatency(host: host, port: port, timeout: timeout)
    }.value
  }
}

private func blockingTCPConnectLatency(host: String, port: Int, timeout: TimeInterval) -> Int? {
  let start = DispatchTime.now()
  let socketFD = socket(AF_INET, SOCK_STREAM, 0)
  guard socketFD >= 0 else { return nil }
  defer { close(socketFD) }

  let flags = fcntl(socketFD, F_GETFL, 0)
  guard flags >= 0, fcntl(socketFD, F_SETFL, flags | O_NONBLOCK) >= 0 else { return nil }

  var address = sockaddr_in()
  address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
  address.sin_family = sa_family_t(AF_INET)
  address.sin_port = in_port_t(port).bigEndian

  if inet_pton(AF_INET, host, &address.sin_addr) != 1 {
    guard let resolved = resolveIPv4(host: host) else { return nil }
    address.sin_addr = resolved
  }

  let result = withUnsafePointer(to: &address) { pointer in
    pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockaddrPointer in
      Darwin.connect(socketFD, sockaddrPointer, socklen_t(MemoryLayout<sockaddr_in>.size))
    }
  }

  if result == 0 { return elapsedMilliseconds(since: start) }
  guard errno == EINPROGRESS else { return nil }

  var pollDescriptor = pollfd(fd: socketFD, events: Int16(POLLOUT), revents: 0)
  let selected = poll(&pollDescriptor, 1, Int32(timeout * 1000))
  guard selected > 0, (pollDescriptor.revents & Int16(POLLOUT)) != 0 else { return nil }

  var socketError: Int32 = 0
  var length = socklen_t(MemoryLayout<Int32>.size)
  guard getsockopt(socketFD, SOL_SOCKET, SO_ERROR, &socketError, &length) == 0,
    socketError == 0
  else { return nil }

  return elapsedMilliseconds(since: start)
}

private func resolveIPv4(host: String) -> in_addr? {
  var hints = addrinfo(
    ai_flags: 0,
    ai_family: AF_INET,
    ai_socktype: SOCK_STREAM,
    ai_protocol: IPPROTO_TCP,
    ai_addrlen: 0,
    ai_canonname: nil,
    ai_addr: nil,
    ai_next: nil
  )
  var result: UnsafeMutablePointer<addrinfo>?
  guard getaddrinfo(host, nil, &hints, &result) == 0, let result else { return nil }
  defer { freeaddrinfo(result) }
  return result.pointee.ai_addr.withMemoryRebound(to: sockaddr_in.self, capacity: 1) {
    $0.pointee.sin_addr
  }
}

private func elapsedMilliseconds(since start: DispatchTime) -> Int {
  let elapsed = DispatchTime.now().uptimeNanoseconds - start.uptimeNanoseconds
  return max(1, Int(Double(elapsed) / 1_000_000.0))
}
