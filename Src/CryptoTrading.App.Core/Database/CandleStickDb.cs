using Binance;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Security;
using System.Text;

namespace CryptoTrading.App.Core.Database
{
    public class CryptoConfig: IConfig
    { 
        private DbContext DbContext { get; }
        public CryptoConfig(DbContext context)
        {
            DbContext = context;
        }

        public bool IsRunning { get; set; }
        public void Load()
        {
            DbContext.Entry(this).Reload();
        }
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
        public SecureString EmailPassword { get; set; }
        public int EmailPort { get; set; }
        public string HtmlTemplate { get; set; }
        public double NoOfTrades { get; set; }
        public decimal Risk { get; set; }
        public decimal Increment { get; set; }
        public string FilePath { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal StartBtcAmount { get; set; }
        public decimal StartBnbAmount { get; set; }
        
    }
}