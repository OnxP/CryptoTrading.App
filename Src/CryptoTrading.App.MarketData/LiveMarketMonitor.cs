using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CryptoTrading.App.Core;
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
    public class LiveMarketMonitor : AbstractMarketData, IMarketMonitor
    {
        protected IExchangeProvider _provider;
        public LiveMarketMonitor(ILogger<LiveMarketMonitor> logger, IExchangeProvider provider)
        {
            _provider = provider;
        }

        private List<string> symbols = new List<string>();

        protected LiveMarketMonitor()
        {
        }

        public async virtual Task<bool> CheckOrder(ITransaction transaction)
        {
            var newOrder = await _provider.GetOrderAsync(transaction.Pair, transaction.Order.ClientOrderId).ConfigureAwait(false);
            transaction.UpdateOrder(newOrder);
            return newOrder.Status == ExchangeOrderStatus.Filled;
        }

        public async Task Subscribe(string symbol, string keyValue, Action<ExchangeCandlestickEvent> processCandleStick)
        {
            symbols.Add(symbol);
            await _provider.SubscribeCandlestickAsync(symbol, CandleInterval.Minute_1, candle =>
            {
                var evt = new ExchangeCandlestickEvent
                {
                    EventTime = DateTime.UtcNow,
                    Candlestick = candle,
                    FirstTradeId = 0,
                    LastTradeId = 0,
                    IsFinal = true
                };
                processCandleStick(evt);
            });
        }

        public bool IsSubscribed(string symbol, string keyValue)
        {
            return symbols.Contains(symbol);
        }

        public void UnSubscribe(string symbol, string keyValue)
        {
            symbols.Remove(symbol);
            // Unsubscription is handled by the provider
        }

        public override void Configure(IConfig request)
        {
            throw new NotImplementedException();
        }

        public async Task<List<ExchangeCandlestick>> GetHistoricCandleSticks(string symbol)
        {
            var calculatedFrom = CandleStickIntervalHelper.CalculateCandleStickTimeFrom(DateTime.Now, CandleInterval.Minute_1, 200).
                ToUniversalTime();
            var candleSticks = await _provider.GetCandlesticksAsync(symbol, CandleInterval.Minute_1, calculatedFrom, DateTime.Now.ToUniversalTime());

            return candleSticks.Reverse().ToList();
        }
    }
}
