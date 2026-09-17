using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace watchtower.services;

public class TelegramBotService : BackgroundService
{
    private readonly TelegramNotifier _notifier;
    private readonly LogingService _logger;
    private readonly MaintenanceService _maintenance;
    private readonly IConfiguration _config;
    private readonly TelegramBotClient _bot;
    private readonly List<ServiceConfig> _services;

    public TelegramBotService(
        TelegramNotifier notifier,
        LogingService logger,
        MaintenanceService maintenance,
        IConfiguration config)
    {
        _notifier = notifier;
        _logger = logger;
        _maintenance = maintenance;
        _config = config;
        _bot = notifier.Bot;
        _services = config.GetSection("Services").Get<List<ServiceConfig>>() ?? new();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("telegram_bot", "TelegramBotService started.");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        _bot.StartReceiving(
        updateHandler: HandleUpdateAsync,
        pollingErrorHandler: (botClient, exception, cancellationToken) =>
        {
            _logger.Error("telegram_bot", $"Ошибка polling: {exception.Message}");
            return Task.CompletedTask;
        },
        receiverOptions: receiverOptions,
        cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message?.Text != null)
            {
                await HandleMessageAsync(update.Message, ct);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                await HandleCallbackAsync(update.CallbackQuery, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("telegram_bot", $"Ошибка обработки: {ex.Message}");
        }
    }

    // ------- Команды -------

    private async Task HandleMessageAsync(Message msg, CancellationToken ct)
    {
        var text = msg.Text!.Trim();

        if (text == "/start" || text == "/menu")
        {
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🛠 Техработы", "maint:menu") },
                new[] { InlineKeyboardButton.WithCallbackData("📊 Статус сервисов", "status:all") }
            });

            await _bot.SendTextMessageAsync(msg.Chat.Id, "Меню Watchtower:", replyMarkup: kb, cancellationToken: ct);
        }
    }

    // ------- Inline callbacks -------

    private async Task HandleCallbackAsync(CallbackQuery cb, CancellationToken ct)
    {
        var data = cb.Data ?? "";
        var chatId = cb.Message!.Chat.Id;

        if (data == "maint:menu")
        {
            await ShowMaintenanceMenu(chatId, ct);
        }
        else if (data.StartsWith("maint:svc:"))
        {
            var svc = data.Substring("maint:svc:".Length);
            await ShowDurationMenu(chatId, svc, ct);
        }
        else if (data.StartsWith("maint:dur:"))
        {
            // формат: maint:dur:<svc>:<minutes>
            var parts = data.Split(':');
            var svc = parts[2];
            var minutes = int.Parse(parts[3]);
            _maintenance.Start(svc, TimeSpan.FromMinutes(minutes));
            await _bot.SendTextMessageAsync(chatId,
                $"🛠 Техработы для «{svc}» на {minutes} мин. Проверки приостановлены.",
                cancellationToken: ct);
        }
        else if (data == "status:all")
        {
            await SendStatusAsync(chatId, ct);
        }

        await _bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    private async Task ShowMaintenanceMenu(long chatId, CancellationToken ct)
    {
        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("🌐 ВСЕ сервисы", "maint:svc:*") }
        };

        foreach (var s in _services)
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData(s.Name, $"maint:svc:{s.Name}") });

        var kb = new InlineKeyboardMarkup(rows);
        await _bot.SendTextMessageAsync(chatId, "Выберите сервис для техработ:", replyMarkup: kb, cancellationToken: ct);
    }

    private async Task ShowDurationMenu(long chatId, string svc, CancellationToken ct)
    {
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("15 мин", $"maint:dur:{svc}:15"),
                InlineKeyboardButton.WithCallbackData("30 мин", $"maint:dur:{svc}:30")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("1 час", $"maint:dur:{svc}:60"),
                InlineKeyboardButton.WithCallbackData("2 часа", $"maint:dur:{svc}:120")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Отмена", "maint:cancel")
            }
        });

        await _bot.SendTextMessageAsync(chatId,
            $"На сколько приостановить проверки «{svc}»?",
            replyMarkup: kb, cancellationToken: ct);
    }

    private async Task SendStatusAsync(long chatId, CancellationToken ct)
    {
        var lines = new List<string> { "📊 Статус сервисов:" };
        foreach (var s in _services)
        {
            var maint = _maintenance.IsUnderMaintenance(s.Name) ? " 🛠 техработы" : "";
            lines.Add($"• {s.Name}{maint}");
        }
        await _bot.SendTextMessageAsync(chatId, string.Join("\n", lines), cancellationToken: ct);
    }
}