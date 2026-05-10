## 2026-05-10 — TASK-001, TASK-008

### TASK-001 — done
- Добавлен динамический выбор local proxy port из диапазона 2080...2090.
- sing-box config теперь получает фактический listen_port.
- System proxy включается на фактически выбранный endpoint.
- Orphan cleanup выключает proxy porkn на всём управляемом диапазоне портов.
- Добавлены тесты PortGuard и generator/system proxy detection.

### TASK-008 — done
- Добавлен план NetworkExtension / PacketTunnelProvider в docs/NetworkExtension/PLAN.md.
- Добавлен skeleton PacketTunnelProvider как документационный template, не ломающий SwiftPM сборку.
- Full VPN / TUN помечен как unavailable/experimental без entitlements; подключение в этом режиме fail-fast без изменения runtime/proxy.
- Добавлен SensitiveRedactor для маскирования UUID/password/token/userinfo в logs/raw config/config preview.
- Добавлены тесты redaction и unavailable TUN guard.

### Проверки
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test` — passed, 16 tests.
- `swift test --filter PortGuardTests` — passed.
- `swift test --filter SensitiveRedactorTests` — passed.
- `swift test --filter TunnelControllerTests` — passed.
- `./script/build_and_run.sh --verify` — passed.
- `scutil --proxy` после verify показывает только `FTPPassive : 1`, system proxy не остался включённым.

