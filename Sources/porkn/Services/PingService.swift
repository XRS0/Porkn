import Foundation

struct PingService {
  func measure(profile: TunnelProfile) async -> Int {
    // Lightweight placeholder until ICMP/TCP probe strategy is selected.
    let base = abs(profile.endpoint.hashValue % 90)
    try? await Task.sleep(for: .milliseconds(220))
    return 28 + base
  }
}
