using Binance;
using System;
using System.Collections.Generic;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using System.Threading;
using Trade = CryptoTrading.App.Core.Trade.Trade;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Monitor;

namespace CryptoTrading.App.BrokerTesting
{
    class Program
    {
        public static void Main(string[] args)
        {
            //first test is to go off to the server and get the current positions in the account.
            var api = new BinanceApi();
            var apiUser = new BinanceApiUser("C5ILSb0VCvBN3BgHMW4MlEqeNKtlif7w7Ib3Jgspl0tdefJZ3WnRn64YKaiPEkTE", "7guaW7iFbPwKqT3dgpL7Tht2L6xPNAxkIk41teMzjxD6G4qn5KaGCi4rCqLc8vW3");

            //bindings for the test broker (algo testing)
            IMarket  market = new TestMarket();
            //ILogger<CryptoBroker> logger = new FileLogger(@"C:\temp\BrokerTest.csv", LogLevel.Information) as ILogger<CryptoBroker>;
            ITradeFactory factory = new TestTradeFactory();
            Dictionary<string, IPosition> dictionaryPositions = new Dictionary<string, IPosition>();
            dictionaryPositions.Add("XRP", new Position("XRP",0m));
            dictionaryPositions.Add("BTC", new Position("BTC",10)); 
            dictionaryPositions.Add("BNB", new Position("BNB",10));
            IPositions positions = new TestPositions(null,factory, dictionaryPositions);
            //IMarketDataEvents marketDataEvents = new 

            //var broker = new CryptoBroker(market, logger);
            //set up message broker and submit trade request
            double result = 0.5;
            var request = RequestBuilder.BuildTradeRequest(result, true,"XRPBTC", 0.5m, DateTime.Now, null,100m,1m);
            MessageBroker.Instance.Publish(new object(), request);

            Thread.Sleep(100000);
        }
    }

    internal class TestTradeFactory : ITradeFactory
    {
        public ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition, ITradeRequest request)
        {
            var trade = new Trade(buyPosition, sellPosition, feePosition, request);
            trade.Open = true;
            return trade;
            //should add a transaction for each ccy, seperate one for the fee.
            //since the same trade is used for the stop loss we can combine the transactions.
        }

        private decimal CalculateQuantity(decimal freeAmount, decimal sellPercentage, decimal price)
        {
            return (freeAmount * sellPercentage) / price;
        }
    }
}