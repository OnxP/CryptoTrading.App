using System.Data.Entity;

namespace CryptoTrading.App.Core.Database.StoreTrades
{
    public class TradeContext : DbContext
    {
        public TradeContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True") { }
        public DbSet<TradesDb> Trades { get; set; }
    }
}
