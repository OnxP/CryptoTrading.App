using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Text;

namespace CryptoTrading.App.Core.Database.Config
{
    public class TradingPairsContext : DbContext
    {
        public TradingPairsContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True")
        { }
        public virtual DbSet<TradingPairsDb> TradingPairs { get; set; }
    }
}
