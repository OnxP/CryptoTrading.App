using System.Data.Entity;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Core.Database.StoreTrades
{
    public class HistoricTradeContext :DbContext
    {
        public HistoricTradeContext(string connectionString)
        {
        }
        public DbSet<HistoricTrades> Trades { get; set; }
    }
}
