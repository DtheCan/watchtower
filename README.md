[Русский](readme_ru.md)

### English

# Watchtower - Service healthcheck monitoring

## Usage:

### 1. Clone the repo

```bash
git clone https://github.com/DtheCan/watchtower.git
```

### 2. Config

In [watchtower-configuration.json](watchtower-configuration.json)

```json
{
  "CheckIntervalSeconds": 5,
  "UnreachableCheckIntervalSeconds": 120,
  "HealthyNotifyCount": 2,
  "LogPath": "/path/to/logs/",
  "Services": [
    {
      "Name": "service name",
      "Host": "server",
      "Port": 80,
      "Ssh": {
        "SshType": "PasswordAuth",
        "SshUser": "ssh user",
        "SshPassword": "ssh password",
        "SshPort": 2222 // if not 22
      },
      // OR
      "Ssh": {
        "SshType": "PrivateKeyAuth",
        "SshUser": "ssh user",
        "SshKeyFilePath": "/path/to/key/file",
        "SshPassphrase": "key passphrase, if set"
      }
    }
  ],
  "Telegram": {
    "BotToken": "bot token",
    "ChatId": "chat id"
  }
}
```

Parameters:

- CheckIntervalSeconds - check interval, in seconds
- UnreachableCheckIntervalSeconds - check interval when service is unreachable, in seconds
- HealthyNotifyCount - number of healthy notifications before stopping sending them
- LogPath - path to logs
- Services - service list for healthcheck monitoring
  - Name - service name (for systemd) or process name
  - Host - server address
  - Port - port used by the service
  - SshType - `PasswordAuth` or `PrivateKeyAuth` - auth ssh using password or key
  - SshUser - ssh username
  - SshPassword - ssh password
  - SshPort - ssh port
  - SshKeyFilePath - path to key file
  - SshPassphrase - key passphrase, if set
