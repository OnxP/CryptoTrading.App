using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    class ManualFixedProfitStopLimit : ManualStopLimit
    {
        public ManualFixedProfitStopLimit(decimal risk, decimal increment) : base(risk,increment)
        {
        }
        public override void MoveStopLimit()
        {
            StopLimitPrice = TargetPrice;
        }
        public override void Configure(ExchangeOrder order)
        {
            var price = order.Price;
            IsOpen = true;
            //StopLimitPrice = price * _risk;
            //TargetPrice = price * _increment;
        }
    }
}
