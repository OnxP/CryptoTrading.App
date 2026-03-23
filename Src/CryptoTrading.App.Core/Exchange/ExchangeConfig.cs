using System;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Runtime configuration for an exchange connection.
    /// Loaded from the ExchangeConfigs database table.
    /// </summary>
    public class ExchangeConfig
    {
        public int Id { get; set; }
        public string ExchangeId { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public bool IsActive { get; set; }
        public string RunType { get; set; }
        public int RefreshIntervalMinutes { get; set; } = 1440; // Daily
        public DateTime? LastRefreshed { get; set; }
        public string Notes { get; set; }

        /// <summary>
        /// Whether this exchange should be used for live trading
        /// </summary>
        public bool IsLive => RunType == "Live";

        /// <summary>
        /// Whether this exchange should be used for paper trading
        /// </summary>
        public bool IsLiveTesting => RunType == "LiveTesting";

        /// <summary>
        /// Whether this exchange should be used for backtesting
        /// </summary>
        public bool IsBackTesting => RunType == "BackTesting";

        /// <summary>
        /// Whether the account data should be refreshed based on the interval
        /// </summary>
        public bool NeedsRefresh =>
            !LastRefreshed.HasValue
            || DateTime.UtcNow - LastRefreshed.Value > TimeSpan.FromMinutes(RefreshIntervalMinutes);
    }
}
