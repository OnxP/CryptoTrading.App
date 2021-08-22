using Binance;
using CryptoTrading.App.Core;
using System.Collections.Generic;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.Position;
using System;

namespace CryptoTrading.App.Monitor
{
    public class TestPositions : IPositions
    {
        private readonly ITradeFactory _factory;

        private readonly Dictionary<string, IPosition> _positions;

        public TestPositions(ITradeFactory factory, Dictionary<string, IPosition> positionsProvider)//, ICalculator calculator)
        {
            _factory = factory;
            _positions = positionsProvider;
            //_calculator = calculator;
        }
        public IPosition GetPosition(string asset)
        {
            return _positions[asset];
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
            var buyPosition = _positions[request.BaseSymbol];
            buyPosition.IsLocked = true;
            var sellPosition = _positions[request.QuoteSymbol];
            var feePosition = _positions["BNB"];
            ITrade trade = _factory.CreateTrade(buyPosition, sellPosition, feePosition, request);
            buyPosition.IsLocked = false;
            return trade;
        }

        public bool CheckRequest(ITradeRequest what)
        {
            return !CheckOpenPosition(what.BaseSymbol) && CheckBalance(what.QuoteSymbol, what.SellPercentage);
        }
    }
}
