using System;
using System.Data.Entity;
using Binance;

namespace CryptoTrading.App.Core
{
    public interface IConfig
    {
        void Load();
        CandlestickInterval Interval { get; set; }
        bool EndProcess { get; set; }
        RunTypeEnum RunType { get; set; }
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
        void Update();
        void SetContext(DbContext context);
    }
}