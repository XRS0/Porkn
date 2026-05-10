#if canImport(NetworkExtension)
import NetworkExtension

/// Build-ready skeleton for the future Xcode PacketTunnelProvider extension target.
/// This file is documentation/skeleton only; it is intentionally not part of the SwiftPM app target.
final class PacketTunnelProvider: NEPacketTunnelProvider {
  override func startTunnel(
    options: [String: NSObject]?,
    completionHandler: @escaping (Error?) -> Void
  ) {
    // Future TASK-009:
    // 1. Read sanitized sing-box config from the app group container.
    // 2. Apply NEPacketTunnelNetworkSettings.
    // 3. Start sing-box core or library runtime.
    // 4. Call completionHandler(nil) only after the tunnel is actually ready.
    completionHandler(NEVPNError(.configurationDisabled))
  }

  override func stopTunnel(
    with reason: NEProviderStopReason,
    completionHandler: @escaping () -> Void
  ) {
    // Future TASK-009: stop core runtime, close file descriptors, persist diagnostics.
    completionHandler()
  }
}
#endif
