namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Exchange-agnostic trading pair definition (replaces Binance.Symbol).
    /// Describes a tradable instrument on a specific exchange.
    /// </summary>
    public class ExchangeSymbol
    {
        public string ExchangeId { get; set; }
        public string Ticker { get; set; }          // e.g. "BTCUSDT"
        public string BaseAsset { get; set; }       // e.g. "BTC"
        public string QuoteAsset { get; set; }      // e.g. "USDT"
        public decimal MinQuantity { get; set; }
        public decimal MaxQuantity { get; set; }
        public decimal StepSize { get; set; }       // Quantity precision increment
        public decimal MinNotional { get; set; }    // Minimum order value in quote currency
        public decimal TickSize { get; set; }       // Price precision increment
        public bool IsActive { get; set; }

        public ExchangeSymbol() { }

        public ExchangeSymbol(string exchangeId, string ticker, string baseAsset, string quoteAsset)
        {
            ExchangeId = exchangeId;
            Ticker = ticker;
            BaseAsset = baseAsset;
            QuoteAsset = quoteAsset;
            IsActive = true;
        }

        /// <summary>
        /// Validates whether a given quantity meets this symbol's constraints
        /// </summary>
        public bool IsValidQuantity(decimal quantity)
        {
            return quantity >= MinQuantity
                && (MaxQuantity == 0 || quantity <= MaxQuantity);
        }

        /// <summary>
        /// Validates whether a given order value meets the minimum notional
        /// </summary>
        public bool MeetsMinNotional(decimal price, decimal quantity)
        {
            return price * quantity >= MinNotional;
        }
    }
}
