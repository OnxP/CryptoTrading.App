using Binance;

namespace CryptoTrading.App.Process
{
    public interface IConfig
    {
        void Load();
        CandlestickInterval Interval { get; set; }
        bool EndProcess { get; set; }
    }
}