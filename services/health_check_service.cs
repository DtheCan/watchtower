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

    // сервис -> хост доступен по SSH
    private readonly ConcurrentDictionary<string, bool> _hostReachable = new();
    // сервис -> сколько "всё ок" уже отправили после восстановления
    private readonly ConcurrentDictionary<string, int> _healthyNotifyCounter = new();
    // сервис -> был ли он в проблеме на прошлом цикле
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
        if (_maintenance.IsUnderMaintenance(svc.Name)) return;

        bool httpOk = false;
        int? httpCode = null;

        // 1. HTTP (если задан HttpCheck)
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

        // 2. SSH (если задан Ssh)
        if (svc.SshEnabled)
        {
            var (reachable, running) = await _sshProbe.CheckAsync(svc);

            if (!reachable)
            {
                await OnHostUnreachable(svc);
                return;
            }

            if (!running)
            {
                await OnServiceDown(svc);
                return;
            }

            await OnHealthy(svc, httpCode,
                sshReachable: true,
                sshRunning: true,
                httpSuspicious: svc.HttpEnabled && !httpOk);
            return;
        }

        // 3. Ни HTTP, ни SSH — сервис не настроен
        if (!svc.HttpEnabled && !svc.SshEnabled)
        {
            _logger.Warning($"[{svc.Name}] Не заданы ни HttpCheck, ни Ssh — проверка невозможна");
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
        bool wasProblem = _wasProblem.GetValueOrDefault(svc.Name, false);
        _wasProblem[svc.Name] = false;
        _hostReachable[svc.Name] = true;

        _stateStore.Update(new ServiceStateStore.ServiceState
        {
            Name = svc.Name,
            HttpOk = httpCode == (svc.HttpCheck?.ExpectedStatusCode ?? 200),
            HttpCode = httpCode,
            SshOk = sshReachable,
            SshRunning = sshRunning,
            LastCheckUtc = DateTime.UtcNow,
            InMaintenance = false
        });

        _logger.Info($"[OK] {svc.Name}" +
            (httpSuspicious ? " (HTTP-проверка не прошла, SSH OK)" : ""));

        if (!wasProblem) return;

        int sent = _healthyNotifyCounter.GetValueOrDefault(svc.Name, 0);
        if (sent >= _healthyNotifyCount) return;

        _healthyNotifyCounter[svc.Name] = sent + 1;
        await _telegram.SendMessageAsync(
            $"✅ Сервис «{svc.Name}» — работает в штатном режиме.",
            throttle: false);
    }

    private async Task OnHostUnreachable(ServiceConfig svc)
    {
        _wasProblem[svc.Name] = true;
        _hostReachable[svc.Name] = false;
        _healthyNotifyCounter[svc.Name] = 0;

        _stateStore.Update(new ServiceStateStore.ServiceState
        {
            Name = svc.Name,
            HttpOk = false,
            SshOk = false,
            SshRunning = false,
            LastCheckUtc = DateTime.UtcNow,
            InMaintenance = false
        });

        _logger.Warning($"[UNREACHABLE] {svc.Name} — сервер недоступен, повтор через {_unreachableCheckInterval}s");
        await _telegram.SendMessageAsync($"⚠️ Сервис «{svc.Name}» — сервер недоступен.");
    }

    private async Task OnServiceDown(ServiceConfig svc)
    {
        _wasProblem[svc.Name] = true;
        _healthyNotifyCounter[svc.Name] = 0;

        _stateStore.Update(new ServiceStateStore.ServiceState
        {
            Name = svc.Name,
            HttpOk = false,
            SshOk = true,
            SshRunning = false,
            LastCheckUtc = DateTime.UtcNow,
            InMaintenance = false
        });

        _logger.Warning($"[DOWN] {svc.Name} — сервис не работает");
        await _telegram.SendMessageAsync($"🔴 Сервис «{svc.Name}» — не работает.");

        await _restarter.RestartServiceAsync(svc);
    }
}