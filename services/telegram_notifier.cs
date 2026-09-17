using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;

namespace watchtower.services;

public class TelegramNotifier
{
    private readonly TelegramBotClient _bot;
    private readonly string _chatId;
    private readonly LogingService _logger;

    // ключ = текст сообщения, значение = последнее время отправки
    private readonly ConcurrentDictionary<string, DateTime> _lastSent = new();
    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(30);

    public TelegramNotifier(IConfiguration config, LogingService logger)
    {
        var token = config["Telegram:BotToken"];
        _chatId = config["Telegram:ChatId"] ?? string.Empty;
        _bot = new TelegramBotClient(token ?? string.Empty);
        _logger = logger;
    }

    public TelegramBotClient Bot => _bot;
    public string ChatId => _chatId;

    public async Task SendMessageAsync(string message, bool throttle = true)
    {
        try
        {
            if (throttle && _lastSent.TryGetValue(message, out var last))
            {
                if (DateTime.UtcNow - last < Throttle)
                {
                    _logger.Info("telegram_notifier", $"Пропуск (throttle): {message}");
                    return;
                }
            }

            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            await _bot.SendTextMessageAsync(_chatId, fullMessage);
            _lastSent[message] = DateTime.UtcNow;

            _logger.Info("telegram_notifier", $"Отправлено: {message}");
        }
        catch (Exception ex)
        {
            _logger.Error("telegram_notifier", $"Ошибка отправки: {ex.Message}");
        }
    }
}