# porkn platform apps

The repository is organized by platform so macOS and Windows clients are first-class siblings:

```text
apps/
├── macos/
│   ├── Sources/              # SwiftUI macOS app
│   ├── Tests/                # Swift tests
│   └── NetworkExtension/     # PacketTunnelProvider skeleton and docs
└── windows/
    ├── src/Porkn.Windows/    # WinForms Windows app
    └── scripts/              # Windows packaging scripts
```

Root-level scripts and CI still provide unified build/release entry points.
