using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core
{
    public interface IBroker
    {
        void SetLimitOrder(ITrade trade, decimal currentStopLoss);
        void SetNewLimitOrder(ITrade trade, decimal currentStopLoss);
        void ClosePosition(ITrade trade);
    }
}
