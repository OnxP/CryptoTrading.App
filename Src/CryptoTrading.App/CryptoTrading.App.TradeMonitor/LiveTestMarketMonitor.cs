using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Monitor
{
    //Class monitors the position in the open trade and adjusts the stop loss, this could work on live streaming data 
    //Input(Initial) - Trade details.
    //Input(continuous) - CandleStick Processing.
    //StaticInput - Stop loss type and limit.
    //Output - Change Stop Limit Order

    //Processing logic
    //Initial - Configure Stoploss Monitor from Open trade. and set a stop limit order.
    //Continuous - Monitor price and once it hits a threshold reset stoploss to limit order X% below threshold then adjust threshold

    public class LiveTestMarketMonitor : LiveMarketMonitor
    {
        public LiveTestMarketMonitor(string symbol, IBinanceApi api, IBinanceApiUser user) :base(symbol,api,user)
        {
        }

        public override bool CheckOrder(ITransaction order)
        {
            return true;
        } 

    }


}
