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

namespace CryptoTrading.App.BrokerTesting
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            //first test is to go off to the server and get the current positions in the account.
            var api = new BinanceApi();
            var apiUser = new BinanceApiUser("C5ILSb0VCvBN3BgHMW4MlEqeNKtlif7w7Ib3Jgspl0tdefJZ3WnRn64YKaiPEkTE", "7guaW7iFbPwKqT3dgpL7Tht2L6xPNAxkIk41teMzjxD6G4qn5KaGCi4rCqLc8vW3");

            //bindings for the test broker (algo testing)
            IMarket  market = new TestMarket();
            ILogger logger = new FileLogger(@"C:\temp\BrokerTest.csv", LogLevel.Information);
            ITradeFactory factory = new TestTradeFactory();
            Dictionary<string, IPosition> dictionaryPositions = new Dictionary<string, IPosition>();
            IPositions positions = new Positions(factory, dictionaryPositions, null);
            //IMarketDataEvents marketDataEvents = new 

            var broker = new Broker.Broker(market, logger,positions,null);
            //Wire events.

            //set up market data feed -- needed for the stoploss monitor( but the broker may be the wrong place for it.)
            //how to do this???
            //seperate load for stoploss MD?? or hook into the live prices
            /*
            several options here
            1. seperate class for stop loss and seperate MD for live prices
                pros
                    can test without stoploss
                    during live trading the stop loss monitor will run on live prices without interupting the algo and due to segeration can keep running as a seperate service incase updates to the algo are required,

                cons
                    requires seperate csv or db loads for the live prices or minute candlesticks
                    2 classes that consumes/requet market data

            2.  
                


            */
            //set up message broker and submit trade request
            IMessageBroker messageBroker = MessageBroker.Instance;


        }

        private static async System.Threading.Tasks.Task LoadAccountData(BinanceApi api, IBinanceApiUser user)
        {
            var account = await api.GetAccountInfoAsync(user);

            Console.WriteLine();
        }
    }

    internal class TestTradeFactory : ITradeFactory
    {
        public ITrade CreateTrade(string requestBuySymbol, string requestSellSymbol)
        {
            throw new NotImplementedException();
        }
    }
}