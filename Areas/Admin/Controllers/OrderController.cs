using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;

        public OrderController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // ====================================================================
        // 1. DANH SÁCH ĐƠN HÀNG & BỘ LỌC
        // ====================================================================
        public async Task<IActionResult> Index(string searchKeyword, string status, DateTime? fromDate, DateTime? toDate)
        {
            // TẢI THÔNG BÁO HOÀN TRẢ CHƯA ĐỌC ĐỂ HIỂN THỊ LÊN WIDGET
            ViewBag.ReturnAlerts = await _context.OrderReturns
                .Include(r => r.Order)
                .Where(r => r.Status == "Chờ duyệt" && r.IsAlertAdminRead == false)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();

            ViewBag.Keyword = searchKeyword;
            ViewBag.Status = status;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            var query = _context.Orders.AsQueryable();
            bool hasSearch = !string.IsNullOrWhiteSpace(searchKeyword);
            string kw = "";
            int exactOrderId = -1;
            bool isNumeric = false;

            if (hasSearch)
            {
                kw = searchKeyword.Trim().ToLower();
                if (kw.StartsWith("#"))
                {
                    kw = kw.Replace("#", "");
                }

                isNumeric = int.TryParse(kw, out exactOrderId);

                query = query.Where(o => o.OrderId.ToString().Contains(kw) ||
                                        (o.ShippingPhone != null && o.ShippingPhone.Contains(kw)));
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(o => o.OrderDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(o => o.OrderDate <= toDate.Value.AddDays(1));
            }

            if (hasSearch)
            {
                if (isNumeric)
                {
                    query = query.OrderByDescending(o => o.OrderId == exactOrderId)
                                 .ThenByDescending(o => o.OrderId.ToString().Contains(kw))
                                 .ThenBy(o => o.OrderId);
                }
                else
                {
                    query = query.OrderBy(o => o.OrderId);
                }
            }
            else
            {
                query = query.OrderByDescending(o => o.OrderDate);
            }

            var orders = await query.ToListAsync();
            return View(orders);
        }

        // ====================================================================
        // API: TẮT THÔNG BÁO (KÍCH HOẠT KHI CLICK VÀO WIDGET)
        // ====================================================================
        [HttpPost]
        public async Task<IActionResult> MarkReturnAlertAsRead(int returnId)
        {
            var ret = await _context.OrderReturns.FindAsync(returnId);
            if (ret != null)
            {
                ret.IsAlertAdminRead = true;
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        // ====================================================================
        // 2. XEM CHI TIẾT ĐƠN HÀNG (ĐÃ INCLUDE KHIẾU NẠI)
        // ====================================================================
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.OrderHistories)
                .Include(o => o.Payments)
                .Include(o => o.Shipping)
                .Include(o => o.OrderReturns)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null) return NotFound();

            return View(order);
        }

        // ====================================================================
        // 3. ĐỘNG CƠ CẬP NHẬT TRẠNG THÁI GIAO HÀNG (TÁCH BIỆT HỦY VÀ HOÀN TRẢ)
        // ====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int orderId, string newStatus, string? note)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.Shipping)
                .Include(o => o.Customer)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đơn hàng." });
            }

            if (order.Status == "Đã hủy" || order.Status == "Đã hoàn trả")
            {
                return Json(new { success = false, message = "Đơn hàng đã ở trạng thái kết thúc, không thể cập nhật tiếp." });
            }

            var payment = order.Payments.FirstOrDefault();

            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                if (newStatus == "Đang xử lý" && order.Status == "Chờ xác nhận")
                {
                    await DeductStockForLegacyOrderAsync(order);

                    var defaultCarrier = await _context.ShippingCarriers.FirstOrDefaultAsync(c => c.IsActive && c.IsDefault);
                    var shipInfo = order.Shipping?.FirstOrDefault();

                    if (shipInfo != null && defaultCarrier != null)
                    {
                        shipInfo.TrackingNumber = "3PL" + defaultCarrier.CarrierCode + DateTime.Now.Ticks.ToString().Substring(11);
                        shipInfo.Carrier = defaultCarrier.CarrierName;
                    }

                    note = note ?? "Admin xác nhận đơn hàng. Kho đã được giữ/trừ trước đó hoặc vừa được trừ cho đơn cũ.";
                }
                else if (newStatus == "Đang giao" && order.Status == "Đang xử lý")
                {
                    note = note ?? "Đã xuất kho và bàn giao kiện hàng cho Shipper.";
                }
                else if (newStatus == "Đã giao" && order.Status == "Đang giao")
                {
                    note = note ?? "Đơn vị vận chuyển báo phát hàng thành công. Bắt đầu thời hạn 7 ngày kiểm tra đổi trả.";
                }
                else if (newStatus == "Hoàn thành" && order.Status == "Đã giao")
                {
                    if (order.Customer != null)
                    {
                        int pointsEarned = (int)((order.TotalAmount ?? 0) / 100000);
                        if (pointsEarned > 0)
                        {
                            order.Customer.RewardPoints += pointsEarned;
                            int cp = order.Customer.RewardPoints;
                            order.Customer.CustomerType = cp >= 600 ? "Kim Cương" : cp >= 300 ? "Vàng" : cp >= 100 ? "Bạc" : "Newbie";
                            note = (note ?? "") + $" [Hệ thống: +{pointsEarned} điểm].";
                        }
                    }

                    order.CompletedDate = DateTime.Now;
                }
                else if (newStatus == "Đã hủy")
                {
                    if (order.Status == "Đã giao" || order.Status == "Hoàn thành")
                    {
                        throw new Exception("Đơn đã giao/hoàn thành phải đi qua quy trình hoàn trả, không hủy trực tiếp.");
                    }

                    if (payment != null && payment.PaymentStatus == "Đã thanh toán" && payment.PaymentMethod == "VNPAY")
                    {
                        var vnPayService = HttpContext.RequestServices.GetRequiredService<VnPayService>();
                        string transactionDateStr = payment.PaymentDate?.ToString("yyyyMMddHHmmss") ?? DateTime.Now.ToString("yyyyMMddHHmmss");
                        bool isRefundSuccess = await vnPayService.RequestBankRefundAsync(order.OrderId, order.TotalAmount ?? 0, transactionDateStr, User.Identity?.Name ?? "admin");

                        if (!isRefundSuccess)
                        {
                            throw new Exception("Lệnh hoàn tiền VNPay thất bại.");
                        }

                        payment.PaymentStatus = "Đã hoàn tiền";
                        note = (note ?? "") + " [Đã kích hoạt lệnh Refund VNPAY thành công].";
                    }

                    await RestoreStockAndFlashSaleSlotAsync(order, "Hủy đơn");
                    note = (note ?? "") + " [Đã hoàn kho và hoàn suất Flash Sale nếu có].";
                }
                else
                {
                    throw new Exception($"Không thể chuyển trạng thái từ '{order.Status}' sang '{newStatus}'.");
                }

                order.Status = newStatus;

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = newStatus,
                    UpdatedAt = DateTime.Now,
                    Note = note ?? $"Hành động điều phối trạng thái: {newStatus}"
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ====================================================================
        // 4. XUẤT EXCEL BÁO CÁO TÀI CHÍNH
        // ====================================================================
        public async Task<IActionResult> ExportToExcel(string searchKeyword, string status, DateTime? fromDate, DateTime? toDate)
        {
            var query = _context.Orders.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                string kw = searchKeyword.Trim().ToLower().Replace("#", "");
                query = query.Where(o => o.OrderId.ToString().Contains(kw) || o.ShippingPhone.Contains(kw));
            }
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }
            if (fromDate.HasValue)
            {
                query = query.Where(o => o.OrderDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                query = query.Where(o => o.OrderDate <= toDate.Value.AddDays(1));
            }

            var orders = await query.OrderByDescending(o => o.OrderDate).ToListAsync();

            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("Mã Đơn Hàng,Khách Hàng,Số Điện Thoại,Ngày Khởi Tạo,Tổng Giá Trị,Trạng Thái");

            foreach (var o in orders)
            {
                csvBuilder.AppendLine($"#ORD-{o.OrderId},{o.ShippingFullName},{o.ShippingPhone},{o.OrderDate?.ToString("dd/MM/yyyy HH:mm")},{o.TotalAmount},{o.Status}");
            }

            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var csvBytes = Encoding.UTF8.GetBytes(csvBuilder.ToString());

            return File(bom.Concat(csvBytes).ToArray(), "text/csv", $"BaoCao_DonHang_{DateTime.Now:yyyyMMdd}.csv");
        }

        // ====================================================================
        // 5. MÀN HÌNH IN HÓA ĐƠN
        // ====================================================================
        public async Task<IActionResult> PrintInvoice(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.Shipping)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null) return NotFound();

            return View(order);
        }

        // ====================================================================
        // 6. WEBHOOK DỰ PHÒNG CHUYỂN KHOẢN NGÂN HÀNG TRỰC TIẾP
        // ====================================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> BankPaymentWebhook([FromBody] BankTransferModel gatewayData)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant)
                .Include(o => o.Payments)
                .Include(o => o.Shipping)
                .FirstOrDefaultAsync(o => o.OrderId == gatewayData.OrderIdReference);

            if (order == null || order.Status == "Hoàn thành" || order.Status == "Đã hủy" || order.Status == "Đã hoàn trả")
            {
                return Json(new { success = false });
            }

            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                if (gatewayData.AmountTransferred < order.TotalAmount)
                {
                    return Json(new { success = false, message = "Thiếu tiền thanh toán." });
                }

                if (order.Status == "Chờ xác nhận")
                {
                    await DeductStockForLegacyOrderAsync(order);
                    order.Status = "Đang xử lý";

                    var defaultCarrier = await _context.ShippingCarriers.FirstOrDefaultAsync(c => c.IsActive && c.IsDefault);
                    string assignedCarrier = defaultCarrier != null ? defaultCarrier.CarrierName : "Hệ thống vận chuyển nội bộ";

                    var shipInfo = order.Shipping?.FirstOrDefault();
                    if (shipInfo != null)
                    {
                        shipInfo.TrackingNumber = "VNDON" + DateTime.Now.Ticks.ToString().Substring(10);
                        shipInfo.Carrier = assignedCarrier;
                    }
                }

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = "Đang xử lý",
                    UpdatedAt = DateTime.Now,
                    Note = $"[Tự động] Nhận thành công {string.Format("{0:N0}", gatewayData.AmountTransferred)}đ qua Ngân hàng."
                });

                var payment = order.Payments?.FirstOrDefault();
                if (payment != null)
                {
                    payment.PaymentStatus = "Đã thanh toán";
                    payment.PaymentDate = DateTime.Now;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, error = ex.Message });
            }
        }

        private async Task DeductStockForLegacyOrderAsync(Orders order)
        {
            if (order.IsStockDeducted) return;

            foreach (var detail in order.OrderDetails)
            {
                int quantity = detail.Quantity ?? 0;
                if (quantity <= 0 || detail.Variant == null) continue;

                int currentStock = detail.Variant.Stock ?? 0;
                if (currentStock < quantity)
                {
                    throw new Exception($"Hết tồn kho mã #{detail.VariantId}.");
                }

                detail.Variant.Stock = currentStock - quantity;
                _context.InventoryTransactions.Add(new InventoryTransactions
                {
                    VariantId = detail.VariantId ?? 0,
                    TransactionType = "ADJUST",
                    Quantity = -quantity,
                    ReferenceId = order.OrderId,
                    TransactionDate = DateTime.Now,
                    Note = detail.IsFlashSaleItem ? $"Trừ kho đơn #{order.OrderId} - Flash Sale" : $"Trừ kho đơn #{order.OrderId}"
                });
            }

            order.IsStockDeducted = true;
            order.StockDeductedAt = DateTime.Now;
        }

        private async Task RestoreStockAndFlashSaleSlotAsync(Orders order, string reason)
        {
            if (!order.IsStockDeducted) return;

            foreach (var detail in order.OrderDetails)
            {
                int quantity = detail.Quantity ?? 0;
                if (quantity <= 0) continue;

                if (detail.Variant != null)
                {
                    detail.Variant.Stock = (detail.Variant.Stock ?? 0) + quantity;
                    _context.InventoryTransactions.Add(new InventoryTransactions
                    {
                        VariantId = detail.VariantId ?? 0,
                        TransactionType = "ADJUST",
                        Quantity = quantity,
                        ReferenceId = order.OrderId,
                        TransactionDate = DateTime.Now,
                        Note = $"{reason} #{order.OrderId}"
                    });
                }

                if (detail.IsFlashSaleItem && detail.FlashSaleItemId.HasValue)
                {
                    var flashSaleItem = await _context.FlashSaleItems.FindAsync(detail.FlashSaleItemId.Value);
                    if (flashSaleItem != null)
                    {
                        flashSaleItem.Sold = Math.Max(0, flashSaleItem.Sold - quantity);
                    }
                }
            }

            order.IsStockDeducted = false;
            order.StockDeductedAt = null;
        }

    }

    public class BankTransferModel
    {
        public int OrderIdReference { get; set; }
        public decimal AmountTransferred { get; set; }
        public string TransactionId { get; set; } = string.Empty;
    }
}