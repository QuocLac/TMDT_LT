using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models;

public static class InventoryCountSessionStatuses
{
    public const string Counting = "Counting";
    public const string PendingApproval = "PendingApproval";
    public const string Posted = "Posted";
    public const string Cancelled = "Cancelled";

    public static readonly string[] OpenStatuses =
    [
        Counting,
        PendingApproval
    ];
}

public static class InventoryCountScopeTypes
{
    public const string Cycle = "Cycle";
    public const string Full = "Full";
}

[Index(nameof(CountCode), IsUnique = true)]
[Index(nameof(WarehouseId), nameof(Status), nameof(CreatedAt))]
[Table("InventoryCountSessions")]
public sealed class InventoryCountSessions
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

    [InverseProperty(nameof(InventoryCountLines.CountSession))]
    public ICollection<InventoryCountLines> Lines { get; set; }
        = new List<InventoryCountLines>();
}
