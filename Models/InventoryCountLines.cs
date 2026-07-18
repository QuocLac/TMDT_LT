using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models;

public static class InventoryCountReasonCodes
{
    public const string Damage = "DAMAGE";
    public const string Loss = "LOSS";
    public const string Found = "FOUND";
    public const string Miscount = "MISCOUNT";
    public const string ReturnToStock = "RETURN_TO_STOCK";
    public const string DataCorrection = "DATA_CORRECTION";
    public const string Other = "OTHER";

    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Damage] = "Hư hỏng / không còn bán được",
            [Loss] = "Thất thoát / không tìm thấy",
            [Found] = "Tìm thấy hàng chưa ghi nhận",
            [Miscount] = "Sai sót lần kiểm đếm trước",
            [ReturnToStock] = "Hàng đủ điều kiện nhập lại kho",
            [DataCorrection] = "Điều chỉnh dữ liệu lịch sử",
            [Other] = "Lý do khác"
        };
}

[Index(nameof(CountSessionId), nameof(VariantId), IsUnique = true)]
[Index(nameof(VariantId), nameof(CountedAt))]
[Table("InventoryCountLines")]
public sealed class InventoryCountLines
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
    [InverseProperty(nameof(InventoryCountSessions.Lines))]
    public InventoryCountSessions CountSession { get; set; } = null!;

    [ForeignKey(nameof(VariantId))]
    public ProductVariants Variant { get; set; } = null!;

    [InverseProperty(nameof(InventoryLots.AdjustmentLine))]
    public ICollection<InventoryLots> AdjustmentLots { get; set; }
        = new List<InventoryLots>();
}
