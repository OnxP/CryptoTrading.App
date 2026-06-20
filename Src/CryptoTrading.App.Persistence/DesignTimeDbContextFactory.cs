using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CryptoTrading.App.Persistence
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CryptoDbContextPg>
    {
        public CryptoDbContextPg CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<CryptoDbContextPg>();
            optionsBuilder.UseNpgsql("Host=localhost;Database=CryptoDb;Username=crypto;Password=crypto");
            return new CryptoDbContextPg(optionsBuilder.Options);
        }
    }
}
