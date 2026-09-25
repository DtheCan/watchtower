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

    // host -> последнее известное состояние доступности
    private readonly ConcurrentDictionary<string, bool> _hostReachable = new();
    // host -> сколько раз уже отправили "всё хорошо"
    private readonly ConcurrentDictionary<string, int> _healthyNotifyCounter = new();

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
        _services = _config.GetSection("Services").Get<List<ServiceConfig>>() ?? [];
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("HealthCheckService started.");
        _logger.Info($"Monitoring {_services.Count} services...");
        _logger.Info($"CheckInterval={_checkInterval}s, UnreachableCheckInterval={_unreachableCheckInterval}s, HealthyNotifyCount={_healthyNotifyCount}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var reachableTasks = _services
                .Where(s => _hostReachable.GetValueOrDefault(HostKey(s), true))
                .Select(s => CheckServiceAsync(s, stoppingToken, isSlowProbe: false));

            var unreachableTasks = _services
                .Where(s => !_hostReachable.GetValueOrDefault(HostKey(s), true))
                .Select(s => CheckServiceAsync(s, stoppingToken, isSlowProbe: true));

            try
            {
                await Task.WhenAll(reachableTasks.Concat(unreachableTasks));
            }
            catch (Exception ex)
            {
                _logger.Error($"Iteration error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(_checkInterval), stoppingToken);
        }

        _logger.Info("HealthCheckService stopped.");
    }

    private static string HostKey(ServiceConfig s) => $"{s.Host}:{s.Port}";

    private async Task CheckServiceAsync(ServiceConfig service, CancellationToken ct, bool isSlowProbe)
    {
        var logId = $"{service.Name} ({service.Host}:{service.Port})";
        var tgId = service.Name;

        try
        {
            if (!isSlowProbe && !_hostReachable.GetValueOrDefault(HostKey(service), true))
                return;

            if (isSlowProbe)
                await Task.Delay(TimeSpan.FromSeconds(_unreachableCheckInterval), ct);

            var (reachable, isRunning) = await _probe.CheckAsync(service);

            if (!reachable)
            {
                _hostReachable[HostKey(service)] = false;
                _healthyNotifyCounter[HostKey(service)] = 0; // сброс счётчика "хорошо"
                _logger.Warning($"[UNREACHABLE] {logId} — host is not reachable. Will retry in {_unreachableCheckInterval}s.");
                await _telegram.SendMessageAsync($"⚠️ Сервис «{tgId}» — сервер недоступен, повтор через {_unreachableCheckInterval}с.");
                return;
            }

            var wasUnreachable = _hostReachable.TryGetValue(HostKey(service), out var prev) && !prev;
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
                var sent = _healthyNotifyCounter.GetValueOrDefault(key, 0);
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