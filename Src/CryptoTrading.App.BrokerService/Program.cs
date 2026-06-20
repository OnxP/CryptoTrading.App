using CryptoTrading.App.BrokerService;
using CryptoTrading.App.Core.Exchange;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<BrokerExchangeRegistry>();

var app = builder.Build();

var registry = app.Services.GetRequiredService<BrokerExchangeRegistry>();

var exchanges = builder.Configuration.GetSection("Exchange").GetChildren();
foreach (var exchangeSection in exchanges)
{
    var exchangeId = exchangeSection.Key;
    var apiKey = exchangeSection["ApiKey"];
    var apiSecret = exchangeSection["ApiSecret"];
    var venuesStr = exchangeSection["Venues"] ?? "Spot";

    var config = new ExchangeConfig
    {
        ExchangeId = exchangeId,
        ApiKey = apiKey,
        ApiSecret = apiSecret,
        IsActive = true,
        RunType = "Live"
    };

    foreach (var venueStr in venuesStr.Split(','))
    {
        var venue = Enum.Parse<TradingVenue>(venueStr.Trim(), ignoreCase: true);
        registry.Register(config, venue);
    }
}

app.MapGrpcService<BrokerGrpcService>();
app.MapGet("/", () => "BrokerService gRPC is running. Use a gRPC client to connect.");

app.Run();
