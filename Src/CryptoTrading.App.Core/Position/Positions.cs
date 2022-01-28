using System.Collections.Generic;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Position
{
    public class Positions : IPositions
    {
        private readonly ITradeFactory _factory;
        private ICalculator _calculator;

        private readonly Dictionary<string, IPosition> _positions;

        public IPosition GetPosition(string asset)
        {
            if(!_positions.ContainsKey(asset))
                _positions.Add(asset, new Position(asset, 0.0m));
            return _positions[asset];
        }

        public Positions(ITradeFactory factory, Dictionary<string, IPosition> positions, ICalculator calculator)
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
            var buyPosition = _positions[request.BaseSymbol];
            var sellPosition = _positions[request.QuoteSymbol];
            var feePosition = _positions["BNBBTC"];
            ITrade trade = _factory.CreateTrade(buyPosition, sellPosition, feePosition, request);
            return trade;
        }


        public bool CheckRequest(ITradeRequest what)
        {
            return CheckOpenPosition(what.BaseSymbol) && CheckBalance(what.QuoteSymbol, what.SellPercentage);
        }
    }
}
