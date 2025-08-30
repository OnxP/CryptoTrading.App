using System.Data.Entity;

namespace CryptoTrading.App.Core.Database
{
    public class CryptoDbContext : DbContext
    {
        public CryptoDbContext() : base(@"Data Source=ANKUR-PC\APDATASERVICE;Initial Catalog=CryptoDb;Integrated Security=True")
        { Database.CommandTimeout = 600; }
        public virtual DbSet<CandleStickDb> CandleSticks { get; set; }
    }
}
