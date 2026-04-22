using System;
using System.Data.Entity;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    public interface IConfig
    {
        void Load();
        CandleInterval Interval { get; set; }
        bool EndProcess { get; set; }
        RunTypeEnum RunType { get; set; }

        /// <summary>
        /// Venue the runtime routes orders through. Spot = no leverage.
        /// Margin = leveraged spot with borrow/auto-repay. Futures = USD-M
        /// perpetuals. Defaults to Spot for back-compat with pre-PR1a configs.
        /// </summary>
        TradingVenue TradingVenue { get; set; }

        /// <summary>
        /// Target leverage for Margin / Futures venues. Ignored on Spot (must be 1).
        /// Applied to the exchange on startup via <see cref="Exchange.IExchangeProvider.SetLeverageAsync"/>.
        /// </summary>
        int Leverage { get; set; }
        string StoreTradesConnectionString { get; set; }
        string EmailServer { get; set; }
        string EmailFrom { get; set; }
        string EmailTo { get; set; }
        string EmailPassword { get; set; }
        int EmailPort { get; set; }
        string HtmlTemplate { get; set; }
        double NoOfTrades { get; set; }
        double Risk { get; set; }
        double Increment { get; set; }
        string FilePath { get; set; }
        DateTime From { get; set; }
        DateTime To { get; set; }
        double StartBtcAmount { get; set; }
        double StartBnbAmount { get; set; }
        bool UseFixedAmount { get; set; }
        double FixedAmount { get; set; }
        double PercentDailyVolume { get; set; }
        string ApiKey { get; set; }
        string ApiKeySecret { get; set; }
        int NumberOfCandleSticksToLoad { get; set; }

        void Update();
        void SetContext(DbContext context);
    }
}