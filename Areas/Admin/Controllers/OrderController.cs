using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        public OrderController(ApplicationDbContext context) => _context = context;

        // 1. Danh sách tất cả đơn hàng
        public async Task<IActionResult> Index(string status = "")
        {
            var query = _context.Orders.AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            return View(orders);
        }

        // 2. Xem chi tiết đơn hàng để xử lý
        public async Task<IActionResult> Detail(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Variant)
                        .ThenInclude(v => v.Product)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null) return NotFound();

            return View(order);
        }

        // 3. API Cập nhật trạng thái đơn hàng
        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int orderId, string newStatus)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

            // Nếu đơn hàng đang bị hủy mà muốn chuyển sang trạng thái khác (hoặc ngược lại), 
            // phải xử lý cẩn thận bài toán Tồn kho.
            if (newStatus == "Đã hủy" && order.Status != "Đã hủy")
            {
                // Mở Transaction để Hoàn kho
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    order.Status = newStatus;

                    foreach (var detail in order.OrderDetails)
                    {
                        var variant = await _context.ProductVariants.FindAsync(detail.VariantId);
                        if (variant != null)
                        {
                            // 1. Cộng lại tồn kho
                            variant.Stock = (variant.Stock ?? 0) + detail.Quantity;

                            // 2. Ghi Thẻ kho (Loại: RETURN / CANCEL)
                            var invLog = new InventoryTransactions
                            {
                                VariantId = (int)detail.VariantId,
                                TransactionType = "RETURN",
                                Quantity = (int)detail.Quantity, // CỘNG vào kho
                                ReferenceId = order.OrderId,
                                TransactionDate = DateTime.Now,
                                AccountId = 1, // Giả lập Admin thao tác
                                Note = $"Hoàn kho do hủy đơn hàng #{order.OrderId}"
                            };
                            _context.InventoryTransactions.Add(invLog);
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Lỗi hệ thống khi hoàn kho." });
                }
            }
            else if (order.Status == "Đã hủy" && newStatus != "Đã hủy")
            {
                // Thực tế ít khi cho khôi phục đơn đã hủy, thường bắt khách đặt lại. 
                // Nhưng nếu làm, phải check xem kho còn hàng để trừ lại không. (Tạm bỏ qua để giữ logic an toàn)
                return Json(new { success = false, message = "Không thể phục hồi đơn đã hủy. Vui lòng tạo đơn mới." });
            }
            else
            {
                // Chỉ cập nhật trạng thái bình thường (Chờ xác nhận -> Đang giao -> Hoàn thành)
                order.Status = newStatus;
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true, message = "Cập nhật trạng thái thành công." });
        }
    }
}