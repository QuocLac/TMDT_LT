using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models;

/// <summary>
/// Audit fields used by the inventory ledger. FIFO, warehouse, lot and
/// cost-snapshot fields remain in the inventory entity extension model.
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
