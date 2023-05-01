using Binance;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    public class FixedProfitStopLimit : StopLimitBase
    {
        public FixedProfitStopLimit(decimal risk, decimal increment)
        {
            Risk =1-( risk / 100m);
            Increment = 1+(increment / 100m);
        }
        public override void MoveStopLimit()
        {
            StopLimitPrice = TargetPrice;
        }
        public override void Configure(Order order)
        {
            var price = order.Price;
            IsOpen = true;
            //StopLimitPrice = price * _risk;
            //TargetPrice = price * _increment;
        }
    }
}
