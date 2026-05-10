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

## 2026-05-10 — TASK-006, TASK-007

### TASK-006 — done
- Добавлены настройки подписок: Auto refresh subscription — Off / Every 6 hours / Every 12 hours / Daily, плюс Refresh on app launch.
- `ProfileStore` теперь считает refresh diff summary: added / updated / removed / total и показывает последний summary в sidebar.
- Профили получили `isFavorite` и `lastUsedAt` с backward-compatible decoding для старых `profiles.json`.
- В sidebar добавлены поиск по name/host/protocol/subscription, Favorites only и сортировки Favorites first / Fastest first / Name / Recently used.
- Favorites доступны через context menu профиля; ping, favorite и last used сохраняются в profiles store.

### TASK-007 — done
- Главный экран теперь показывает крупный пользовательский статус: Off / Connecting / Protected / Failed / Switching.
- Основная карточка подключения сохраняет текущий сервер, protocol, local proxy endpoint и health check result.
- Runtime logs переименованы в Advanced Logs и скрыты по умолчанию в collapsible блоке.
- Empty state для пустого списка профилей получил кнопки Import Subscription и Add SOCKS.
- Добавлен onboarding первого запуска с шагами импорта, ping/auto fastest и routing settings.

### Проверки
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test --filter ProfileListSettingsTests` — passed, 3 tests.
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test` — passed, 25 tests.
- `./script/build_and_run.sh --verify` — passed.
- `/Users/rootix/Applications/porkn.app` пересобран и заменён из `dist/porkn.app`.
- `codesign --verify --deep --strict /Users/rootix/Applications/porkn.app` — passed.
- `scutil --proxy` после установки показывает только `FTPPassive : 1`, system proxy от porkn не остался включённым.

## 2026-05-10 — TASK-009, TASK-010

### TASK-009 — done
- Добавлен `NetworkExtensionSupport` с явным entitlement gate: Full VPN/TUN доступен только для подписанного build с PacketTunnelProvider entitlement.
- Добавлен build-ready skeleton `NetworkExtension/PacketTunnelProvider` с provider source и entitlements для будущего Xcode target.
- Добавлен `PacketTunnelConfigHandoff` для передачи generated sing-box config через app group в будущий extension.
- `RoutingMode.systemTun` теперь использует NetworkExtension availability status и fail-fast не меняет system proxy/routes в обычном build.
- TUN sing-box config дополнен `stack: system` и `mtu`, тесты проверяют tun inbound / auto_route / strict_route.
- Добавлена developer-инструкция `NetworkExtension/README.md` по app target, extension target, bundle IDs, entitlements и signing.

### TASK-010 — done
- Добавлен `.github/workflows/release.yml` для tag `v*` / manual workflow_dispatch: build arm64/x86_64 artifacts и upload в GitHub Release.
- `script/package_release.sh` теперь прокидывает `APP_VERSION` / tag в Info.plist и собирает `CFBundleShortVersionString` + `CFBundleVersion`.
- Release script теперь быстрее и понятнее падает при проблемах скачивания amd64 sing-box.
- Добавлен `UpdateCheckService`: GitHub Releases latest API, semantic version compare, update result для UI.
- В Settings добавлен блок Updates с кнопкой `Check for Updates`, результатом current/latest version и ссылкой на release page.
- Добавлен план Sparkle rollout в `docs/Release/SPARKLE.md`.

### Проверки
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test --filter NetworkExtensionSupportTests` — passed, 3 tests.
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test --filter UpdateCheckServiceTests` — passed, 2 tests.
- `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test` — passed, 30 tests.
- `./script/build_and_run.sh --verify` — passed.
- `APP_VERSION=0.1.1 DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer ./script/package_release.sh` — passed, generated arm64/x86_64 zips and SHA256SUMS.
- `/Users/rootix/Applications/porkn.app` пересобран и заменён из `dist/porkn.app`.
- `codesign --verify --deep --strict /Users/rootix/Applications/porkn.app` — passed.
- `scutil --proxy` после установки показывает только `FTPPassive : 1`, system proxy от porkn не остался включённым.
