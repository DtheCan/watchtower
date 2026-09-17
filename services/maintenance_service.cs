using System.Collections.Concurrent;

namespace watchtower.services;

public class MaintenanceService
{
    private readonly LogingService _logger;

    // ключ = имя сервиса ИЛИ "*" для всех
    private readonly ConcurrentDictionary<string, DateTime> _until = new();

    public MaintenanceService(LogingService logger)
    {
        _logger = logger;
    }

    public void Start(string serviceName, TimeSpan duration)
    {
        var until = DateTime.UtcNow.Add(duration);
        _until[serviceName] = until;

        _logger.Info("maintenance",
            $"ТЕХРАБОТЫ для «{serviceName}» с {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} до {until:yyyy-MM-dd HH:mm:ss} UTC ({duration.TotalMinutes:F0} мин)");
    }

    public void Stop(string serviceName)
    {
        if (_until.TryRemove(serviceName, out var until))
        {
            _logger.Info("maintenance",
                $"ТЕХРАБОТЫ для «{serviceName}» завершены досрочно в {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC (планировалось до {until:yyyy-MM-dd HH:mm:ss})");
        }
    }

    public bool IsUnderMaintenance(string serviceName)
    {
        // "все сервисы"
        if (_until.TryGetValue("*", out var allUntil) && allUntil > DateTime.UtcNow)
            return true;

        if (_until.TryGetValue(serviceName, out var until))
        {
            if (until > DateTime.UtcNow) return true;
            // истёк — убираем
            _until.TryRemove(serviceName, out _);
            _logger.Info("maintenance",
                $"ТЕХРАБОТЫ для «{serviceName}» автоматически завершены в {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        }

        return false;
    }

    public IReadOnlyDictionary<string, DateTime> Active()
    {
        var now = DateTime.UtcNow;
        return _until.Where(kv => kv.Value > now).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}