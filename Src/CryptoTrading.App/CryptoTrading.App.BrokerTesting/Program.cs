using Binance;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Logging;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
using CryptoTrading.App.Core.TradeRequest;
using Binance.Client;
using System.Threading;
using Trade = CryptoTrading.App.Core.Trade.Trade;

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
            ILogger logger = new FileLogger(@"C:\temp\BrokerTest.csv", LogLevel.Information);
            ITradeFactory factory = new TestTradeFactory();
            Dictionary<string, IPosition> dictionaryPositions = new Dictionary<string, IPosition>();
            dictionaryPositions.Add("XRP", new Position("XRP",0m));
            dictionaryPositions.Add("BTC", new Position("BTC",10)); 
            dictionaryPositions.Add("BNB", new Position("BNB",10));
            IPositions positions = new TestPositions(factory, dictionaryPositions, null);
            //IMarketDataEvents marketDataEvents = new 

            var broker = new CryptoBroker(market, logger,positions);
            //set up message broker and submit trade request
            double result = 0.5;
            var request = RequestBuilder.BuildTradeRequest(result, "XRPBTC", 0.5m, DateTime.Now);
            MessageBroker.Instance.Publish(new object(), request);

            Thread.Sleep(100000);
        }
    }

    internal class TestTradeFactory : ITradeFactory
    {
        public ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition, ITradeRequest request)
        {
            var trade = new Trade();
            trade.Open = true;
            var quoteQuantity = sellPosition.FreeAmount * (decimal)request.SellPercentage;
            var quantity = quoteQuantity / request.Price;
            trade.CreateTransaction(buyPosition.CreateTransaction(quantity), sellPosition.CreateTransaction(-quoteQuantity), feePosition.CreateTransaction(quoteQuantity / 0.002m), request.Price, request.RequestDateTime);
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