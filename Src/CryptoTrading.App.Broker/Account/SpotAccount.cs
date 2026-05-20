using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker.Account
{
    public class SpotAccount : IAccount
    {
        private readonly ILogger<SpotAccount> _logger;
        private const decimal FeeBuffer = 0.02m;

        public TradingVenue VenueType => TradingVenue.Spot;
        public IPositions Positions { get; }

        public SpotAccount(IPositions positions, ILogger<SpotAccount> logger)
        {
            Positions = positions;
            _logger = logger;
        }

        public bool HasSufficientBalance(ITradeRequest request)
        {
            var balanceSymbol = request.OrderSide == ExchangeOrderSide.Buy
                ? request.QuoteSymbol
                : request.BaseSymbol;

            var position = Positions.GetPosition(balanceSymbol);
            if (position == null)
                return false;

            var requiredAmount = request.Amount;
            var requiredWithFee = requiredAmount + (requiredAmount * FeeBuffer);

            if (position.FreeAmount <= 0 || position.FreeAmount < requiredWithFee)
            {
                _logger.LogWarning(
                    "Insufficient {Asset} balance for {Symbol}: need {Required}, have {Available}",
                    balanceSymbol, request.Symbol, requiredWithFee, position.FreeAmount);
                return false;
            }

            return true;
        }

        public void UpdatePositionFromFill(ExchangeOrder order, ITradeRequest request)
        {
            var basePosition = Positions.GetPosition(request.BaseSymbol);
            var quotePosition = Positions.GetPosition(request.QuoteSymbol);

            if (order.Side == ExchangeOrderSide.Buy)
            {
                basePosition.CreateTransaction(order.FilledQuantity);
                quotePosition.CreateTransaction(-order.QuoteQuantity);
            }
            else
            {
                basePosition.CreateTransaction(-order.FilledQuantity);
                quotePosition.CreateTransaction(order.QuoteQuantity);
            }
        }

        public void SyncBalance(string asset, decimal freeAmount)
        {
            Positions.AjdustPosition(asset, freeAmount);
        }
    }
}
