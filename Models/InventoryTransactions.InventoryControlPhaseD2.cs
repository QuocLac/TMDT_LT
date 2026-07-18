using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models;

/// <summary>
/// Phase D2 audit fields. Các trường FIFO/kho/lô và snapshot giá vốn
/// vẫn được giữ nguyên từ partial model của Phase B.
/// </summary>
public partial class InventoryTransactions
{
    [MaxLength(50)]
    public string? ReferenceType { get; set; }

    public int? QuantityBefore { get; set; }

    public int? QuantityAfter { get; set; }

    [MaxLength(40)]
    public string? ReasonCode { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? ValueImpact { get; set; }
}
