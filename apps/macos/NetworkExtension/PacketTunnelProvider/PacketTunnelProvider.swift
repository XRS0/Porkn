import Foundation
import NetworkExtension

final class PacketTunnelProvider: NEPacketTunnelProvider {
  private var coreProcess: Process?

  override func startTunnel(
    options: [String: NSObject]?,
    completionHandler: @escaping (Error?) -> Void
  ) {
    let settings = NEPacketTunnelNetworkSettings(tunnelRemoteAddress: "127.0.0.1")
    settings.ipv4Settings = NEIPv4Settings(addresses: ["172.19.0.1"], subnetMasks: ["255.255.255.252"])
    settings.ipv4Settings?.includedRoutes = [NEIPv4Route.default()]
    settings.dnsSettings = NEDNSSettings(servers: ["1.1.1.1", "8.8.8.8"])

    setTunnelNetworkSettings(settings) { error in
      if let error {
        completionHandler(error)
        return
      }

      // MVP fallback: the extension target is build-ready for entitled Xcode builds,
      // but core startup is intentionally disabled until signing/app-group handoff is configured.
      completionHandler(NEVPNError(.configurationDisabled))
    }
  }

  override func stopTunnel(
    with reason: NEProviderStopReason,
    completionHandler: @escaping () -> Void
  ) {
    coreProcess?.terminate()
    coreProcess = nil
    completionHandler()
  }
}
