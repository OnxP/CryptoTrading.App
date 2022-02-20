using System.Data.Entity;

namespace CryptoTrading.App.Core.Database.Config
{
    public class TradingPairsContext : DbContext
    {
        public TradingPairsContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True")
        { }
        public virtual DbSet<TradingPairsDb> TradingPairs { get; set; }
    }
}
