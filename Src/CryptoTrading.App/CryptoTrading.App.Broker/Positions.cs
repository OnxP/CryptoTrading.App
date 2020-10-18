using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Broker
{
    public class Positions : IPositions
    {
        private ITradeFactory _factory;
        private ICalculator _calculator;

        private Dictionary<string, IPosition> _positions;

        public Positions(ITradeFactory factory, Dictionary<string, IPosition> positions, ICalculator calculator)
        {
            _factory = factory;
            _positions = positions;
            _calculator = calculator;
        }
        public bool CheckBalance(string sellSymbol, string sellAmount)
        {
            if (_positions.ContainsKey(sellSymbol))
            {
                return _positions[sellSymbol].CheckFunds(sellAmount);
            }

            return false;
        }

        public bool CheckOpenPosition(string requestBuySymbol)
        {
            if (_positions.ContainsKey(requestBuySymbol))
            {
                return _positions[requestBuySymbol].HasOpenPosition;
            }

            return false;
        }

        public ITrade CreateTrade(ITradeRequest request, StopLossMonitor stopLossMonitor)
        {
            ITrade trade = _factory.CreateTrade(request.BuySymbol, request.SellSymbol);
            

            return trade;
        }

        public void UpdatePosition(Order order)
        {
            if (_positions.ContainsKey(order.Symbol))
            {
                _positions[order.Symbol].UpdateOrder(order);
            }
        }
    }
}
