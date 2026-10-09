# Серверный стек (docker compose)

Все контейнеры работают от непривилегированных пользователей, без capabilities (`cap_drop: [ALL]`)
и с `no-new-privileges`. Порты ниже 1024 внутри caddy, maddy и webmail открываются через sysctl
контейнера `net.ipv4.ip_unprivileged_port_start=0` (нужен Docker 20.10+).

| Сервис | Пользователь | Записываемые тома |
| --- | --- | --- |
| postgres | `postgres` (70) | `pgdata` |
| migrate, seed, server | `app` (1654) | `keys` → `/var/lib/zapara/keys`, `media` → `/var/lib/zapara/media` |
| admin | `www-data` (33), порт 8080 | `storage/`, `bootstrap/cache/` внутри образа |
| caddy, certsync | `caddy` (10001) | `caddy_data`, `caddy_config`, `maddy_certs` |
| maddy | `maddy` (10002) | `maddy_data` (кроме `/data/tls`, его пишет certsync) |
| webmail | `www-data` (33) | `webmail_db` |

Образы caddy, maddy и webmail собираются из `images/*` поверх официальных: в них добавлен пользователь
и права на каталоги данных.

## Переход с root-контейнеров

Тома, созданные прежними root-контейнерами, содержат файлы root. Один раз после обновления:

```sh
cd deploy/server
docker compose down            # тома сохраняются
./fix-volume-owners.sh         # пересобирает образы и меняет владельцев файлов в томах
docker compose up -d postgres admin server caddy
docker compose --profile ops run --rm migrate   # как обычно при обновлении
```

Скрипт можно запускать повторно. Если сервис не стартует с ошибкой доступа к файлу, значит в его
томе остались файлы другого владельца: повторите скрипт.

## Заметки

- Ключи Data Protection должны лежать в томе `keys` (`Web__DataProtectionKeysPath` и, если включена
  админка .NET, `Admin__DataProtectionKeysPath` внутри `/var/lib/zapara/keys`). Домашний каталог
  процесса теперь `/home/app`, он не сохраняется между пересозданиями контейнера, как и прежний `/root`.
- Admin-панель слушает 8080 вместо 80; Caddy проксирует на `admin:8080`.
- Файлы приложения в образах принадлежат root и недоступны для записи процессу.
