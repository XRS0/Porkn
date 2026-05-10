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

## 2026-05-10 — TASK-002, TASK-003

### TASK-002 — done
- Добавлено состояние `ConnectionState.switching(from:to)` и busy flag `isTransitioning`.
- `TunnelController.switchTo` теперь выполняет safe reconnect flow: показывает Switching, останавливает старый runtime, восстанавливает proxy, затем запускает новый runtime.
- Добавлен transition token, чтобы старые/параллельные операции не перетирали новое состояние.
- Старый `onExit` sing-box по-прежнему игнорируется через `activeRunID`.
- Добавлен тест `ConnectionStateTests`.

### TASK-003 — done
- Добавлены `ProxyHealthStatus` и `ProxyHealthCheckService`.
- После успешного local proxy connect запускается health check: local listener check + HTTP(S) IP request через proxy.
- UI показывает health card: Not checked, Checking, Protected, Proxy reachable, Remote check failed, Local proxy failed.
- В логах отображается итог health check.
- Добавлены тесты `ProxyHealthCheckServiceTests`.

### Проверки
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test` — passed, 19 tests.
- `./script/build_and_run.sh --verify` — pending in final verification step after this progress entry.


## 2026-05-10 — TASK-004, TASK-005

### TASK-004 — done
- `PingService` теперь измеряет реальную TCP connect latency до host:port через nonblocking socket + timeout.
- В sidebar добавлены действия `Ping All` и `Auto fastest`.
- Рядом с профилями отображается latency/timeout/not checked, результаты сохраняются в profiles store.
- `ProfileStore` умеет параллельно обновлять ping всех профилей и выбирать самый быстрый успешный профиль.
- Добавлены тесты выбора fastest профиля.

### TASK-005 — done
- Routing расширен до presets: Proxy all, Direct RU/SU, Direct selected, Bypass LAN, Custom.
- В Settings → Routing добавлены отдельные группы Direct / Proxy / Block domains, preview правил, быстрые presets и pending changes indicator.
- Добавлены Export JSON / Import from Clipboard для routing settings.
- `SingBoxConfigGenerator` теперь генерирует route rules для direct, proxy-out, block и LAN bypass.
- `Apply & Reconnect` применяет текущие routing-настройки к активному подключению через reconnect flow.
- Добавлены тесты JSON round-trip и генерации proxy/block rules.

### Проверки
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test --filter RoutingSettingsTests` — passed, 4 tests.
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test` — passed, 22 tests.
- `./script/build_and_run.sh --verify` — passed.
- `/Users/rootix/Applications/porkn.app` пересобран и заменён из `dist/porkn.app`.
- `codesign --verify --deep --strict /Users/rootix/Applications/porkn.app` — passed.
- `scutil --proxy` после установки показывает только `FTPPassive : 1`, system proxy от porkn не остался включённым.
