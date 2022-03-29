using System.Collections.Generic;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Core.Position;
using System;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Monitor
{
    public class TestPositions : IPositions
    {
        private readonly ITradeFactory _factory;

        private readonly Dictionary<string, IPosition> _positions;

        private ILogger<TestPositions> Logger { get; set; }
        public TestPositions(ILogger<TestPositions> logger, ITradeFactory factory, Dictionary<string, IPosition> positionsProvider) : this(factory)
        {
            Logger = logger;
            _positions = positionsProvider;
            //_calculator = calculator;
        }

        public TestPositions(ITradeFactory factory)
        {
            _factory = factory;
            _positions = new Dictionary<string, IPosition>();
        }

        public IPosition GetPosition(string asset)
        {
            if (!_positions.ContainsKey(asset))
                _positions.Add(asset, new Position(asset, 0.0m));
            return _positions[asset];
        }

        public bool CheckBalance(ITradeRequest what)
        {
            return _positions.ContainsKey(what.QuoteSymbol) && _positions[what.QuoteSymbol].CheckFunds(what.Amount,what.FixedAmount);
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
            return !CheckOpenPosition(what.BaseSymbol) && CheckBalance(what);
        }

        public void AjdustPosition(string accountPositionAsset, decimal accountPositionFree)
        {
            Logger.LogInformation($"Ajusting Position from Binance {accountPositionAsset} - {accountPositionFree}");

            var pos = GetPosition(accountPositionAsset);
            pos.CreateTransaction(accountPositionFree);
        }
    }
}
