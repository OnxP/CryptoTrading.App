namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeSymbol
    {
        public string ExchangeId { get; set; }
        public string Ticker { get; set; }
        public string BaseAsset { get; set; }
        public string QuoteAsset { get; set; }
        public decimal MinQuantity { get; set; }
        public decimal MaxQuantity { get; set; }
        public decimal StepSize { get; set; }
        public decimal MinNotional { get; set; }
        public decimal TickSize { get; set; }
        public bool IsActive { get; set; }

        // Aliases for backward compat with InclusiveRange pattern
        public decimal PriceIncrement => TickSize;
        public decimal QuantityIncrement => StepSize;

        public ExchangeSymbol() { }

        public ExchangeSymbol(string exchangeId, string ticker, string baseAsset, string quoteAsset)
        {
            ExchangeId = exchangeId;
            Ticker = ticker;
            BaseAsset = baseAsset;
            QuoteAsset = quoteAsset;
            IsActive = true;
        }

        public bool IsValidQuantity(decimal quantity)
        {
            return quantity >= MinQuantity
                && (MaxQuantity == 0 || quantity <= MaxQuantity);
        }

        public bool MeetsMinNotional(decimal price, decimal quantity)
        {
            return price * quantity >= MinNotional;
        }
    }
}
