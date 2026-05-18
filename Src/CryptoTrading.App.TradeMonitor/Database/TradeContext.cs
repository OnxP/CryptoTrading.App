using System.Data.Entity;
using CryptoTrading.App.Monitor.Trade;

namespace CryptoTrading.App.Monitor.Database
{
    public class TradeContext : DbContext
    {
        public TradeContext() : base(@"Data Source=localhost;Initial Catalog=CryptoDb;Integrated Security=True") { }
        public DbSet<TradesDb> Trades { get; set; }
    }
}
