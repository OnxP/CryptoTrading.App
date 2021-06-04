using System.Data.Entity;

namespace CryptoTrading.App.Core.Database.Indicators
{
    public class IndicatorContext<T> : DbContext where T : class
    {
        public IndicatorContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True") { }
        public virtual DbSet<T> Values { get; set; }
    }
}
