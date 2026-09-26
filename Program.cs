using w2.services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("watchtower-configuration.json", optional: false, reloadOnChange: true);

builder.Services.AddSingleton<LogingService>();
builder.Services.AddSingleton<TelegramNotifier>();
builder.Services.AddSingleton<ServiceProbe>();
builder.Services.AddSingleton<ServiceRestarter>();
builder.Services.AddSingleton<HealthCheckService>();
builder.Services.AddHostedService<HealthCheckService>(provider =>
    provider.GetRequiredService<HealthCheckService>());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();