- Telegram - telegram bot settings (not required)
  - BotToken - bot token, you can get it [here](https://t.me/botfather)
  - ChatId - chat id where to send notifications, you can get it [here](https://t.me/getmyid_bot)

Logs are saved:

```
/path/to/logs/
├── health_check_service/
├── service_restarter/
└── telegram_notifier/
```

### 3. Launch

```
docker compose up -d
```

## Build

You can build the project to use without docker
Installed dotnet is required
Build commands:

### Windows

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

### Linux

```
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

## `systemd` — основной вариант

Подходит для всего, что установлено через `apt` / `yum` / `dnf` и управляется systemd.
`Name` = имя unit-файла (без `.service`).

| Категория      | Сервис                        | `Name` в конфиге                    | Порт для проверки |
| -------------- | ----------------------------- | ----------------------------------- | ----------------- |
| Веб-сервер     | Nginx                         | `nginx`                             | 80 / 443          |
| Веб-сервер     | Apache                        | `apache2` (Debian) / `httpd` (RHEL) | 80 / 443          |
| База данных    | PostgreSQL                    | `postgresql`                        | 5432              |
| База данных    | MySQL                         | `mysql`                             | 3306              |
| База данных    | MariaDB                       | `mariadb`                           | 3306              |
| База данных    | MongoDB                       | `mongod`                            | 27017             |
| Кэш            | Redis                         | `redis` / `redis-server`            | 6379              |
| Кэш            | Memcached                     | `memcached`                         | 11211             |
| Очередь        | RabbitMQ                      | `rabbitmq-server`                   | 5672              |
| Очередь        | Kafka                         | `kafka`                             | 9092              |
| Контейнеры     | Docker                        | `docker`                            | 2375 / 2376       |
| Виртуализация  | libvirt                       | `libvirtd`                          | 16509             |
| VPN            | WireGuard                     | `wg-quick@wg0`                      | 51820             |
| VPN            | OpenVPN                       | `openvpn@server`                    | 1194              |
| Прокси         | HAProxy                       | `haproxy`                           | 80 / 443          |
| Прокси         | Traefik (как service)         | `traefik`                           | 80 / 443          |
| Мониторинг     | Prometheus                    | `prometheus`                        | 9090              |
| Мониторинг     | Grafana                       | `grafana-server`                    | 3000              |
| Мониторинг     | Node Exporter                 | `node_exporter`                     | 9100              |
| Логи           | Filebeat                      | `filebeat`                          | —                 |
| Логи           | Logstash                      | `logstash`                          | 5044              |
| Почта          | Postfix                       | `postfix`                           | 25 / 587          |
| Почта          | Dovecot                       | `dovecot`                           | 143 / 993         |
| DNS            | Bind9                         | `bind9` / `named`                   | 53                |
| DNS            | dnsmasq                       | `dnsmasq`                           | 53                |
| DHCP           | ISC DHCP                      | `isc-dhcp-server`                   | 67                |
| Файлы          | Samba                         | `smbd`                              | 445               |
| Файлы          | NFS                           | `nfs-server`                        | 2049              |
| CI/CD          | GitLab Runner                 | `gitlab-runner`                     | —                 |
| CI/CD          | Jenkins (как service)         | `jenkins`                           | 8080              |
| Веб-приложение | Gunicorn (через systemd unit) | `myapp`                             | 8000              |
| Веб-приложение | Uvicorn (через systemd unit)  | `myapp`                             | 8000              |
| Node.js        | PM2 (через systemd)           | `pm2-root`                          | 3000              |

## `process` — только для «сырых» приложений

Используй, **только если сервис не управляется systemd** и ты точно знаешь,
что `pkill -f <Name>` не убьёт лишнее. `Name` = **команда запуска**, а не просто имя.

| Ситуация                   | `Name` в конфиге                           | Порт |
| -------------------------- | ------------------------------------------ | ---- |
| Самописный Python-скрипт   | `/opt/myapp/run.sh`                        | 8000 |
| Python + uvicorn вручную   | `/usr/bin/python3 /opt/app/main.py`        | 8000 |
| Node.js-приложение вручную | `/usr/bin/node /opt/app/index.js`          | 3000 |
| Go-бинарник                | `/opt/myapp/myapp`                         | 8080 |
| Java JAR вручную           | `/usr/bin/java -jar /opt/app/app.jar`      | 8080 |
| Скрипт-обёртка             | `/home/user/start_bot.sh`                  | —    |
| Telegram-бот на Python     | `/opt/bot/venv/bin/python /opt/bot/bot.py` | —    |
| Парсер / воркер            | `/opt/worker/worker`                       | —    |

**Риски:**

- `pkill -f` может убить лишние процессы, если `Name` слишком общий (`python`, `node`, `java`).
- Процесс может умереть вместе с SSH-сессией — нужен `nohup` / `disown` / `setsid`.
- Нет контроля окружения (переменные, рабочий каталог).

## `port` — только мониторинг, без перезапуска

При `Type: "port"` Watchtower **ничего не перезапускает** — только пишет в лог
и шлёт в Telegram «сервис упал». Это безопасный режим «наблюдателя».

| Ситуация                           | Почему `port`                                                             | Что делать при падении                    |
| ---------------------------------- | ------------------------------------------------------------------------- | ----------------------------------------- |
| **Docker-контейнер**               | Watchtower на хосте не может управлять процессом внутри контейнера        | Вручную: `docker restart <container>`     |
| **Docker Compose-стек**            | То же самое — контейнеры живут своей жизнью                               | `docker compose restart <service>`        |
| **Kubernetes-поды**                | Управляются k8s, не systemd                                               | `kubectl rollout restart`                 |
| **Критичная БД**                   | PostgreSQL/MySQL в аварийном режиме могут требовать ручного вмешательства | Проверить логи, потом решать              |
| **Продакшн-сервис с SLA**          | Автоматический рестарт может усугубить проблему                           | Сначала уведомление, потом ручное решение |
| **Чужой сервис**                   | Не твоя ответственность, но хочешь знать о падении                        | Уведомить владельца                       |
| **Тестовый / отладочный процесс**  | Ты сам его перезапускаешь при правках                                     | Игнорировать уведомления                  |
| **Сервис с внешним оркестратором** | Nomad, Swarm, systemd-таймер                                              | Управляется извне                         |
| **Одноразовые задачи**             | Cron-задачи, batch-джобы                                                  | Перезапустит cron сам                     |
| **Лицензионное ПО**                | Рестарт может сломать лицензию                                            | Ручной контроль                           |
