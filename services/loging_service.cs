namespace watchtower.services;

public class LogingService
{
    private readonly string _basePath;

    public LogingService(IConfiguration config)
    {
        // Используем путь из конфигурации
        _basePath = config.GetValue<string>("LogPath") ?? "/var/log/watchtower";

        // Создаем основную папку для логов
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);

        // Создаем подпапки для каждого сервиса
        foreach (var service in new[] { "telegram_notifier", "service_restarter", "health_check_service" })
        {
            var dir = Path.Combine(_basePath, service);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }

    private void WriteLog(string service, string level, string message)
    {
        var logDir = Path.Combine(_basePath, service);
        var logFile = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        File.AppendAllText(logFile, line + Environment.NewLine);
        Console.WriteLine(line);
    }

    public void Info(string message) => WriteLog("health_check_service", "INFO", message);
    public void Warning(string message) => WriteLog("health_check_service", "WARN", message);
    public void Error(string message) => WriteLog("health_check_service", "ERROR", message);

    public void Info(string service, string message) => WriteLog(service, "INFO", message);
    public void Warning(string service, string message) => WriteLog(service, "WARN", message);
    public void Error(string service, string message) => WriteLog(service, "ERROR", message);
}