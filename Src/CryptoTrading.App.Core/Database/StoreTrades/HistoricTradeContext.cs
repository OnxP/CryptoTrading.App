using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Text;
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
