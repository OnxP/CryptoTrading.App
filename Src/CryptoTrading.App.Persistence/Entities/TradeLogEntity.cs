using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Persistence.Entities
{
    [Table("trade_log")]
    public class TradeLogEntity
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("exchange_id")]
        public string ExchangeId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("strategy_name")]
        public string StrategyName { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("symbol")]
        public string Symbol { get; set; }

        [Required]
        [MaxLength(10)]
        [Column("side")]
        public string Side { get; set; }

        [Column("entry_price")]
        public double EntryPrice { get; set; }

        [Column("exit_price")]
        public double ExitPrice { get; set; }

        [Column("quantity")]
        public double Quantity { get; set; }

        [Column("pnl")]
        public double Pnl { get; set; }

        [Column("entry_time")]
        public DateTime EntryTime { get; set; }

        [Column("exit_time")]
        public DateTime ExitTime { get; set; }

        [MaxLength(50)]
        [Column("signal_type")]
        public string SignalType { get; set; }

        [Column("metadata_json", TypeName = "jsonb")]
        public string MetadataJson { get; set; }
    }
}
