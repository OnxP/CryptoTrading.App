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
        void ClosePosition(ITrade trade);
    }
}
