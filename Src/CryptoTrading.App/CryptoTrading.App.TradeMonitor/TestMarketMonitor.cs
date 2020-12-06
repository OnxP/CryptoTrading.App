using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;

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

    public class TestMarketMonitor : IMarketMonitor
    {
        private System.Action<CandlestickEventArgs> action;

        public string Symbol { get; }

        public TestMarketMonitor(string symbol)
        {
            Symbol = symbol;
        }

        public bool CheckOrder(string clientOrderId)
        {
            return true;
        }

        public void Dispose()
        {
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
    }


}
