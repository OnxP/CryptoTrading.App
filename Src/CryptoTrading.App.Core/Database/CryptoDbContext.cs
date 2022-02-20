using System.Data.Entity;

namespace CryptoTrading.App.Core.Database
{
    public class CryptoDbContext : DbContext
    {
        public CryptoDbContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True")
        { Database.CommandTimeout = 180; }
        public virtual DbSet<CandleStickDb> CandleSticks { get; set; }
    }
}
