using Microsoft.Extensions.Configuration;

namespace watchtower.services;

public class LogingService
{
    private readonly string _basePath;

    public LogingService(IConfiguration config)
    {
        _basePath = config.GetValue<string>("LogPath") ?? "/var/log/watchtower";

        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);

        foreach (var service in new[]
        {
            "telegram_notifier",
            "telegram_bot",
            "service_restarter",
            "health_check_service",
            "http_probe",
            "ssh_probe",
            "maintenance"
        })
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

        try
        {
            File.AppendAllText(logFile, line + Environment.NewLine);
        }
        catch { /* не роняем приложение из-за логов */ }

        Console.WriteLine($"[{service}] {line}");
    }

    // health_check_service
    public void Info(string message)    => WriteLog("health_check_service", "INFO", message);
    public void Warning(string message) => WriteLog("health_check_service", "WARN", message);
    public void Error(string message)   => WriteLog("health_check_service", "ERROR", message);

    // произвольный сервис
    public void Info(string service, string message)    => WriteLog(service, "INFO", message);
    public void Warning(string service, string message) => WriteLog(service, "WARN", message);
    public void Error(string service, string message)   => WriteLog(service, "ERROR", message);
}