using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models;

[Index(nameof(CountSessionId), nameof(VariantId), IsUnique = true)]
[Index(nameof(VariantId), nameof(CountedAt))]
[Table("InventoryCountLines")]
public sealed class InventoryCountLine
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int CountLineId { get; set; }

    public int CountSessionId { get; set; }
    public int VariantId { get; set; }
    public int SystemQuantity { get; set; }
    public int? CountedQuantity { get; set; }
    public int Difference { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCostSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? AdjustmentUnitCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal VarianceValue { get; set; }

    [MaxLength(40)]
    public string? ReasonCode { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime? CountedAt { get; set; }
    public int? CountedByAccountId { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    [ForeignKey(nameof(CountSessionId))]
    [InverseProperty(nameof(InventoryCountSession.Lines))]
    public InventoryCountSession CountSession { get; set; } = null!;

    [ForeignKey(nameof(VariantId))]
    public ProductVariants Variant { get; set; } = null!;

    [InverseProperty(nameof(InventoryLots.AdjustmentLine))]
    public ICollection<InventoryLots> AdjustmentLots { get; set; }
        = new List<InventoryLots>();
}
