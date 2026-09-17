using System.Collections.Concurrent;

namespace watchtower.services;

public class MaintenanceService
{
    private readonly LogingService _logger;

    // ключ = svc.Key ("SRV-2:nginx") ИЛИ "*" для всех
    private readonly ConcurrentDictionary<string, DateTime> _until = new();

    public MaintenanceService(LogingService logger)
    {
        _logger = logger;
    }

    public void Start(string key, TimeSpan duration)
    {
        var until = DateTime.UtcNow.Add(duration);
        _until[key] = until;

        _logger.Info("maintenance",
            $"ТЕХРАБОТЫ для «{key}» с {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} до {until:yyyy-MM-dd HH:mm:ss} UTC ({duration.TotalMinutes:F0} мин)");
    }

    public void Stop(string key)
    {
        if (_until.TryRemove(key, out var until))
        {
            _logger.Info("maintenance",
                $"ТЕХРАБОТЫ для «{key}» завершены досрочно в {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC (планировалось до {until:yyyy-MM-dd HH:mm:ss})");
        }
    }

    public bool IsUnderMaintenance(string key)
    {
        if (_until.TryGetValue("*", out var allUntil) && allUntil > DateTime.UtcNow)
            return true;

        if (_until.TryGetValue(key, out var until))
        {
            if (until > DateTime.UtcNow) return true;
            _until.TryRemove(key, out _);
            _logger.Info("maintenance",
                $"ТЕХРАБОТЫ для «{key}» автоматически завершены в {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        }

        return false;
    }

    public IReadOnlyDictionary<string, DateTime> Active()
    {
        var now = DateTime.UtcNow;
        return _until.Where(kv => kv.Value > now).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}