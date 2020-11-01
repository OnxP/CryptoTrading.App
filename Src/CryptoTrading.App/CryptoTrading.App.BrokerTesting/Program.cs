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
            double result = 10;
            var request = RequestBuilder.BuildTradeRequest(result, "XRPBTC", 0.5m);
            MessageBroker.Instance.Publish(new object(), request);

            Thread.Sleep(100000);
        }
    }

    internal class TestTradeFactory : ITradeFactory
    {
        public ITrade CreateTrade(string requestBuySymbol, string requestSellSymbol)
        {
            var trade = new Trade();
            trade.OrderType = OrderSide.Buy;
            trade.Symbol = requestBuySymbol + requestSellSymbol;
            return trade;
        }

        public ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, ITradeRequest request)
        {
            var trade = new Trade();
            trade.OrderType = OrderSide.Buy;
            trade.Symbol = buyPosition.Symbol + sellPosition.Symbol;
            trade.Price = request.Price;
            return trade;
        }

        public ITrade CreateTrade(IPosition buyPosition, IPosition sellPosition, IPosition feePosition, ITradeRequest request)
        {
            var trade = new Trade();
            trade.OrderType = OrderSide.Buy;
            trade.Symbol = buyPosition.Symbol + sellPosition.Symbol;
            trade.Quantity = CalculateQuantity(sellPosition.FreeAmount, (decimal)request.SellPercentage, request.Price);
            trade.Price = request.Price;

            trade.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       


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