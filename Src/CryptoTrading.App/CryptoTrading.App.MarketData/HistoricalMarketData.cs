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
using Binance.Utility;

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
            _client.Subscribe()
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
            _webSocket.Message += (s, e) => _client.HandleMessage(e.Subject, e.Json);

        }

        public void StartStream()
        {
            try
            {
                _webSocket.Uri = BinanceWebSocketStream.CreateUri(_client);

                using var controller = new RetryTaskController(_webSocket.StreamAsync);
                controller.Error += (s, e) => HandleError(e.Exception);
                controller.Begin();

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine();
                Console.WriteLine("  ...press any key to close window.");
                Console.ReadKey(true);
            }
        }
        private static void HandleError(Exception e)
        {
            //lock (_sync)
            //{
                Console.WriteLine(e.Message);
            //}
        }
    } 
}

    

