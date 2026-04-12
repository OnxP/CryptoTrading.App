using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

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
            // Leveraged trades use quote asset (USDT) as collateral for both sides;
            // only the margin portion needs to be available.
            var balanceSymbol = what.Leverage > 1
                ? what.QuoteSymbol
                : (what.OrderSide == Binance.OrderSide.Buy
                    ? what.QuoteSymbol
                    : what.BaseSymbol);

            var checkAmount = what.Leverage > 1
                ? what.Amount / what.Leverage
                : what.Amount;

            if (!_positions.TryGetValue(balanceSymbol, out var position))
                return false;

            // Check main balance, including fee if fee asset is the same
            bool hasEnoughBalance = balanceSymbol == FeeAsset
                ? position.CheckHasEnoughBalanceIncludingFee(checkAmount)
                : position.CheckHasEnoughBalance(checkAmount);

            // If fee asset is *not* one of the legs, check separate fee balance on base symbol
            //need to convert symbol to fee asset using the latest price.(the broker will confirm the actual fee.)
            if (FeeAsset != what.QuoteSymbol &&
                FeeAsset != what.BaseSymbol &&
                _positions.TryGetValue(what.BaseSymbol, out var basePosition))
            {
                hasEnoughBalance = hasEnoughBalance &&
                                   _positions[FeeAsset].CheckHasEnoughBalanceFee(what);
            }

            return hasEnoughBalance;
        }

        public bool CheckHasOpenPositionAndVolume(string requestBuySymbol, ITradeRequest request)
        {
            if (_positions.ContainsKey(requestBuySymbol))
            {
                return _positions[requestBuySymbol].HasOpenPosition;
            }

            return false;
        }

        public ITrade CreateTrade(ITradeRequest request)
        {
            var basePosition = _positions[request.BaseSymbol];
            basePosition.IsLocked = true;
            var quotePosition = _positions[request.QuoteSymbol];
            var feePosition = _positions[FeeAsset];
            ITrade trade = _factory.CreateTrade(basePosition, quotePosition, feePosition);
            basePosition.IsLocked = false;
            return trade;
        }
        //

        public bool CheckRequest(ITradeRequest what)
        {
            return CheckHasEnoughBalance(what) && !CheckHasOpenPositionAndVolume(what.BaseSymbol, what);
        }

        public void AjdustPosition(string accountPositionAsset, decimal accountPositionFree)
        {
            Logger.LogInformation($"Ajusting Position from Binance {accountPositionAsset} - {accountPositionFree}");

            var pos = GetPosition(accountPositionAsset);
            pos.CreateTransaction(accountPositionFree);
        }
    }
}
