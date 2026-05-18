using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Broker.Account
{
    public class FuturesAccount : IAccount
    {
        private readonly ILogger<FuturesAccount> _logger;
        private const decimal FeeBuffer = 0.02m;

        public TradingVenue VenueType => TradingVenue.Futures;
        public IPositions Positions { get; }

        public FuturesAccount(IPositions positions, ILogger<FuturesAccount> logger)
        {
            Positions = positions;
            _logger = logger;
        }

        public bool HasSufficientBalance(ITradeRequest request)
        {
            // Futures: always check quote asset (collateral), only need Amount / Leverage
            var position = Positions.GetPosition(request.QuoteSymbol);
            if (position == null)
                return false;

            var marginRequired = request.Amount / request.Leverage;
            var requiredWithFee = marginRequired + (marginRequired * FeeBuffer);

            if (position.FreeAmount <= 0 || position.FreeAmount < requiredWithFee)
            {
                _logger.LogWarning(
                    "Insufficient margin for {Symbol}: need {Required} {Asset} (leverage {Leverage}x), have {Available}",
                    request.Symbol, requiredWithFee, request.QuoteSymbol, request.Leverage, position.FreeAmount);
                return false;
            }

            return true;
        }

        public void UpdatePositionFromFill(ExchangeOrder order, ITradeRequest request)
        {
            // Futures: only collateral (quote) moves; base is a contract exposure, not a balance transfer
            var quotePosition = Positions.GetPosition(request.QuoteSymbol);
            var marginUsed = order.QuoteQuantity / request.Leverage;

            if (order.Side == ExchangeOrderSide.Buy)
            {
                quotePosition.CreateTransaction(-marginUsed);
            }
            else
            {
                quotePosition.CreateTransaction(marginUsed);
            }
        }

        public void SyncBalance(string asset, decimal freeAmount)
        {
            Positions.AjdustPosition(asset, freeAmount);
        }
    }
}
