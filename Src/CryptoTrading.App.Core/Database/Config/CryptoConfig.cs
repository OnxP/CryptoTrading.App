using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using Binance;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core.Database.Config
{
    public class CryptoConfig: IConfig
    {
        private DbContext DbContext { get; set; }
        public int Id { get; set; }

        public void SetContext(DbContext context)
        {
            DbContext = context;
        }
        public void Load()
        {
            DbContext.Entry(this).Reload();
        }

        public bool UseFixedAmount { get; set; }
        public double FixedAmount { get; set; }
        public double PercentDailyVolume { get; set; }
        public string ApiKey { get; set; }
        public string ApiKeySecret { get; set; }
        public int NumberOfCandleSticksToLoad { get; set; }

        public void Update()
        {
            DbContext.SaveChanges();
        }

        public CandlestickInterval Interval { get; set; }
        public bool EndProcess { get; set; }
        public RunTypeEnum RunType { get; set; }
        public string StoreTradesConnectionString { get; set; }
        public string EmailServer { get; set; }
        public string EmailFrom { get; set; }
        public string EmailTo { get; set; }
        public string EmailPassword { get; set; }
        public int EmailPort { get; set; }
        public string HtmlTemplate { get; set; }
        public double NoOfTrades { get; set; }
        public double Risk { get; set; }
        public double Increment { get; set; }
        public string FilePath { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public double StartBtcAmount { get; set; }
        public double StartBnbAmount { get; set; }

        // ---- Venue routing (added PR 1a) ----
        // Stored as int in the DB so the schema stays compatible with the
        // existing CryptoConfigs table — callers set TradingVenue via the
        // enum property below, and EF persists the underlying int.

        [Column("TradingVenue")]
        public int TradingVenueRaw { get; set; }

        /// <inheritdoc />
        [NotMapped]
        public TradingVenue TradingVenue
        {
            get => (TradingVenue)TradingVenueRaw;
            set => TradingVenueRaw = (int)value;
        }

        /// <inheritdoc />
        public int Leverage { get; set; } = 1;
    }
}