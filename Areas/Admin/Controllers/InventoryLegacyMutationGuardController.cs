using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TMDT_LT.Areas.Admin.Controllers;

/// <summary>
/// Chặn các route ghi tồn legacy sau khi Phase D được đưa vào vận hành.
/// Các route đọc cũ vẫn hoạt động để tránh làm hỏng báo cáo, nhưng mọi mutation
/// phải đi qua receiving, count journal, transfer hoặc distribution mới.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory")]
public sealed class InventoryLegacyMutationGuardController : Controller
{
    [HttpPost("AdjustStock", Order = -1000)]
    public IActionResult AdjustStock() => Disabled("Kiểm kê trực tiếp", "/Admin/Inventory");

    [HttpPost("UpdateLotDetail", Order = -1000)]
    public IActionResult UpdateLotDetail() => Disabled("Sửa trực tiếp tồn/giá vốn lô", "/Admin/Inventory");

    [HttpPost("DeleteLotPhysical", Order = -1000)]
    public IActionResult DeleteLotPhysical() => Disabled("Xóa lô trực tiếp", "/Admin/Inventory");

    [HttpPost("RestoreLot", Order = -1000)]
    public IActionResult RestoreLot() => Disabled("Khôi phục lô trực tiếp", "/Admin/Inventory");

    [HttpPost("TransferLot", Order = -1000)]
    public IActionResult TransferLot() => Disabled("Điều chuyển một lô legacy", "/Admin/Inventory/Operations");

    [HttpPost("SubmitPO", Order = -1000)]
    public IActionResult SubmitPO() => Disabled("Nhập kho legacy", "/Admin/Inventory/CreatePO");

    [HttpPost("QuickAddProduct", Order = -1000)]
    public IActionResult QuickAddProduct() => Disabled("Tạo sản phẩm trong màn nhận hàng", "/Admin/Product");

    [HttpPost("EstimateSO", Order = -1000)]
    public IActionResult EstimateSO() => Disabled("Preview xuất kho không có anti-forgery", "/Admin/Inventory/CreateSO");

    [HttpPost("SubmitSO", Order = -1000)]
    public IActionResult SubmitSO() => Disabled("Xuất kho legacy không có anti-forgery", "/Admin/Inventory/CreateSO");

    private ObjectResult Disabled(string operation, string replacementUrl)
    {
        return StatusCode(StatusCodes.Status410Gone, new
        {
            success = false,
            code = "INVENTORY_LEGACY_MUTATION_DISABLED",
            message = $"{operation} đã bị khóa để bảo vệ sổ lô, serial và giá vốn.",
            replacementUrl
        });
    }
}
