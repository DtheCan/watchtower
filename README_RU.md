[English](readme.md) 
### Русский
# Watchtower - Мониторинг и контроль отказоустойчивости сервисов

## Использование: 
### 1. Клонируем репозиторий
```bash
git clone https://github.com/DtheCan/watchtower.git
```

### 2. Настраиваем конфигурацию
В файле [watchtower-configuration.json](watchtower-configuration.json)
```json
{
  "CheckIntervalSeconds": 5,
  "UnreachableCheckIntervalSeconds": 120,
  "HealthyNotifyCount": 2,
  "LogPath": "/путь/к/логам/",
  "Services": [
    {
      "Name": "название сервиса",
      "Host": "сервер",
      "Port": 80,
      "Ssh": {
        "SshType": "PasswordAuth",
        "SshUser": "ssh пользователь",
        "SshPassword": "ssh пароль",
        "SshPort": 2222 // если используется не 22 порт
      },
      // ИЛИ
      "Ssh": {
        "SshType": "PrivateKeyAuth",
        "SshUser": "ssh пользователь",
        "SshKeyFilePath": "/путь/к/файлу/ключа",
        "SshPassphrase": "пароль ключа, если есть"
      },
    }
  ],
  "Telegram": {
    "BotToken": "токен бота",
    "ChatId": "id чата"
  }
}
```

Параметры:
- CheckIntervalSeconds - интервал проверки в секундах
- UnreachableCheckIntervalSeconds - интервал проверки сервера если не удалось подключиться в секундах
- HealthyNotifyCount - количество сообщений о нормальном подключении в боте, после которой отправка сообщения прекращается
- LogPath - путь для хранения логов
- Services - список сервисов для мониторинга и контроля
    - Name - имя сервиса (для systemd) или имя процесса
    - Host - адрес сервера
    - Port - порт на котором развернут сервис
    - SshType - `PasswordAuth` или `PrivateKeyAuth` - ssh аутентификация по паролю или по ключу
    - SshUser - ssh имя пользователя сервера
    - SshPassword - ssh пароль сервера
    - SshPort - ssh порт сервера
    - SshKeyFilePath - путь к ssh ключу
    - SshPassphrase - пароль ssh ключа, если использовался при создании
- Telegram - настройки Telegram бота (не обязательны)
    - BotToken - токен бота для уведомлений в Телеграме (можно получить [здесь](https://t.me/botfather))
    - ChatId - id чата в который бот отправляет сообщения (можно получить [здесь](https://t.me/getmyid_bot))

Логи сохраняются:
```
/путь/к/логам/
├── health_check_service/
├── service_restarter/
└── telegram_notifier/
```

### 3. Запуск
```
docker compose up -d
```

## Сборка
Есть возможность собрать проект для запуска без докера
Необходимо иметь установленный dotnet
Команды для сборки:
### Windows
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```
### Linux
```
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```