using CryptoTrading.App.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CryptoTrading.App.Persistence
{
    public class CryptoDbContextPg : DbContext
    {
        public CryptoDbContextPg(DbContextOptions<CryptoDbContextPg> options)
            : base(options)
        {
        }

        public DbSet<CandlestickEntity> Candlesticks { get; set; }
        public DbSet<ExchangeConfigEntity> ExchangeConfigs { get; set; }
        public DbSet<StrategyParameterEntity> StrategyParameters { get; set; }
        public DbSet<TradingPairEntity> TradingPairs { get; set; }
        public DbSet<ModelArtifactEntity> ModelArtifacts { get; set; }
        public DbSet<AlgoStateEntity> AlgoStates { get; set; }
        public DbSet<TradeLogEntity> TradeLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CandlestickEntity>(entity =>
            {
                entity.HasIndex(e => new { e.ExchangeId, e.Symbol, e.Interval, e.OpenTime })
                      .IsUnique()
                      .HasDatabaseName("ix_candlesticks_exchange_symbol_interval_time");

                entity.HasIndex(e => new { e.Symbol, e.Interval, e.OpenTime })
                      .HasDatabaseName("ix_candlesticks_symbol_interval_time");
            });

            modelBuilder.Entity<ModelArtifactEntity>(entity =>
            {
                entity.HasIndex(e => new { e.StrategyName, e.Version })
                      .IsUnique()
                      .HasDatabaseName("ix_model_artifacts_strategy_version");

                entity.HasIndex(e => new { e.StrategyName, e.IsActive })
                      .HasDatabaseName("ix_model_artifacts_strategy_active");
            });

            modelBuilder.Entity<AlgoStateEntity>(entity =>
            {
                entity.HasIndex(e => new { e.StrategyName, e.InstanceId })
                      .IsUnique()
                      .HasDatabaseName("ix_algo_state_strategy_instance");
            });

            modelBuilder.Entity<TradeLogEntity>(entity =>
            {
                entity.HasIndex(e => new { e.StrategyName, e.Symbol, e.EntryTime })
                      .HasDatabaseName("ix_trade_log_strategy_symbol_time");
            });

            modelBuilder.Entity<ExchangeConfigEntity>(entity =>
            {
                entity.HasIndex(e => e.ExchangeId)
                      .HasDatabaseName("ix_exchange_configs_exchange_id");
            });

            modelBuilder.Entity<TradingPairEntity>(entity =>
            {
                entity.HasIndex(e => new { e.ExchangeId, e.Symbol })
                      .IsUnique()
                      .HasDatabaseName("ix_trading_pairs_exchange_symbol");
            });
        }
    }
}
