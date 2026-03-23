using System.Data.Entity;

namespace CryptoTrading.App.Core.Database.Config
{
    /// <summary>
    /// DbContext for strategy parameters.
    /// Follows the existing CryptoDbConfigContext pattern using EF6.
    /// </summary>
    public class StrategyParameterContext : DbContext
    {
        public StrategyParameterContext(string source)
            : base($"Data Source={source};Initial Catalog=CryptoDb;Integrated Security=True")
        {
            Database.CommandTimeout = 600;
        }

        public virtual DbSet<StrategyParameterDb> StrategyParameters { get; set; }
    }
}
