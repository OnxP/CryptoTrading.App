using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Persistence.Entities
{
    [Table("model_artifacts")]
    public class ModelArtifactEntity
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("strategy_name")]
        public string StrategyName { get; set; }

        [Column("version")]
        public int Version { get; set; }

        [Column("trained_at")]
        public DateTime TrainedAt { get; set; }

        [Column("manifest_json", TypeName = "jsonb")]
        public string ManifestJson { get; set; }

        [Column("model_1h_onnx")]
        public byte[] Model1hOnnx { get; set; }

        [Column("model_4h_onnx")]
        public byte[] Model4hOnnx { get; set; }

        [Column("val_r2_1h")]
        public double ValR2_1h { get; set; }

        [Column("val_r2_4h")]
        public double ValR2_4h { get; set; }

        [Column("train_n_bars")]
        public int TrainNBars { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("promoted_at")]
        public DateTime? PromotedAt { get; set; }
    }
}
