using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        public OrderController(ApplicationDbContext context) => _context = context;

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int orderId, string newStatus, string? note)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.Shipping)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (newStatus == "Đang xử lý" && order.Status == "Chờ xác nhận")
                {
                    // 1. Kiểm tra tồn kho hệ thống
                    foreach (var detail in order.OrderDetails)
                    {
                        if (detail.Variant != null)
                        {
                            if (detail.Variant.Stock < detail.Quantity)
                                return Json(new { success = false, message = $"Sản phẩm mã #{detail.VariantId} không đủ tồn kho." });

                            detail.Variant.Stock -= detail.Quantity;
                        }
                    }

                    // 2. TRỤC API ĐỘNG: Tìm đối tác vận chuyển đang kích hoạt trong hệ thống
                    var activeCarrier = await _context.ShippingCarriers.FirstOrDefaultAsync(c => c.IsActive == true);
                    if (activeCarrier == null)
                    {
                        return Json(new { success = false, message = "Lỗi vận hành: Chưa cấu hình hoặc bật cổng vận chuyển bên thứ 3 nào." });
                    }

                    // 3. GIẢ LẬP GIAO TIẾP MẠNG HTTP CLIENT ĐẨY ĐƠN SANG SERVER ĐỐI TÁC (GHTK/GHN)
                    // Trong thực tế, đoạn này sẽ thiết lập HttpClient để POST dữ liệu JSON sang activeCarrier.ApiUrl
                    // sử dụng Header mã hóa chứa activeCarrier.ApiToken.

                    bool apiCallSuccess = true; // Giả lập phản hồi kết nối Gateway thành công
                    string returnedTrackingNumber = "3PL" + activeCarrier.CarrierName.Substring(0, 2).ToUpper() + DateTime.Now.Ticks.ToString().Substring(11);

                    if (apiCallSuccess)
                    {
                        var shipInfo = order.Shipping?.FirstOrDefault();
                        if (shipInfo != null)
                        {
                            shipInfo.TrackingNumber = returnedTrackingNumber; // Ghi nhận mã vận đơn do API bên thứ 3 trả về
                            shipInfo.Carrier = activeCarrier.CarrierName;     // Ghi nhận tên hãng vận chuyển cấu hình
                        }
                    }
                    else
                    {
                        return Json(new { success = false, message = "Cổng kết nối API của đơn vị vận chuyển từ chối phản hồi dữ liệu." });
                    }
                }
                else if (newStatus == "Đã hủy" && order.Status == "Đang xử lý")
                {
                    foreach (var detail in order.OrderDetails)
                    {
                        if (detail.Variant != null) detail.Variant.Stock += detail.Quantity;
                    }
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
                return Json(new { success = false, message = "Lỗi luồng nghiệp vụ: " + ex.Message });
            }
        }

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
            csvBuilder.AppendLine("Mã Đơn Hàng,Khách Hàng,Số Điện Thại,Ngày Khởi Tạo,Tổng Giá Trị,Trạng Thái");

            foreach (var o in orders)
            {
                csvBuilder.AppendLine($"#ORD-{o.OrderId},{o.ShippingFullName},{o.ShippingPhone},{o.OrderDate?.ToString("dd/MM/yyyy HH:mm")},{o.TotalAmount},{o.Status}");
            }

            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var csvBytes = Encoding.UTF8.GetBytes(csvBuilder.ToString());
            return File(bom.Concat(csvBytes).ToArray(), "text/csv", $"BaoCao_DonHang_{DateTime.Now:yyyyMMdd}.csv");
        }

        public async Task<IActionResult> PrintInvoice(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.Shipping)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null) return NotFound();
            return View(order);
        }

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

                        var shipInfo = order.Shipping?.FirstOrDefault();
                        if (shipInfo != null)
                        {
                            shipInfo.TrackingNumber = "VNDON" + DateTime.Now.Ticks.ToString().Substring(10);
                            shipInfo.Carrier = "Giao Hàng Tiết Kiệm (GHTK)";
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