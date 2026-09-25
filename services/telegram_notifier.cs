using Telegram.Bot;

namespace w2.services;

public class TelegramNotifier
{
    private readonly TelegramBotClient _bot;
    private readonly string _chatId;
    private readonly LogingService _logger;

    public TelegramNotifier(IConfiguration config, LogingService logger)
    {
        var token = config["Telegram:BotToken"];
        _chatId = config["Telegram:ChatId"] ?? string.Empty;
        _bot = new TelegramBotClient(token ?? string.Empty);
        _logger = logger;
    }

    public async Task SendMessageAsync(string message)
    {
        try
        {
            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            await _bot.SendTextMessageAsync(_chatId, fullMessage);
            _logger.Info("telegram_notifier", $"Telegram sent: {message}");
        }
        catch (Exception ex)
        {
            _logger.Error("telegram_notifier", $"Telegram send failed: {ex.Message}");
        }
    }
}