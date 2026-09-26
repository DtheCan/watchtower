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
      },
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