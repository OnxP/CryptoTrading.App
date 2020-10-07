using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Broker
{
    public class Positions : IPositions
    {
        public Dictionary<string, IPosition> _positions;
        public bool CheckBalance(string sellSymbol, string sellAmount)
        {
            throw new NotImplementedException();
        }

        public bool CheckOpenPosition(string requestBuySymbol)
        {
            throw new NotImplementedException();
        }

        public ITrade CreateTrade(ITradeRequest request, StopLossMonitor stopLossMonitor)
        {
            throw new NotImplementedException();
        }

        public void UpdatePosition(Order order)
        {
            throw new NotImplementedException();
        }
    }
}
