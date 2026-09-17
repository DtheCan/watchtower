using watchtower.services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("watchtower-configuration.json", optional: false, reloadOnChange: true);

// Логи и инфраструктура
builder.Services.AddSingleton<LogingService>();
builder.Services.AddSingleton<TelegramNotifier>();
builder.Services.AddSingleton<MaintenanceService>();
builder.Services.AddSingleton<ServiceStateStore>();

// Пробники
builder.Services.AddHttpClient("probe")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    });
builder.Services.AddSingleton<HttpProbe>();
builder.Services.AddSingleton<ServiceProbe>();

// Рестартер и health-check
builder.Services.AddSingleton<ServiceRestarter>();
builder.Services.AddSingleton<HealthCheckService>();
builder.Services.AddHostedService(p => p.GetRequiredService<HealthCheckService>());

// Бот (фоновый сервис с кнопками)
builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddHostedService(p => p.GetRequiredService<TelegramBotService>());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();