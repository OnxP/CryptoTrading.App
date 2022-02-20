using System.Data.Entity;

namespace CryptoTrading.App.Core.Database.Config
{
    public class CryptoDbConfigContext : DbContext
    {
        public CryptoDbConfigContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True")
        { }
        public virtual DbSet<CryptoConfig> CryptoConfigs { get; set; }
    }
}
