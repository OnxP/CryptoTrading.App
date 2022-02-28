using System.Data.Entity;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Database.StoreTrades
{
    public class HistoricTradeContext :DbContext
    {
        public HistoricTradeContext(string connectionString) : base(connectionString)
        {
        }
        public DbSet<HistoricTrades> HistoricTrades { get; set; }
    }
}
