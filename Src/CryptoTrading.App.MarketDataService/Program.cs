using System.Linq;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.MarketDataService;
using CryptoTrading.App.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var connectionString = builder.Configuration.GetConnectionString("CryptoDb")
    ?? "Host=localhost;Database=CryptoDb;Username=crypto;Password=crypto";

builder.Services.AddCryptoDbPg(connectionString);
builder.Services.AddSingleton<ExchangeProviderRegistry>();
builder.Services.AddSingleton<CandleGapFiller>();
builder.Services.AddSingleton<CandleCacheWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CandleCacheWorker>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CryptoDbContextPg>();
    await db.Database.MigrateAsync();

    var registry = scope.ServiceProvider.GetRequiredService<ExchangeProviderRegistry>();
    var exchangeConfigs = await db.ExchangeConfigs.Where(c => c.IsActive).ToListAsync();

    foreach (var config in exchangeConfigs)
    {
        var exchangeConfig = new ExchangeConfig
        {
            Id = config.Id,
            ExchangeId = config.ExchangeId,
            ApiKey = config.ApiKey,
            ApiSecret = config.ApiSecret,
            IsActive = config.IsActive,
            RunType = config.RunType
        };

        var apiKeyOverride = builder.Configuration[$"Exchange:{config.ExchangeId}:ApiKey"];
        var apiSecretOverride = builder.Configuration[$"Exchange:{config.ExchangeId}:ApiSecret"];
        if (!string.IsNullOrEmpty(apiKeyOverride)) exchangeConfig.ApiKey = apiKeyOverride;
        if (!string.IsNullOrEmpty(apiSecretOverride)) exchangeConfig.ApiSecret = apiSecretOverride;

        registry.Register(exchangeConfig);
    }
}

app.MapGrpcService<MarketDataGrpcService>();
app.MapGet("/", () => "MarketDataService gRPC is running. Use a gRPC client to connect.");

app.Run();
