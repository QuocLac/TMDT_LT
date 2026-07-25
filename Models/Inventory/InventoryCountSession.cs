using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models;

[Index(nameof(CountCode), IsUnique = true)]
[Index(nameof(WarehouseId), nameof(Status), nameof(CreatedAt))]
[Table("InventoryCountSessions")]
public sealed class InventoryCountSession
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int CountSessionId { get; set; }

    [Required, MaxLength(40)]
    public string CountCode { get; set; } = string.Empty;

    public int WarehouseId { get; set; }

    [Required, MaxLength(30)]
    public string ScopeType { get; set; } = InventoryCountScopeTypes.Cycle;

    [Required, MaxLength(30)]
    public string Status { get; set; } = InventoryCountSessionStatuses.Counting;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? PostedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public int? CreatedByAccountId { get; set; }
    public int? SubmittedByAccountId { get; set; }
    public int? PostedByAccountId { get; set; }
    public int? CancelledByAccountId { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    [ForeignKey(nameof(WarehouseId))]
    public Warehouses Warehouse { get; set; } = null!;

    [InverseProperty(nameof(InventoryCountLine.CountSession))]
    public ICollection<InventoryCountLine> Lines { get; set; }
        = new List<InventoryCountLine>();
}
