using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoTrading.App.Core.Database.Config
{
    /// <summary>
    /// Entity Framework entity for the AccountSnapshots database table.
    /// Stores periodic snapshots of exchange balances for audit trail.
    /// </summary>
    [Table("AccountSnapshots")]
    public class AccountSnapshotDb
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ExchangeId { get; set; }

        [Required]
        [MaxLength(20)]
        public string Asset { get; set; }

        public decimal FreeBalance { get; set; }

        public decimal LockedBalance { get; set; }

        public DateTime SnapshotDate { get; set; } = DateTime.UtcNow;
    }
}
