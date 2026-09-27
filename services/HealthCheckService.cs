using System.Collections.Concurrent;
using w2.models;

namespace w2.services;

public class HealthCheckService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly LogingService _logger;
    private readonly ServiceRestarter _restarter;
    private readonly TelegramNotifier _telegram;
    private readonly ServiceProbe _probe;
    private readonly int _checkInterval;
    private readonly int _unreachableCheckInterval;
    private readonly int _healthyNotifyCount;
    private readonly List<ServiceConfig> _services;

    // ключ = svc.Key ("SRV-2:nginx")
    private readonly ConcurrentDictionary<string, bool> _hostReachable = new();
    private readonly ConcurrentDictionary<string, int> _healthyNotifyCounter = new();
    private readonly ConcurrentDictionary<string, bool> _wasProblem = new();

    public HealthCheckService(
        IConfiguration config,
        LogingService logger,
        ServiceRestarter restarter,
        TelegramNotifier telegram,
        ServiceProbe probe)
    {
        _config = config;
        _logger = logger;
        _restarter = restarter;
        _telegram = telegram;
        _probe = probe;

        _checkInterval = _config.GetValue<int>("CheckIntervalSeconds", 30);
        _unreachableCheckInterval = _config.GetValue<int>("UnreachableCheckIntervalSeconds", 120);
        _healthyNotifyCount = _config.GetValue<int>("HealthyNotifyCount", 2);
        _services = _config.GetSection("Services").Get<List<ServiceConfig>>() ?? new List<ServiceConfig>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("HealthCheckService started.");
        _logger.Info($"Мониторинг {_services.Count} сервисов, интервал {_checkInterval}s");

        // Проверим уникальность ключей — иначе два сервиса будут конфликтовать
        var duplicates = _services
            .GroupBy(s => s.Key)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            _logger.Warning($"Дубликаты ключей в конфиге: {string.Join(", ", duplicates)}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var tasks = _services.Select(s => CheckServiceAsync(s, stoppingToken));
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка итерации: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(_checkInterval), stoppingToken);
        }

        _logger.Info("HealthCheckService stopped.");
    }

    private async Task CheckServiceAsync(ServiceConfig svc, CancellationToken ct)
    {
        if (_maintenance.IsUnderMaintenance(svc.Key)) return;

        bool httpOk = false;
        int? httpCode = null;

        // 1. HTTP
        if (svc.HttpEnabled)
        {
            var (reachable, healthy, code, _) = await _httpProbe.CheckAsync(svc, ct);
            httpOk = reachable && healthy;
            httpCode = code;

            if (httpOk)
            {
                await OnHealthy(svc, httpCode);
                return;
            }

            bool wasUnreachable = _hostReachable.TryGetValue(HostKey(service), out var prev) && !prev;
            _hostReachable[HostKey(service)] = true;

            if (wasUnreachable)
            {
                _logger.Info($"[RECOVERED] {logId} — host reachable again.");
                await _telegram.SendMessageAsync($"🟢 Сервис «{tgId}» — сервер снова доступен.");
                _healthyNotifyCounter[HostKey(service)] = 0;
            }

            if (!isRunning)
            {
                _logger.Warning($"[DOWN] {logId} — service is DOWN.");
                _healthyNotifyCounter[HostKey(service)] = 0;
                await _restarter.RestartServiceAsync(service);
            }
            else
            {
                _logger.Info($"[OK] {logId} — service is running.");

                // Отправляем "всё хорошо" только первые N раз
                var key = HostKey(service);
                int sent = _healthyNotifyCounter.GetValueOrDefault(key, 0);
                if (sent < _healthyNotifyCount)
                {
                    _healthyNotifyCounter[key] = sent + 1;
                    await _telegram.SendMessageAsync($"✅ Сервис «{tgId}» — работает в штатном режиме.");
                }
                // иначе — просто пишем в лог (уже сделано выше)
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[CHECK-FAILED] {logId} — {ex.Message}");
            _hostReachable[HostKey(service)] = false;
            _healthyNotifyCounter[HostKey(service)] = 0;
            await _telegram.SendMessageAsync($"⚠️ Сервис «{tgId}» — ошибка проверки: {ex.Message}");
        }
    }
}