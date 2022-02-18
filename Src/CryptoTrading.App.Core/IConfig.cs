using System.Security;
using Binance;
using CryptoTrading.App.Core;

namespace CryptoTrading.App.Process
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
        SecureString EmailPassword { get; set; }
        int EmailPort { get; set; }
        string HtmlTemplate { get; set; }
    }
}