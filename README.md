# porkn

Нативный macOS VPN/proxy-клиент под кастомный дизайн и будущую Android-версию.

## Текущий статус

MVP с реальным `sing-box` runtime:
- SwiftUI macOS app;
- импорт subscription URL;
- импорт VLESS / VMess / Trojan / Shadowsocks / Hysteria2 / TUIC / SOCKS ссылок;
- ручное добавление SOCKS5 proxy;
- локальное сохранение профилей и подписок;
- bundled `sing-box` внутри `.app` для переносимости между Mac без Homebrew;
- генерация sing-box JSON;
- реальный запуск/остановка `sing-box` процесса;
- local mixed proxy на `127.0.0.1:2080`;
- автоматическое включение/восстановление системного HTTP/HTTPS/SOCKS proxy macOS;
- experimental TUN config generation;
- runtime logs в UI.

## Запуск

```bash
./script/build_and_run.sh
```

Собранное переносимое приложение появляется здесь:

```text
dist/porkn.app
```

Внутри приложения уже лежит core:

```text
dist/porkn.app/Contents/Resources/bin/sing-box
```

## Проверки

```bash
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test
./script/build_and_run.sh --verify
```

## Как пользоваться текущим MVP

1. Импортируй subscription URL или отдельный VLESS/SOCKS/Trojan профиль.
2. Выбери профиль.
3. Оставь режим `System proxy (как v2RayTun)`.
4. Нажми `Подключить`.
5. Приложение запустит bundled `sing-box`, создаст config, поднимет локальный proxy и автоматически включит системные proxy-настройки macOS:

```text
127.0.0.1:2080
```

При отключении porkn восстанавливает прежние proxy-настройки сетевых сервисов macOS.

## Следующий этап

- Production TUN/NetworkExtension путь для режима полноценного VPN, аналогичного системному VPN-сервису v2RayTun.
- Добавить генерацию outbounds для VMess/SS/Hysteria2/TUIC после проверки реального subscription состава.


## Release-сборка

```bash
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer ./script/package_release.sh
```

Скрипт собирает два ad-hoc signed архива:

```text
release/porkn-macos-arm64.zip
release/porkn-macos-x86_64.zip
```

В каждый `.app` вшивается подходящий `sing-box`: arm64 для Apple Silicon и amd64 для Intel Mac.
