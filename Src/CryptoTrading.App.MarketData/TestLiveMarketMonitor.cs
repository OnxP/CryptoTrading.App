using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.MarketData
{
    //Class monitors the position in the open trade and adjusts the stop loss, this could work on live streaming data
    //Input(Initial) - Trade details.
    //Input(continuous) - CandleStick Processing.
    //StaticInput - Stop loss type and limit.
    //Output - Change Stop Limit Order

    //Processing logic
    //Initial - Configure Stoploss Monitor from Open trade. and set a stop limit order.
    //Continuous - Monitor price and once it hits a threshold reset stoploss to limit order X% below threshold then adjust threshold


    //this needs to send a signal back to Trade Processor with a ready. then the trade processor can decide on which order to execute the trades.
    public class TestLiveMarketMonitor : LiveMarketMonitor
    {
        public TestLiveMarketMonitor(ILogger<TestLiveMarketMonitor> logger, IExchangeProvider provider)
        {
            _provider = provider;
        }

        public async override Task<bool> CheckOrder(ITransaction transaction)
        {
            transaction.Complete();
            return true;
        }

    }
}
