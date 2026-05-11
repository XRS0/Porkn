# porkn macOS

Native SwiftUI macOS client powered by bundled `sing-box`.

The root `Package.swift` points at this app so existing commands still work from the repository root:

```bash
swift test
./script/build_and_run.sh --verify
./script/package_release.sh
```
