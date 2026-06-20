using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Persistence.Entities
{
    [Table("algo_state")]
    public class AlgoStateEntity
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("strategy_name")]
        public string StrategyName { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("instance_id")]
        public string InstanceId { get; set; }

        [Column("state_json", TypeName = "jsonb")]
        public string StateJson { get; set; }

        [Column("last_bar_time")]
        public DateTime LastBarTime { get; set; }

        [Column("last_1h_bar_count")]
        public int Last1hBarCount { get; set; }

        [Column("last_4h_bar_count")]
        public int Last4hBarCount { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
