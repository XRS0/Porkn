# porkn Windows

Native Windows companion client for porkn, implemented with .NET 8 WinForms and bundled `sing-box.exe`.

Current Windows mode is System Proxy:

1. Import a subscription URL or direct VLESS/SOCKS/Trojan profile.
2. porkn generates a sing-box config.
3. porkn starts bundled `sing-box.exe`.
4. porkn enables Windows per-user system proxy via `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`.
5. On disconnect/app close, porkn restores the previous proxy settings.

## Build locally on Windows

```powershell
pwsh apps/windows/scripts/package.ps1 -AppVersion 0.3.0
```

Artifact:

```text
release/windows/porkn-windows-x64.zip
```

## Current limitations

- System Proxy mode only.
- No Windows firewall/TUN driver yet.
- UI is intentionally minimal for the first Windows release.
