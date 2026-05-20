using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace CryptoTrading.App.Broker.Position
{
    public class BrokerPositions : IPositions
    {
        private readonly Dictionary<string, IPosition> _positions;
        private readonly ILogger<BrokerPositions> _logger;

        public BrokerPositions(ILogger<BrokerPositions> logger)
        {
            _logger = logger;
            _positions = new Dictionary<string, IPosition>();
        }

        public BrokerPositions(ILogger<BrokerPositions> logger, Dictionary<string, IPosition> positionsProvider)
            : this(logger)
        {
            _positions = positionsProvider;
        }

        public IPosition GetPosition(string asset)
        {
            if (!_positions.ContainsKey(asset))
                _positions.Add(asset, new BrokerPosition(asset, 0.0m));
            return _positions[asset];
        }

        private string FeeAsset => "USDT";

        public bool CheckHasEnoughBalance(ITradeRequest what)
        {
            var balanceSymbol = what.Leverage > 1
                ? what.QuoteSymbol
                : (what.OrderSide == ExchangeOrderSide.Buy
                    ? what.QuoteSymbol
                    : what.BaseSymbol);

            var checkAmount = what.Leverage > 1
                ? what.Amount / what.Leverage
                : what.Amount;

            if (!_positions.TryGetValue(balanceSymbol, out var position))
                return false;

            bool hasEnoughBalance = balanceSymbol == FeeAsset
                ? position.CheckHasEnoughBalanceIncludingFee(checkAmount)
                : position.CheckHasEnoughBalance(checkAmount);

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
                return _positions[requestBuySymbol].HasOpenPosition;
            return false;
        }

        public bool CheckRequest(ITradeRequest what)
        {
            return CheckHasEnoughBalance(what) && !CheckHasOpenPositionAndVolume(what.BaseSymbol, what);
        }

        public void AjdustPosition(string accountPositionAsset, decimal accountPositionFree)
        {
            _logger.LogInformation("Adjusting position {Asset} to {Amount}", accountPositionAsset, accountPositionFree);
            var pos = GetPosition(accountPositionAsset);
            pos.CreateTransaction(accountPositionFree);
        }
    }
}
