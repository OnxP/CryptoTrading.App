using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Core.Database.Config
{
    /// <summary>
    /// Database entity for strategy parameters.
    /// Stores parameter values with optimization bounds (min/max/step).
    /// </summary>
    [Table("StrategyParameters")]
    public class StrategyParameterDb
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string StrategyName { get; set; }

        [Required]
        [MaxLength(100)]
        public string ParameterName { get; set; }

        public double Value { get; set; }

        /// <summary>Minimum value for optimization grid search</summary>
        public double MinValue { get; set; }

        /// <summary>Maximum value for optimization grid search</summary>
        public double MaxValue { get; set; }

        /// <summary>Step size for grid search increments</summary>
        public double StepSize { get; set; }

        /// <summary>Category: Regime, Setup, Entry, Exit, Zone</summary>
        [MaxLength(50)]
        public string Category { get; set; }

        /// <summary>Group parameters into sets for comparison</summary>
        public int ParameterSetId { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}
