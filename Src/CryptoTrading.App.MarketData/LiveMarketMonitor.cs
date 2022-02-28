using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;
using System;
using CryptoTrading.App.Core.MarketMonitorFactory;

namespace CryptoTrading.App.Monitor
{
    //Class monitors the position in the open trade and adjusts the stop loss, this could work on live streaming data 
    //Input(Initial) - Trade details.
    //Input(continuous) - CandleStick Processing.
    //StaticInput - Stop loss type and limit.
    //Output - Change Stop Limit Order

    //Processing logic
    //Initial - Configure Stoploss Monitor from Open trade. and set a stop limit order.
    //Continuous - Monitor price and once it hits a threshold reset stoploss to limit order X% below threshold then adjust threshold

    public class LiveMarketMonitor : IMarketMonitor
    {
        private System.Action<CandlestickEventArgs> action;

        public string Symbol { get; set; }

        public bool Started => true;

        private IBinanceApi _api;
        private IBinanceApiUser _user;
        public LiveMarketMonitor(string symbol, IBinanceApi api, IBinanceApiUser user)
        {
            _api = api;
            _user = user;
            Symbol = symbol;
        }

        public virtual bool CheckOrder(ITransaction transaction)
        {
            var newOrder=_api.GetOrderAsync(_user,Symbol,transaction.Order.ClientOrderId).Result;
            transaction.UpdateOrder(newOrder);
            return newOrder.Status == OrderStatus.Filled;
        }

        public void Dispose()
        {
            _api = null;
            _user = null;
        }

        public void StopStream()
        {
            throw new System.NotImplementedException();
        }

        public void StartStream()
        {
            throw new System.NotImplementedException();
        }

        public void Subscribe(System.Action<CandlestickEventArgs> processCandleStick)
        {
            action = processCandleStick;
        }

        public void Subscribe(string symbol, Action<CandlestickEventArgs> processCandleStick)
        {
            throw new NotImplementedException();
        }

        public bool IsSubscribed(string symbol, Action<CandlestickEventArgs> processCandleStick)
        {
            throw new NotImplementedException();
        }

        public void UnSubscribe(string symbol, Action<CandlestickEventArgs> processCandleStick)
        {
            throw new NotImplementedException();
        }

        public void Subscribe(string symbol, string keyValue, Action<CandlestickEventArgs> processCandleStick)
        {
            throw new NotImplementedException();
        }

        public bool IsSubscribed(string symbol, string keyValue)
        {
            throw new NotImplementedException();
        }

        public void UnSubscribe(string symbol, string keyValue)
        {
            throw new NotImplementedException();
        }
    }


}
