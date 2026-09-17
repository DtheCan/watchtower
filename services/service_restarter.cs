namespace watchtower.services;

public class ServiceRestarter
{
    private readonly LogingService _logger;
    private readonly TelegramNotifier _telegram;
    private readonly ServiceProbe _probe;

    public ServiceRestarter(LogingService logger, TelegramNotifier telegram, ServiceProbe probe)
    {
        _logger = logger;
        _telegram = telegram;
        _probe = probe;
    }

    public async Task RestartServiceAsync(ServiceConfig service)
    {
        _logger.Info($"Перезапуск {service.Name}...");
        await _telegram.SendMessageAsync($"🔄 Сервис «{service.Name}» — попытка перезапуска...");

        try
        {
            bool success = await _probe.RestartAsync(service);

            if (success)
            {
                await Task.Delay(5000);

                var (reachable, isRunning) = await _probe.CheckAsync(service);
                if (reachable && isRunning)
                {
                    _logger.Info($"{service.Name} работает после перезапуска");
                    await _telegram.SendMessageAsync($"🟢 Сервис «{service.Name}» — восстановлен после перезапуска.");
                }
                else
                {
                    _logger.Warning($"{service.Name} всё ещё не работает после перезапуска");
                    await _telegram.SendMessageAsync($"⚠️ Сервис «{service.Name}» — не удалось восстановить.");
                }
            }
            else
            {
                _logger.Error($"Не удалось перезапустить {service.Name}");
                await _telegram.SendMessageAsync($"❌ Сервис «{service.Name}» — команда перезапуска не выполнена.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка перезапуска {service.Name}: {ex.Message}");
            await _telegram.SendMessageAsync($"⚠️ Сервис «{service.Name}» — ошибка перезапуска.");
        }
    }
}