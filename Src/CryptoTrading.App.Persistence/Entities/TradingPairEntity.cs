using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Persistence.Entities
{
    [Table("trading_pairs")]
    public class TradingPairEntity
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("exchange_id")]
        public string ExchangeId { get; set; } = "Binance";

        [Required]
        [MaxLength(50)]
        [Column("symbol")]
        public string Symbol { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;
    }
}
