using CryptoTrading.App.Persistence;
using CryptoTrading.App.Training;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();

var connString = Environment.GetEnvironmentVariable("CRYPTO_DB_CONN")
    ?? "Host=localhost;Port=5432;Database=cryptotrading;Username=postgres;Password=postgres";

services.AddCryptoDbPg(connString);
services.AddTransient<TrainingPipeline>();
services.AddLogging(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddConsole());

var sp = services.BuildServiceProvider();

var symbol = args.Length > 0 ? args[0] : "BTCUSDT";
var exchangeId = args.Length > 1 ? args[1] : "Binance";
var days = args.Length > 2 && int.TryParse(args[2], out var d) ? d : 90;

var pipeline = sp.GetRequiredService<TrainingPipeline>();
await pipeline.RunAsync(symbol, exchangeId, days);
