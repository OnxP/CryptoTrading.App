using System;

namespace CryptoTrading.App.Core.Exchange
{
    public class ExchangeConfig
    {
        public int Id { get; set; }
        public string ExchangeId { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public bool IsActive { get; set; }
        public string RunType { get; set; } = "BackTesting";
        public int RefreshIntervalMinutes { get; set; } = 1440;
        public DateTime? LastRefreshed { get; set; }
        public string Notes { get; set; }
    }
}
