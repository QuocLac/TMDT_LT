using System.Collections.ObjectModel;

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

    public static IReadOnlyDictionary<string, string> Labels { get; } =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Damage] = "Hư hỏng / không còn bán được",
                [Loss] = "Thất thoát / không tìm thấy",
                [Found] = "Tìm thấy hàng chưa ghi nhận",
                [Miscount] = "Sai sót lần kiểm đếm trước",
                [ReturnToStock] = "Hàng đủ điều kiện nhập lại kho",
                [DataCorrection] = "Điều chỉnh dữ liệu lịch sử",
                [Other] = "Lý do khác"
            });
}

public static class InventoryCountSessionStatuses
{
    public const string Counting = "Counting";
    public const string PendingApproval = "PendingApproval";
    public const string Posted = "Posted";
    public const string Cancelled = "Cancelled";

    public static IReadOnlySet<string> OpenStatuses { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Counting,
            PendingApproval
        };
}

public static class InventoryCountScopeTypes
{
    public const string Cycle = "Cycle";
    public const string Full = "Full";
}
