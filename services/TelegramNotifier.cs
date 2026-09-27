using Telegram.Bot;

namespace w2.services;

public class TelegramNotifier(IConfiguration config, LogingService logger)
{
    private readonly TelegramBotClient _bot = new(config["Telegram:BotToken"] ?? string.Empty);
    private readonly string _chatId = config["Telegram:ChatId"] ?? string.Empty;
    private readonly LogingService _logger = logger;

    public async Task SendMessageAsync(string message)
    {
        try
        {
            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            await _bot.SendMessage(_chatId, fullMessage);
            _logger.Info("telegram_notifier", $"Telegram sent: {message}");
        }
        catch (Exception ex)
        {
            _logger.Error("telegram_notifier", $"Telegram send failed: {ex.Message}");
        }
    }
}