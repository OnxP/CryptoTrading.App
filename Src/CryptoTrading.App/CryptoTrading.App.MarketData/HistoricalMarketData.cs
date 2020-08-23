using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using Binance.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using Binance;
using Binance.Client;
using Binance.Application;

namespace CryptoTrading.App.MarketData
{
    public class HistoricalMarketData : IMarketData
    {
        DateTime From { get; set; }
        DateTime To { get; set; }

        ICandlestickClient _client;
        IBinanceWebSocketStream _webSocket;
        //public events 

        public void InitialDataLoadSubscribe(Action<IList<CandlestickEventArgs>> callback)
        {

        }
        public void InitialDataLoadUnSubscribe(Action<IList<CandlestickEventArgs>> callback)
        {

        }

        public void InitialDataStreamSubscribe(Action<CandlestickEventArgs> callback)
        {

        }

        public void InitialDataStreamUnSubscribe(Action<CandlestickEventArgs> callback)
        {

        }

        public void Configure(IRequest request)
        {
            var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", true, false)
                    .Build();

            // Configure services.
            var services = new ServiceCollection()
                .AddBinance() // add default Binance services.
                .AddLogging(builder => builder // configure logging.
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddFile(configuration.GetSection("Logging:File")))
                .BuildServiceProvider();

            // Initialize client.
            var client = services.GetService<ICandlestickClient>();

            // Initialize the stream.
            var webSocket = services.GetService<IBinanceWebSocketStream>();

        }

        public void StartStream()
        {
            _webSocket.Uri = BinanceWebSocketStream.CreateUri(_client);
        }

    }
}
