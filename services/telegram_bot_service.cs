using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Exceptions;

namespace watchtower.services;

public class TelegramBotService : BackgroundService
{
    private readonly TelegramNotifier _notifier;
    private readonly LogingService _logger;
    private readonly MaintenanceService _maintenance;
    private readonly ServiceStateStore _stateStore;
    private readonly IConfiguration _config;
    private readonly TelegramBotClient _bot;
    private readonly List<ServiceConfig> _services;

    public TelegramBotService(
        TelegramNotifier notifier,
        LogingService logger,
        MaintenanceService maintenance,
        ServiceStateStore stateStore,
        IConfiguration config)
    {
        _notifier = notifier;
        _logger = logger;
        _maintenance = maintenance;
        _stateStore = stateStore;
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
    pollingErrorHandler: (botClient, exception, cancellationToken) => // Убрали параметр 'source'
    {
        // Логика обработки ошибок
        if (exception is RequestException re && re.Message.Contains("timed out"))
            return Task.CompletedTask;

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
                await HandleMessageAsync(update.Message, ct);
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
                await HandleCallbackAsync(update.CallbackQuery, ct);
        }
        catch (Exception ex)
        {
            _logger.Error("telegram_bot", $"Ошибка обработки: {ex.Message}");
        }
    }

    // ------- Текстовые команды -------

    private async Task HandleMessageAsync(Message msg, CancellationToken ct)
    {
        var text = msg.Text!.Trim();

        switch (text)
        {
            case "/start":
            case "/menu":
                await ShowMainMenu(msg.Chat.Id, ct);
                break;

            case "/status":
                await SendStatusAsync(msg.Chat.Id, ct);
                break;

            case "/help":
                await _bot.SendTextMessageAsync(msg.Chat.Id,
                    "Команды:\n" +
                    "/menu — меню\n" +
                    "/status — статус всех сервисов\n" +
                    "/help — эта справка",
                    cancellationToken: ct);
                break;
        }
    }

    private async Task ShowMainMenu(long chatId, CancellationToken ct)
    {
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🛠 Техработы", "maint:menu") },
            new[] { InlineKeyboardButton.WithCallbackData("📊 Статус сервисов", "status:all") }
        });

        await _bot.SendTextMessageAsync(chatId, "Меню Watchtower:", replyMarkup: kb, cancellationToken: ct);
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
            var key = data.Substring("maint:svc:".Length);
            await ShowDurationMenu(chatId, key, ct);
        }
        else if (data.StartsWith("maint:dur:"))
        {
            // maint:dur:<key>:<minutes>
            var parts = data.Split(':');
            var key = parts[2];
            var minutes = int.Parse(parts[3]);
            _maintenance.Start(key, TimeSpan.FromMinutes(minutes));

            var display = FindDisplayName(key);
            await _bot.SendTextMessageAsync(chatId,
                $"🛠 Техработы для «{display}» на {minutes} мин. Проверки приостановлены.",
                cancellationToken: ct);
        }
        else if (data.StartsWith("maint:stop:"))
        {
            var key = data.Substring("maint:stop:".Length);
            _maintenance.Stop(key);
            await _bot.SendTextMessageAsync(chatId,
                $"✅ Техработы для «{FindDisplayName(key)}» сняты.",
                cancellationToken: ct);
        }
        else if (data == "status:all")
        {
            await SendStatusAsync(chatId, ct);
        }

        await _bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
    }

    private string FindDisplayName(string key)
    {
        if (key == "*") return "ВСЕ сервисы";
        var svc = _services.FirstOrDefault(s => s.Key == key);
        return svc?.DisplayName ?? key;
    }

    private async Task ShowMaintenanceMenu(long chatId, CancellationToken ct)
    {
        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("🌐 ВСЕ сервисы", "maint:svc:*") }
        };

        foreach (var s in _services)
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData(s.DisplayName, $"maint:svc:{s.Key}") });

        var kb = new InlineKeyboardMarkup(rows);
        await _bot.SendTextMessageAsync(chatId, "Выберите сервис для техработ:", replyMarkup: kb, cancellationToken: ct);
    }

    private async Task ShowDurationMenu(long chatId, string key, CancellationToken ct)
    {
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("15 мин", $"maint:dur:{key}:15"),
                InlineKeyboardButton.WithCallbackData("30 мин", $"maint:dur:{key}:30")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("1 час", $"maint:dur:{key}:60"),
                InlineKeyboardButton.WithCallbackData("2 часа", $"maint:dur:{key}:120")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Отмена", "maint:cancel")
            }
        });

        await _bot.SendTextMessageAsync(chatId,
            $"На сколько приостановить проверки «{FindDisplayName(key)}»?",
            replyMarkup: kb, cancellationToken: ct);
    }

    private async Task SendStatusAsync(long chatId, CancellationToken ct)
    {
        var all = _stateStore.All();
        if (all.Count == 0)
        {
            await _bot.SendTextMessageAsync(chatId, "Нет данных о сервисах.", cancellationToken: ct);
            return;
        }

        var lines = new List<string> { "📊 *Статус сервисов:*", "" };
        foreach (var s in all.OrderBy(x => x.NodeName).ThenBy(x => x.Name))
        {
            var icon = s.InMaintenance ? "🛠"
                     : s.LastResultHealthy ? "🟢"
                     : "🔴";

            var http = s.HttpCode.HasValue ? $"HTTP {s.HttpCode}" : "HTTP —";
            var ssh = s.SshOk ? (s.SshRunning ? "SSH RUNNING" : "SSH STOPPED") : "SSH —";

            lines.Add($"{icon} *{s.DisplayName}*");
            lines.Add($"   {http} | {ssh}");
            if (s.InMaintenance) lines.Add("   🛠 техработы");
            lines.Add("");
        }

        await _bot.SendTextMessageAsync(chatId, string.Join("\n", lines),
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }
}