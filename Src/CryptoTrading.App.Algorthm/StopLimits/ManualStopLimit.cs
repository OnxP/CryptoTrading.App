using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    class ManualStopLimit : StopLimitBase
    {
        private bool _triggerUpdate = false;
        public ManualStopLimit(decimal risk, decimal increment)
        {
            Risk = risk;
            Increment = increment;
        }
        public override void Configure(ExchangeOrder order)
        {
            base.Configure(order);
            _triggerUpdate = false;
        }
        public override void ManualChangeSL(decimal sl)
        {
            if (sl > StopLimitPrice)
            {
                StopLimitPrice = sl;
                _triggerUpdate = true;
            }
        }
        public override void MoveStopLimit()
        {
            //do nothing.
        }

        public override bool RequestUpdateOfStopLimit(decimal closePrice)
        {
            if (TargetPrice <= closePrice) return true;

            var update = _triggerUpdate;
            _triggerUpdate = false;
            return update;
        }
    }
}
