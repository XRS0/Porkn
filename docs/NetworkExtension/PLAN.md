# porkn NetworkExtension / Full VPN TUN plan

This is the implementation plan for the future Full VPN / TUN mode. The current production path remains **System Proxy** and must not modify macOS routes directly.

## Target architecture

- `porkn.app`: SwiftUI host app, profiles, subscriptions, settings, diagnostics.
- `PacketTunnelProvider.appex`: macOS NetworkExtension with `NEPacketTunnelProvider`.
- Shared config model: app serializes a sanitized sing-box config and passes it to the extension through an app group container.
- Core runtime options:
  1. Start bundled `sing-box` from the extension if allowed by sandbox/signing.
  2. Integrate sing-box as a library/framework if process launch is blocked.
  3. Keep System Proxy fallback when extension entitlement is unavailable.

## Required Apple capabilities

A real Packet Tunnel build requires:

- Apple Developer Program membership.
- Bundle IDs for the app and extension.
- NetworkExtension entitlement with packet tunnel provider capability:
  - `com.apple.developer.networking.networkextension`
  - value containing `packet-tunnel-provider`.
- App Groups entitlement for sharing generated configs and runtime state.
- Developer ID signing for distribution outside the Mac App Store.
- Hardened Runtime and notarization for public releases.

Without these entitlements porkn must show Full VPN / TUN as unavailable/experimental and must not attempt to create routes or start a tunnel provider.

## Signing and release requirements

- Debug builds can compile the SwiftUI app with SwiftPM, but a real extension target needs an Xcode project/workspace.
- Release builds must sign both `porkn.app` and `PacketTunnelProvider.appex` with compatible Team ID and entitlements.
- Notarization must include the nested extension.
- CI release workflow needs secrets for Developer ID certificate and notarytool credentials.

## Safety constraints

- Full VPN mode must not silently fall back to partial routing without telling the user.
- If tunnel startup fails, porkn must leave system proxy/routes/DNS unchanged or restore the previous state.
- Logs and generated configs must redact UUIDs, passwords, subscription tokens, and URL user-info unless the user explicitly reveals them.

## Suggested implementation phases

1. Add unavailable/experimental UI state and redaction. Done in TASK-008.
2. Add Xcode project with app + PacketTunnelProvider target.
3. Add app group config handoff.
4. Generate sing-box TUN config for extension.
5. Start core inside the provider or implement a documented library fallback.
6. Add signed local developer build instructions.
7. Add notarized release pipeline.
