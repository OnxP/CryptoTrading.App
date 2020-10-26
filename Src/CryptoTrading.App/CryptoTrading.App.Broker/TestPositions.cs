using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Broker
{
    public class TestPositions : IPositions
    {
        private readonly ITradeFactory _factory;
        private ICalculator _calculator;

        private readonly Dictionary<string, IPosition> _positions;

        public TestPositions(ITradeFactory factory, Dictionary<string, IPosition> positions, ICalculator calculator)
        {
            _factory = factory;
            _positions = positions;
            _calculator = calculator;
        }

        public bool CheckBalance(string sellSymbol, double sellPercentage)
        {
            if (_positions.ContainsKey(sellSymbol))
            {
                return _positions[sellSymbol].CheckFunds(sellPercentage);
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

        public ITrade CreateTrade(ITradeRequest request)
        {
            var buyPosition = _positions[request.BuySymbol];
            var sellPosition = _positions[request.SellSymbol];
            var feePosition = _positions["BNB"];
            ITrade trade = _factory.CreateTrade(buyPosition, sellPosition, feePosition, request);
            return trade;
        }

        public void AddOrder(Order order)
        {
            if (_positions.ContainsKey(order.Symbol))
            {
                _positions[order.Symbol].UpdateOrder(order);
            }
        }

        public decimal CalculateStoploss(Order order)
        {
            if (_positions.ContainsKey(order.Symbol))
            {
                return _positions[order.Symbol].CalculateStopLoss(order);
            }

            return 0;
        }
    }
}
