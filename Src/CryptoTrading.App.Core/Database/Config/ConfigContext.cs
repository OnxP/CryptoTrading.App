using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Text;

namespace CryptoTrading.App.Core.Database.Config
{
    class ConfigContext : DbContext
    {
        public ConfigContext() : base(@"Data Source=AnkurPC\AnkurPC;Initial Catalog=CryptoDb;Integrated Security=True")
        { }
        public virtual DbSet<ConfigDb> Config { get; set; }
    }
}
