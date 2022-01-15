using Binance;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoTrading.App.Core.Database
{
    public class CryptoConfigSymbols
    { 
        public CryptoConfigSymbols()
        {

        }

        public string Symbol { get; set; }
        public bool ValidSymbol { get; set; }
    }
}