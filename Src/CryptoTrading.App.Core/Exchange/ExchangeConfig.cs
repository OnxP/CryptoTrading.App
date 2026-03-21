namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeConfig
    {
        public string ExchangeId { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public bool IsActive { get; set; }
        public RunTypeEnum RunType { get; set; }
    }
}
