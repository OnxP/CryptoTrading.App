using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Binance;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core
{
    public interface IBroker
    {
        Task<Order> SetLimitOrder(ITrade trade, decimal currentStopLoss);
        Task<Order> SetNewLimitOrder(ITrade trade,Order order, decimal currentStopLoss);
        void ClosePosition(ITrade trade);
    }
}
