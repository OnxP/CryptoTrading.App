namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Exchange-agnostic account balance (replaces Binance.AccountBalance).
    /// Represents a single asset balance on a specific exchange.
    /// </summary>
    public class ExchangeBalance
    {
        public string ExchangeId { get; set; }
        public string Asset { get; set; }
        public decimal Free { get; set; }
        public decimal Locked { get; set; }

        /// <summary>
        /// Total balance (free + locked)
        /// </summary>
        public decimal Total => Free + Locked;

        public ExchangeBalance() { }

        public ExchangeBalance(string exchangeId, string asset, decimal free, decimal locked = 0m)
        {
            ExchangeId = exchangeId;
            Asset = asset;
            Free = free;
            Locked = locked;
        }
    }
}
