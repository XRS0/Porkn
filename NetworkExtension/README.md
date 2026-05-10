# porkn Full VPN / TUN MVP

This directory contains the Xcode-ready PacketTunnelProvider source and entitlements for the future Full VPN mode.

## Current behavior

The SwiftPM app still ships with **System Proxy** as the production mode. Full VPN/TUN is visible in the UI, generates a sing-box `tun` config for preview/tests, but fails fast unless the app is rebuilt with a signed PacketTunnelProvider entitlement.

This is intentional: without the entitlement porkn must not create routes, DNS settings, or a partial fake VPN state.

## Xcode target requirements

Create an Xcode project/workspace with:

- macOS app target: `porkn.app`, bundle id `app.porkn.desktop`.
- Network Extension target: `PacketTunnelProvider.appex`, bundle id `app.porkn.desktop.PacketTunnelProvider`.
- Entitlements:
  - `com.apple.developer.networking.networkextension = packet-tunnel-provider`
  - `com.apple.security.application-groups = group.app.porkn.shared`
- Embed the extension in the app target.
- Sign app and extension with the same Team ID.

## Core startup options

1. Prefer sing-box library/framework inside the provider if process launch is blocked by sandboxing.
2. Otherwise start the bundled `sing-box` binary from the extension container.
3. Use `PacketTunnelConfigHandoff` to read the generated sanitized config from the shared app group.

## Local developer run

1. Enroll in Apple Developer Program and enable NetworkExtension capability for both bundle IDs.
2. Add the app group to both targets.
3. Build with Xcode using Developer ID or development signing.
4. Flip `NetworkExtensionSupport.status` behind a build flag only for entitled builds.
5. Verify System Proxy mode still works before testing Full VPN/TUN.
