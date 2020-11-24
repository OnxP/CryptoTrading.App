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

    public class StopLossMonitor
    {
        private IBroker _broker;
        private decimal _currentStopLoss;
        private decimal _targetStopLoss;
        private ITrade _trade;
        private int _trailingPercentIncrement = 2;
        private int _risk = 10;
        private int _target = 15;
        private Order _order;
        public StopLossMonitor(IBroker broker)
        {
            _broker = broker;
        }

        //Process a candlestick
        public async void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs)
        {
            var closePrice = candlestickEventArgs.Candlestick.Close;
            if (closePrice >= _targetStopLoss)
            {
                _currentStopLoss = CalculateNewPrice(closePrice, -1 * _trailingPercentIncrement);
                _targetStopLoss = CalculateNewPrice(closePrice, _trailingPercentIncrement);
                //_order = await _broker.SetNewLimitOrder(_trade, _order, _currentStopLoss);

            }

            if (closePrice < _currentStopLoss)
            {
                //limit order has been meet
                //tell the broker to check the order.
                _broker.ClosePosition(_trade);
            }
        }

        public async void ConfigureStopLossMonitor(ITrade trade)
        {
            _trade = trade;
            _currentStopLoss = CalculateNewPrice(_trade.Price, -1 * _risk);
            _targetStopLoss = CalculateNewPrice(_trade.Price, _target);
            //_order = await _broker.SetLimitOrder(trade, _currentStopLoss);
        }

        private decimal CalculateNewPrice(decimal _tradedPrice, int percentOfValue)
        {
            return _tradedPrice * (1 + percentOfValue / 100);
        }
    }


}
