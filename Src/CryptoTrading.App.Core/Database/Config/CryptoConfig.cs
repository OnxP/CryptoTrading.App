using System;
using System.Data.Entity;
using Binance;

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
        
    }
}