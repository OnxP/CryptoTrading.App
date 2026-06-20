using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Persistence.Entities
{
    [Table("strategy_parameters")]
    public class StrategyParameterEntity
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
        [MaxLength(100)]
        [Column("parameter_name")]
        public string ParameterName { get; set; }

        [Column("value")]
        public double Value { get; set; }

        [Column("min_value")]
        public double MinValue { get; set; }

        [Column("max_value")]
        public double MaxValue { get; set; }

        [Column("step_size")]
        public double StepSize { get; set; }

        [MaxLength(50)]
        [Column("category")]
        public string Category { get; set; }

        [Column("parameter_set_id")]
        public int ParameterSetId { get; set; } = 1;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("last_modified")]
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}
