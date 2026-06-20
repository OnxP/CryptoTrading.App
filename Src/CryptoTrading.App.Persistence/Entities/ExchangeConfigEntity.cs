using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Persistence.Entities
{
    [Table("exchange_configs")]
    public class ExchangeConfigEntity
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("exchange_id")]
        public string ExchangeId { get; set; }

        [MaxLength(200)]
        [Column("api_key")]
        public string ApiKey { get; set; }

        [MaxLength(200)]
        [Column("api_secret")]
        public string ApiSecret { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [MaxLength(20)]
        [Column("run_type")]
        public string RunType { get; set; } = "BackTesting";

        [Column("refresh_interval_minutes")]
        public int RefreshIntervalMinutes { get; set; } = 1440;

        [Column("last_refreshed")]
        public DateTime? LastRefreshed { get; set; }

        [MaxLength(500)]
        [Column("notes")]
        public string Notes { get; set; }
    }
}
