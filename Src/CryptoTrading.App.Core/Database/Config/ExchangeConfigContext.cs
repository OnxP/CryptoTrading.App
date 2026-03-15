using System.Data.Entity;

namespace CryptoTrading.App.Core.Database.Config
{
    /// <summary>
    /// DbContext for accessing the ExchangeConfigs table.
    /// Follows same pattern as CryptoDbConfigContext.
    /// </summary>
    public class ExchangeConfigContext : DbContext
    {
        public ExchangeConfigContext(string source)
            : base($"Data Source={source};Initial Catalog=CryptoDb;Integrated Security=True")
        {
            Database.CommandTimeout = 600;
        }

        public virtual DbSet<ExchangeConfigDb> ExchangeConfigs { get; set; }
    }
}
