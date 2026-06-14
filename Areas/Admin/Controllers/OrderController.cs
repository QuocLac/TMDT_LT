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
using TMDT_LT.Services; // Thêm thư viện gọi VnPayService

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
                if (kw.StartsWith("#")) kw = kw.Replace("#", "");
                isNumeric = int.TryParse(kw, out exactOrderId);

                query = query.Where(o => o.OrderId.ToString().Contains(kw) ||
                                        (o.ShippingPhone != null && o.ShippingPhone.Contains(kw)));
            }

            if (!string.IsNullOrEmpty(status)) query = query.Where(o => o.Status == status);
            if (fromDate.HasValue) query = query.Where(o => o.OrderDate >= fromDate.Value);
            if (toDate.HasValue) query = query.Where(o => o.OrderDate <= toDate.Value.AddDays(1));

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
        // 2. XEM CHI TIẾT ĐƠN HÀNG
        // ====================================================================
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.OrderHistories)
                .Include(o => o.Payments)
                .Include(o => o.Shipping)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null) return NotFound();
            return View(order);
        }

        // ====================================================================
        // 3. TRUNG TÂM XỬ LÝ TRẠNG THÁI (ĐỘNG CƠ CỐT LÕI)
        // ====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int orderId, string newStatus, string? note)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.Shipping)
                .Include(o => o.Customer)
                .Include(o => o.Payments) // Nạp thêm thông tin thanh toán để xử lý hoàn tiền
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

            var payment = order.Payments.FirstOrDefault();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // -------------------------------------------------------------
                // LUỒNG 1: DUYỆT ĐƠN HÀNG (TRỪ KHO & GỌI API GIAO HÀNG)
                // -------------------------------------------------------------
                if (newStatus == "Đang xử lý" && order.Status == "Chờ xác nhận")
                {
                    // 1.1 Kiểm tra và trừ tồn kho
                    foreach (var detail in order.OrderDetails)
                    {
                        if (detail.Variant != null)
                        {
                            if (detail.Variant.Stock < detail.Quantity)
                                return Json(new { success = false, message = $"Sản phẩm mã #{detail.VariantId} không đủ tồn kho." });

                            detail.Variant.Stock -= detail.Quantity;
                        }
                    }

                    // 1.2 Gọi API Đơn vị vận chuyển (GHTK/GHN)
                    var defaultCarrier = await _context.ShippingCarriers.FirstOrDefaultAsync(c => c.IsActive && c.IsDefault);
                    if (defaultCarrier == null)
                    {
                        return Json(new { success = false, message = "Lỗi vận hành: Chưa cấu hình đơn vị vận chuyển mặc định." });
                    }

                    // Mock Gọi API và lấy mã vận đơn về
                    string returnedTrackingNumber = "3PL" + defaultCarrier.CarrierCode + DateTime.Now.Ticks.ToString().Substring(11);
                    var shipInfo = order.Shipping?.FirstOrDefault();
                    if (shipInfo != null)
                    {
                        shipInfo.TrackingNumber = returnedTrackingNumber;
                        shipInfo.Carrier = defaultCarrier.CarrierName;
                    }
                }

                // -------------------------------------------------------------
                // LUỒNG 2: HỦY ĐƠN / HOÀN ĐƠN (HOÀN TIỀN & HOÀN KHO)
                // -------------------------------------------------------------
                else if (newStatus == "Đã hủy" || newStatus == "Trả hàng/Hoàn tiền")
                {
                    // 2.1 KIỂM TRA LUỒNG TIỀN VÀ GỌI API BANK REFUND (Nếu đã thanh toán)
                    if (payment != null && payment.PaymentStatus == "Đã thanh toán")
                    {
                        string transactionDateStr = payment.PaymentDate?.ToString("yyyyMMddHHmmss") ?? DateTime.Now.ToString("yyyyMMddHHmmss");
                        string adminEmail = User.Identity?.Name ?? "admin_system";

                        // Lấy Service VNPay để gọi hàm Hoàn tiền tự động
                        var vnPayService = HttpContext.RequestServices.GetRequiredService<VnPayService>();
                        bool isRefundSuccess = await vnPayService.RequestBankRefundAsync(order.OrderId, order.TotalAmount ?? 0, transactionDateStr, adminEmail);

                        if (!isRefundSuccess)
                        {
                            return Json(new { success = false, message = "Cổng ngân hàng từ chối lệnh hoàn tiền tự động. Vui lòng đối soát lại số dư hoặc mã giao dịch." });
                        }

                        payment.PaymentStatus = "Đã hoàn tiền";
                        note = (note ?? "") + " [Hệ thống: Đã kích hoạt lệnh Refund hoàn trả tiền về tài khoản ngân hàng của khách thành công].";
                    }

                    // 2.2 ĐỀN BÙ LẠI TỒN KHO 
                    // (Chỉ đền bù nếu đơn hàng đã chuyển qua trạng thái 'Đang xử lý' vì lúc đó kho mới bị trừ)
                    if (order.Status == "Đang xử lý" || order.Status == "Đang giao")
                    {
                        foreach (var detail in order.OrderDetails)
                        {
                            if (detail.Variant != null) detail.Variant.Stock += detail.Quantity ?? 0;
                        }
                    }
                }

                // -------------------------------------------------------------
                // LUỒNG 3: HOÀN THÀNH ĐƠN (TÍCH ĐIỂM THÀNH VIÊN)
                // -------------------------------------------------------------
                else if (newStatus == "Hoàn thành" && order.Status == "Đang giao")
                {
                    if (order.Customer != null)
                    {
                        decimal totalAmount = order.TotalAmount ?? 0;
                        int pointsEarned = (int)(totalAmount / 100000); // 100k = 1 điểm

                        if (pointsEarned > 0)
                        {
                            order.Customer.RewardPoints += pointsEarned;
                            int currentPoints = order.Customer.RewardPoints;

                            if (currentPoints >= 600) order.Customer.CustomerType = "Kim Cương";
                            else if (currentPoints >= 300) order.Customer.CustomerType = "Vàng";
                            else if (currentPoints >= 100) order.Customer.CustomerType = "Bạc";
                            else order.Customer.CustomerType = "Newbie";

                            note = (note ?? "") + $" [Hệ thống: Tích lũy +{pointsEarned} điểm thành viên. Hạng: {order.Customer.CustomerType}].";
                        }
                    }
                }

                // Cập nhật trạng thái và ghi vết (Log)
                order.Status = newStatus;

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = newStatus,
                    UpdatedAt = DateTime.Now,
                    Note = note ?? $"Hành động điều phối trạng thái: {newStatus}" // Lý do hủy đơn hoặc ghi chú sẽ được điền vào đây
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi luồng nghiệp vụ: " + ex.Message });
            }
        }

        // ====================================================================
        // 4. XUẤT EXCEL BÁO CÁO DOANH THU
        // ====================================================================
        public async Task<IActionResult> ExportToExcel(string searchKeyword, string status, DateTime? fromDate, DateTime? toDate)
        {
            var query = _context.Orders.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                string kw = searchKeyword.Trim().ToLower();
                if (kw.StartsWith("#")) kw = kw.Replace("#", "");
                query = query.Where(o => o.OrderId.ToString().Contains(kw) || o.ShippingPhone.Contains(kw));
            }
            if (!string.IsNullOrEmpty(status)) query = query.Where(o => o.Status == status);
            if (fromDate.HasValue) query = query.Where(o => o.OrderDate >= fromDate.Value);
            if (toDate.HasValue) query = query.Where(o => o.OrderDate <= toDate.Value.AddDays(1));

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
        // 5. IN HÓA ĐƠN ĐIỆN TỬ
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
        // 6. WEBHOOK DỰ PHÒNG CHUYỂN KHOẢN NGÂN HÀNG (Giữ nguyên gốc)
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

            if (order == null || order.Status == "Hoàn thành" || order.Status == "Đã hủy")
                return Json(new { success = false });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (gatewayData.AmountTransferred >= order.TotalAmount)
                {
                    if (order.Status == "Chờ xác nhận")
                    {
                        foreach (var detail in order.OrderDetails)
                        {
                            if (detail.Variant != null) detail.Variant.Stock -= detail.Quantity;
                        }
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
                return Json(new { success = false, message = "Thiếu tiền thanh toán." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, error = ex.Message });
            }
        }
    }

    public class BankTransferModel
    {
        public int OrderIdReference { get; set; }
        public decimal AmountTransferred { get; set; }
        public string TransactionId { get; set; } = string.Empty;
    }
}