using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace watchtower.services;

public class HealthCheckService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly LogingService _logger;
    private readonly ServiceRestarter _restarter;
    private readonly TelegramNotifier _telegram;
    private readonly ServiceProbe _sshProbe;
    private readonly HttpProbe _httpProbe;
    private readonly MaintenanceService _maintenance;
    private readonly ServiceStateStore _stateStore;

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
        ServiceProbe sshProbe,
        HttpProbe httpProbe,
        MaintenanceService maintenance,
        ServiceStateStore stateStore)
    {
        _config = config;
        _logger = logger;
        _restarter = restarter;
        _telegram = telegram;
        _sshProbe = sshProbe;
        _httpProbe = httpProbe;
        _maintenance = maintenance;
        _stateStore = stateStore;

        _checkInterval = _config.GetValue<int>("CheckIntervalSeconds", 5);
        _unreachableCheckInterval = _config.GetValue<int>("UnreachableCheckIntervalSeconds", 120);
        _healthyNotifyCount = _config.GetValue<int>("HealthyNotifyCount", 2);
        _services = _config.GetSection("Services").Get<List<ServiceConfig>>() ?? new();
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
        }

        // 2. SSH
        if (svc.SshEnabled)
        {
            var (reachable, running) = await _sshProbe.CheckAsync(svc);

            if (!reachable) { await OnHostUnreachable(svc); return; }
            if (!running)   { await OnServiceDown(svc); return; }

            await OnHealthy(svc, httpCode,
                sshReachable: true,
                sshRunning: true,
                httpSuspicious: svc.HttpEnabled && !httpOk);
            return;
        }

        // 3. Ни HTTP, ни SSH
        if (!svc.HttpEnabled && !svc.SshEnabled)
        {
            _logger.Warning($"[{svc.LogName}] Не заданы ни HttpCheck, ни Ssh — проверка невозможна");
        }
    }

    // ------- Обработчики состояний -------

    private async Task OnHealthy(
        ServiceConfig svc,
        int? httpCode,
        bool sshReachable = false,
        bool sshRunning = false,
        bool httpSuspicious = false)
    {
        bool wasProblem = _wasProblem.GetValueOrDefault(svc.Key, false);
        _wasProblem[svc.Key] = false;
        _hostReachable[svc.Key] = true;

        _stateStore.Update(new ServiceStateStore.ServiceState
        {
            Key = svc.Key,
            NodeName = svc.NodeName,
            Name = svc.Name,
            Host = svc.Host,
            Port = svc.Port,
            HttpOk = httpCode == (svc.HttpCheck?.ExpectedStatusCode ?? 200),
            HttpCode = httpCode,
            SshOk = sshReachable,
            SshRunning = sshRunning,
            LastCheckUtc = DateTime.UtcNow,
            InMaintenance = false
        });

        _logger.Info($"[OK] {svc.LogName}" +
            (httpSuspicious ? " (HTTP-проверка не прошла, SSH OK)" : ""));

        // "Всё ок" после восстановления — N раз
        if (!wasProblem) return;

        int sent = _healthyNotifyCounter.GetValueOrDefault(svc.Key, 0);
        if (sent >= _healthyNotifyCount) return;

        _healthyNotifyCounter[svc.Key] = sent + 1;
        await _telegram.SendMessageAsync(
            $"✅ Сервис «{svc.DisplayName}» — работает в штатном режиме.",
            throttle: false);
    }

    private async Task OnHostUnreachable(ServiceConfig svc)
    {
        _wasProblem[svc.Key] = true;
        _hostReachable[svc.Key] = false;
        _healthyNotifyCounter[svc.Key] = 0;

        _stateStore.Update(new ServiceStateStore.ServiceState
        {
            Key = svc.Key,
            NodeName = svc.NodeName,
            Name = svc.Name,
            Host = svc.Host,
            Port = svc.Port,
            HttpOk = false,
            SshOk = false,
            SshRunning = false,
            LastCheckUtc = DateTime.UtcNow,
            InMaintenance = false
        });

        _logger.Warning($"[UNREACHABLE] {svc.LogName} — сервер недоступен, повтор через {_unreachableCheckInterval}s");
        await _telegram.SendMessageAsync($"⚠️ Сервис «{svc.DisplayName}» — сервер недоступен.");
    }

    private async Task OnServiceDown(ServiceConfig svc)
    {
        _wasProblem[svc.Key] = true;
        _healthyNotifyCounter[svc.Key] = 0;

        _stateStore.Update(new ServiceStateStore.ServiceState
        {
            Key = svc.Key,
            NodeName = svc.NodeName,
            Name = svc.Name,
            Host = svc.Host,
            Port = svc.Port,
            HttpOk = false,
            SshOk = true,
            SshRunning = false,
            LastCheckUtc = DateTime.UtcNow,
            InMaintenance = false
        });

        _logger.Warning($"[DOWN] {svc.LogName} — сервис не работает");
        await _telegram.SendMessageAsync($"🔴 Сервис «{svc.DisplayName}» — не работает.");

        await _restarter.RestartServiceAsync(svc);
    }
}