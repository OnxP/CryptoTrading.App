using System.Data.Entity;
using CryptoTrading.App.Monitor.Trade;

namespace CryptoTrading.App.Monitor.Database
{
    public class HistoricTradeContext : DbContext
    {
        public HistoricTradeContext(string connectionString) : base(connectionString)
        {
        }
        public DbSet<HistoricTrades> HistoricTrades { get; set; }
    }
}
