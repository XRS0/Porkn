import Darwin
import XCTest

@testable import porkn

final class PortGuardTests: XCTestCase {
  func testFirstAvailablePortSkipsOccupiedPort() throws {
    let listener = try TCPTestListener(port: 0)
    let occupiedPort = listener.port
    let selected = PortGuard.firstAvailablePort(in: occupiedPort...(occupiedPort + 1))

    XCTAssertEqual(selected, occupiedPort + 1)
  }
}

private final class TCPTestListener {
  let socketFD: Int32
  let port: Int

  init(port requestedPort: Int) throws {
    let fd = socket(AF_INET, SOCK_STREAM, 0)
    guard fd >= 0 else { throw POSIXError(.EIO) }

    var yes: Int32 = 1
    setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &yes, socklen_t(MemoryLayout<Int32>.size))

    var address = sockaddr_in()
    address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
    address.sin_family = sa_family_t(AF_INET)
    address.sin_port = in_port_t(requestedPort).bigEndian
    address.sin_addr = in_addr(s_addr: inet_addr("127.0.0.1"))

    let bindResult = withUnsafePointer(to: &address) { pointer in
      pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockaddrPointer in
        Darwin.bind(fd, sockaddrPointer, socklen_t(MemoryLayout<sockaddr_in>.size))
      }
    }
    guard bindResult == 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
    guard listen(fd, 1) == 0 else {
      throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
    }

    var storedAddress = sockaddr_in()
    var length = socklen_t(MemoryLayout<sockaddr_in>.size)
    let nameResult = withUnsafeMutablePointer(to: &storedAddress) { pointer in
      pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { sockaddrPointer in
        getsockname(fd, sockaddrPointer, &length)
      }
    }
    guard nameResult == 0 else { throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO) }
    socketFD = fd
    port = Int(UInt16(bigEndian: storedAddress.sin_port))
  }

  deinit {
    close(socketFD)
  }
}
