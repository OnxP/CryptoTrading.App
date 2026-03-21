using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;
using CryptoTrading.App.Broker;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.TradeRequest;
using System.Threading;
using Trade = CryptoTrading.App.Core.Trade.Trade;
using CryptoTrading.App.Core.Position;

namespace CryptoTrading.App.BrokerTesting
{
    class Program
    {
        public static void Main(string[] args)
        {
            //first test is to go off to the server and get the current positions in the account.
            // TODO: Replace with IExchangeProvider from DI
            // var provider = new BinanceExchangeProvider(apiKey, apiSecret);

            //bindings for the test broker (algo testing)
            IMarket  market = new TestMarket();
            //ILogger<CryptoBroker> logger = new FileLogger(@"C:\temp\BrokerTest.csv", LogLevel.Information) as ILogger<CryptoBroker>;
            ITradeFactory factory = new TestTradeFactory();
            Dictionary<string, IPosition> dictionaryPositions = new Dictionary<string, IPosition>();
            dictionaryPositions.Add("XRP", new Position("XRP",0m));
            dictionaryPositions.Add("BTC", new Position("BTC",10)); 
            dictionaryPositions.Add("BNB", new Position("BNB",10));
            IPositions positions = new Positions(null,factory, dictionaryPositions);
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
        public ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition)
        {
            var trade = new Trade(buyPosition, sellPosition, feePosition);
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