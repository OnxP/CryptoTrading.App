using System;
using System.Collections.Generic;
using System.Text;
using Binance;
using CryptoTrading.App.Core.Database.Config;
using CryptoTrading.App.Process;

namespace CryptoTrading.App.Core.Database
{
    internal class DatabaseAccountConfig :IAccountConfig
    {
        public DatabaseAccountConfig(IConfig config)
        {
            Config = config;
        }

        public IConfig Config { get; }

        //public 
        public List<Symbol> LoadCurrencies()
        {
            using (var context = new CryptoDbContext())
            {
                throw new NotImplementedException();
            }
        }

        public List<AccountBalance> LoadPositions()
        {
            throw new NotImplementedException();
        }
    }
}
