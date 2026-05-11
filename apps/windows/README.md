# porkn Windows

Native Windows companion client for porkn, implemented with .NET 8 Avalonia UI and bundled `sing-box.exe`.

Current Windows mode is System Proxy:

1. Import a subscription URL or direct VLESS/SOCKS/Trojan/VMess profile.
2. porkn stores subscriptions/profiles and refreshes subscriptions with upsert behavior.
3. porkn generates a sing-box config, including routing settings.
4. porkn starts bundled `sing-box.exe`.
5. porkn enables Windows per-user system proxy via `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`.
6. porkn checks local proxy reachability and remote proxy IP.
7. On manual disconnect/app close, porkn restores the previous proxy settings.

## UI

The Windows UI is built with Avalonia and follows the macOS client structure:

- sidebar with subscriptions, search, profile rows, Favorites and sort controls;
- profile detail screen with status header, connection card, metadata, sing-box preview, logs and raw config;
- Settings screen with General and Routing sections;
- Apply & Reconnect for routing changes;
- porkn app icon bundled in the app package.

## Build locally on Windows

```powershell
pwsh apps/windows/scripts/package.ps1 -AppVersion 0.3.2
```

Artifact:

```text
release/windows/porkn-windows-x64.zip
```

## Current limitations

- System Proxy mode only.
- No Windows firewall/TUN driver yet.
- Windows code signing / installer are not implemented yet.
