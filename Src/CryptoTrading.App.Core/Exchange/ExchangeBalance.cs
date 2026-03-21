namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeBalance
    {
        public string ExchangeId { get; set; }
        public string Asset { get; set; }
        public decimal Free { get; set; }
        public decimal Locked { get; set; }
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
