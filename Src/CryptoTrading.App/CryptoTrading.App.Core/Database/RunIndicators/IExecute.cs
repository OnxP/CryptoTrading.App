using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Text;

namespace CryptoTrading.App.Core.Database.Indicators
{
    public interface IExecute
    {
        void Execute(List<CandleStickDb> candleStick);
        public DbContext Context { get;}

        void Dispose();
    }
}
