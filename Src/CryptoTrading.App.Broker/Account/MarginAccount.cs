using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker.Account
{
    public class MarginAccount : IAccount
    {
        private readonly ILogger<MarginAccount> _logger;
        private const decimal FeeBuffer = 0.02m;

        public TradingVenue VenueType => TradingVenue.Margin;
        public IPositions Positions { get; }

        public MarginAccount(IPositions positions, ILogger<MarginAccount> logger)
        {
            Positions = positions;
            _logger = logger;
        }

        public bool HasSufficientBalance(ITradeRequest request)
        {
            // Margin: check collateral (quote asset). Borrowing power = collateral * leverage.
            // The required margin is Amount / Leverage (same math as futures for now).
            var position = Positions.GetPosition(request.QuoteSymbol);
            if (position == null)
                return false;

            var marginRequired = request.Leverage > 1
                ? request.Amount / request.Leverage
                : request.Amount;

            var requiredWithFee = marginRequired + (marginRequired * FeeBuffer);

            if (position.FreeAmount <= 0 || position.FreeAmount < requiredWithFee)
            {
                _logger.LogWarning(
                    "Insufficient margin collateral for {Symbol}: need {Required} {Asset}, have {Available}",
                    request.Symbol, requiredWithFee, request.QuoteSymbol, position.FreeAmount);
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
