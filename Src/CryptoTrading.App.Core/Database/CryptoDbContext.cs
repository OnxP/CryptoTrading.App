using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Text;

namespace CryptoTrading.App.Core.Database
{
    public class CryptoDBContext : DbContext
    {
        public CryptoDBContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True") { }
        public virtual DbSet<CandleStickDb> CandleSticks { get; set; }
    }
}
