using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Broker.Account
{
    public interface IAccount
    {
        TradingVenue VenueType { get; }
        IPositions Positions { get; }
        bool HasSufficientBalance(ITradeRequest request);
        void UpdatePositionFromFill(ExchangeOrder order, ITradeRequest request);
        void SyncBalance(string asset, decimal freeAmount);
    }
}
