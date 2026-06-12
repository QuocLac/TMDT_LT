using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration _config;

        public OrderController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

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
            // Bổ sung Include(o => o.Customer) để lấy được thông tin khách hàng phục vụ tự động hóa
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.Shipping)
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (newStatus == "Đang xử lý" && order.Status == "Chờ xác nhận")
                {
                    foreach (var detail in order.OrderDetails)
                    {
                        if (detail.Variant != null)
                        {
                            if (detail.Variant.Stock < detail.Quantity)
                                return Json(new { success = false, message = $"Sản phẩm mã #{detail.VariantId} không đủ tồn kho." });

                            detail.Variant.Stock -= detail.Quantity;
                        }
                    }

                    var defaultCarrier = await _context.ShippingCarriers.FirstOrDefaultAsync(c => c.IsActive && c.IsDefault);
                    if (defaultCarrier == null)
                    {
                        return Json(new { success = false, message = "Lỗi vận hành: Chưa cấu hình đơn vị vận chuyển mặc định." });
                    }

                    string apiToken = _config[$"ShippingAPI:{defaultCarrier.CarrierCode}:Token"] ?? "";
                    string baseUrl = _config[$"ShippingAPI:{defaultCarrier.CarrierCode}:BaseUrl"] ?? "";

                    if (string.IsNullOrEmpty(apiToken))
                    {
                        return Json(new { success = false, message = $"Chưa cấu hình Token bảo mật cho hãng {defaultCarrier.CarrierCode}." });
                    }

                    bool apiCallSuccess = true;
                    string returnedTrackingNumber = "3PL" + defaultCarrier.CarrierCode + DateTime.Now.Ticks.ToString().Substring(11);

                    if (apiCallSuccess)
                    {
                        var shipInfo = order.Shipping?.FirstOrDefault();
                        if (shipInfo != null)
                        {
                            shipInfo.TrackingNumber = returnedTrackingNumber;
                            shipInfo.Carrier = defaultCarrier.CarrierName;
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
                // LUỒNG TỰ ĐỘNG HÓA KHI ĐƠN HÀNG HOÀN THÀNH KHI ĐỐI SOÁT GIAO THÀNH CÔNG
                else if (newStatus == "Hoàn thành" && order.Status == "Đang giao")
                {
                    if (order.Customer != null)
                    {
                        // Thuật toán: Cứ 100.000 đ tổng hóa đơn đơn hàng = Tích lũy 1 điểm thưởng vào tài khoản
                        decimal totalAmount = order.TotalAmount ?? 0;
                        int pointsEarned = (int)(totalAmount / 100000);

                        if (pointsEarned > 0)
                        {
                            order.Customer.RewardPoints += pointsEarned;

                            // Tự động rẽ nhánh thăng hạng dựa theo mốc tích lũy RewardPoints mới
                            int currentPoints = order.Customer.RewardPoints;
                            if (currentPoints >= 600)
                            {
                                order.Customer.CustomerType = "Kim Cương";
                            }
                            else if (currentPoints >= 300)
                            {
                                order.Customer.CustomerType = "Vàng";
                            }
                            else if (currentPoints >= 100)
                            {
                                order.Customer.CustomerType = "Bạc";
                            }
                            else
                            {
                                order.Customer.CustomerType = "Newbie";
                            }

                            // Bổ sung ghi chú tự động vào hành trình đơn hàng để Admin tiện theo dõi
                            note = (note ?? "") + $" [Hệ thống: Tích lũy +{pointsEarned} điểm thành viên. Cập nhật hạng hiện tại: {order.Customer.CustomerType}].";
                        }
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