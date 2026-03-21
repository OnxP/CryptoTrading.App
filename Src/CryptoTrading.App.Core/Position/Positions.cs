using System.Collections.Generic;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Core.Position
{
    public class Positions : IPositions
    {
        private readonly ITradeFactory _factory;

        private readonly Dictionary<string, IPosition> _positions;

        private string FeeAsset => "USDT";

        public IPosition GetPosition(string asset)
        {
            if(!_positions.ContainsKey(asset))
                _positions.Add(asset, new Position(asset, 0.0m));
            return _positions[asset];
        }
        private ILogger<Positions> Logger { get; set; }
        public Positions(ILogger<Positions> logger, ITradeFactory factory, Dictionary<string, IPosition> positionsProvider) : this(logger,factory)
        {
            _factory = factory;
            _positions = positionsProvider;
            //_calculator = calculator;
        }

        public Positions(ILogger<Positions> logger, ITradeFactory factory)
        {
            Logger = logger;
            _factory = factory;
            _positions = new Dictionary<string, IPosition>();
        }

        public bool CheckHasEnoughBalance(ITradeRequest what)
        {
            if (_positions.ContainsKey(what.QuoteSymbol))
            {
                return _positions[what.QuoteSymbol].CheckHasEnoughBalance(what);
            }

            return false;
        }

        public bool CheckHasOpenPositionAndVolume(string requestBuySymbol, ITradeRequest request)
        {
            if (_positions.ContainsKey(requestBuySymbol))
            {
                return _positions[requestBuySymbol].HasOpenPosition && request.BaseQuantity < request.Volume/2;
            }

            return false;
        }

        public ITrade CreateTrade(ITradeRequest request)
        {
            var buyPosition = _positions[request.BaseSymbol];
            buyPosition.IsLocked = true;
            var sellPosition = _positions[request.QuoteSymbol];
            var feePosition = _positions[FeeAsset];
            ITrade trade = _factory.CreateTrade(buyPosition, sellPosition, feePosition, request);
            buyPosition.IsLocked = false;
            return trade;
        }
        //

        public bool CheckRequest(ITradeRequest what)
        {
            return CheckHasEnoughBalance(what) && !CheckHasOpenPositionAndVolume(what.BaseSymbol, what);
        }

        public void AjdustPosition(string accountPositionAsset, decimal accountPositionFree)
        {
            Logger.LogInformation($"Ajusting Position from Exchange {accountPositionAsset} - {accountPositionFree}");

            var pos = GetPosition(accountPositionAsset);
            pos.CreateTransaction(accountPositionFree);
        }
    }
}
