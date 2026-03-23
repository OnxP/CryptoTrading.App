using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Core.Database.Config
{
    /// <summary>
    /// Entity Framework entity for the ExchangeConfigs database table.
    /// Stores per-exchange API credentials and configuration.
    /// </summary>
    [Table("ExchangeConfigs")]
    public class ExchangeConfigDb
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ExchangeId { get; set; }

        [MaxLength(200)]
        public string ApiKey { get; set; }

        [MaxLength(200)]
        public string ApiSecret { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(20)]
        public string RunType { get; set; } = "BackTesting";

        public int RefreshIntervalMinutes { get; set; } = 1440;

        public DateTime? LastRefreshed { get; set; }

        [MaxLength(500)]
        public string Notes { get; set; }

        /// <summary>
        /// Convert to runtime ExchangeConfig model
        /// </summary>
        public Exchange.ExchangeConfig ToExchangeConfig()
        {
            return new Exchange.ExchangeConfig
            {
                Id = Id,
                ExchangeId = ExchangeId,
                ApiKey = ApiKey,
                ApiSecret = ApiSecret,
                IsActive = IsActive,
                RunType = RunType,
                RefreshIntervalMinutes = RefreshIntervalMinutes,
                LastRefreshed = LastRefreshed,
                Notes = Notes
            };
        }
    }
}
