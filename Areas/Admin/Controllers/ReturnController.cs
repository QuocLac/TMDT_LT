using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ReturnController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;

        public ReturnController(ApplicationDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // ====================================================================
        // HÀM HELPER: GỬI EMAIL TỰ ĐỘNG THÔNG BÁO CHO KHÁCH HÀNG
        // ====================================================================
        private async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var smtpSettings = _config.GetSection("SmtpSettings");
                var mailMessage = new MailMessage
                {
                    From = new MailAddress(smtpSettings["SenderEmail"], smtpSettings["SenderName"]),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                using var smtpClient = new SmtpClient(smtpSettings["Server"])
                {
                    Port = int.Parse(smtpSettings["Port"]),
                    Credentials = new NetworkCredential(smtpSettings["SenderEmail"], smtpSettings["Password"]),
                    EnableSsl = true
                };
                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi gửi Email: " + ex.Message);
            }
        }

        // ====================================================================
        // 1. DASHBOARD TỔNG DANH SÁCH KHIẾU NẠI (Dành cho trang Return/Index)
        // ====================================================================
        public async Task<IActionResult> Index(string status)
        {
            var query = _context.OrderReturns
                .Include(r => r.Order)
                .Include(r => r.Customer)
                    .ThenInclude(c => c.Account)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(r => r.Status == status);
            }

            var returns = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            ViewBag.CurrentStatus = status;

            return View(returns);
        }

        // ====================================================================
        // 2. API: CẬP NHẬT TRẠNG THÁI TIẾN TRÌNH VÀ RÓT XUỐNG BẢNG ORDERS
        // ====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReturnStatus(int returnId, string newStatus, string adminNote)
        {
            var returnReq = await _context.OrderReturns
                .Include(r => r.Order)
                .Include(r => r.Customer)
                    .ThenInclude(c => c.Account)
                .FirstOrDefaultAsync(r => r.ReturnId == returnId);

            if (returnReq == null) return Json(new { success = false, message = "Không tìm thấy hồ sơ khiếu nại." });

            returnReq.Status = newStatus;
            returnReq.AdminNote = adminNote;
            string customerEmail = returnReq.Customer?.Account?.Email ?? "";

            // NẾU TỪ CHỐI: ĐÁNH BẬT ĐƠN HÀNG VỀ LẠI "HOÀN THÀNH"
            if (newStatus == "Đã từ chối")
            {
                returnReq.ResolvedAt = DateTime.Now;

                if (returnReq.Order != null)
                {
                    returnReq.Order.Status = "Hoàn thành";
                    _context.Orders.Update(returnReq.Order); // Rót dữ liệu
                }

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = returnReq.OrderId,
                    Status = "Hoàn thành",
                    UpdatedAt = DateTime.Now,
                    Note = $"[Hệ thống] Yêu cầu Trả hàng bị TỪ CHỐI. Lý do Admin: {adminNote}. Khôi phục trạng thái Hoàn thành."
                });

                if (!string.IsNullOrEmpty(customerEmail))
                {
                    string emailBody = $"<h3>Chào {returnReq.Customer?.FullName},</h3><p>Cửa hàng rất tiếc phải thông báo yêu cầu đổi trả cho đơn hàng <b>#ORD-{returnReq.OrderId}</b> của bạn đã bị từ chối.</p><p><b>Lý do:</b> {adminNote}</p>";
                    _ = SendEmailAsync(customerEmail, $"[PHONE.ST] Cập nhật khiếu nại đơn hàng #ORD-{returnReq.OrderId}", emailBody);
                }
            }
            else
            {
                // RÓT TRỰC TIẾP TRẠNG THÁI ("Chờ khách trả hàng", "Đang kiểm định"...) XUỐNG BẢNG ORDERS
                if (returnReq.Order != null)
                {
                    returnReq.Order.Status = newStatus;
                    _context.Orders.Update(returnReq.Order); // Rót dữ liệu
                }

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = returnReq.OrderId,
                    Status = newStatus,
                    UpdatedAt = DateTime.Now,
                    Note = $"[Tiến trình Trả hàng] Cập nhật mốc: {newStatus}. Ghi chú: {adminNote}"
                });

                if (newStatus == "Chờ khách trả hàng" && !string.IsNullOrEmpty(customerEmail))
                {
                    string emailBody = $"<h3>Chào {returnReq.Customer?.FullName},</h3><p>Yêu cầu trả hàng cho đơn <b>#ORD-{returnReq.OrderId}</b> của bạn đã được <b>CHẤP NHẬN</b>.</p><p>Vui lòng đóng gói sản phẩm cẩn thận và gửi về địa chỉ kho của chúng tôi theo hướng dẫn trong Lịch sử đơn hàng.</p><p><b>Ghi chú từ Shop:</b> {adminNote}</p>";
                    _ = SendEmailAsync(customerEmail, $"[PHONE.ST] Yêu cầu trả hàng #ORD-{returnReq.OrderId} được chấp nhận", emailBody);
                }
            }

            _context.OrderReturns.Update(returnReq);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Đã cập nhật tiến trình." });
        }

        // ====================================================================
        // 3. API: KIỂM ĐỊNH KHO BÃI & XÁC NHẬN HOÀN TIỀN (BƯỚC QUYẾT TOÁN)
        // ====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessRefund(int returnId, bool isProductIntact, string adminNote, string transactionRef)
        {
            var returnReq = await _context.OrderReturns
                .Include(r => r.Order).ThenInclude(o => o.OrderDetails).ThenInclude(od => od.Variant)
                .Include(r => r.Order).ThenInclude(o => o.Payments)
                .Include(r => r.Customer).ThenInclude(c => c.Account)
                .FirstOrDefaultAsync(r => r.ReturnId == returnId);

            if (returnReq == null || returnReq.Status != "Đang kiểm định")
                return Json(new { success = false, message = "Hồ sơ không hợp lệ hoặc chưa đến bước Kiểm định kho." });

            var order = returnReq.Order;
            if (order == null) return Json(new { success = false, message = "Lỗi liên kết dữ liệu đơn hàng gốc." });

            var payment = order.Payments.FirstOrDefault();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // PHÂN LUỒNG HOÀN TIỀN VNPAY
                if (payment != null && payment.PaymentStatus == "Đã thanh toán" && payment.PaymentMethod == "VNPAY")
                {
                    var vnPayService = HttpContext.RequestServices.GetRequiredService<TMDT_LT.Services.VnPayService>();
                    string transactionDateStr = payment.PaymentDate?.ToString("yyyyMMddHHmmss") ?? DateTime.Now.ToString("yyyyMMddHHmmss");

                    bool isRefundSuccess = true; // Môi trường DEV để test localhost
                    // bool isRefundSuccess = await vnPayService.RequestBankRefundAsync(order.OrderId, order.TotalAmount ?? 0, transactionDateStr, User.Identity?.Name ?? "admin");

                    if (!isRefundSuccess)
                    {
                        return Json(new { success = false, message = "Cổng VNPay từ chối lệnh hoàn tiền." });
                    }

                    transactionRef = $"VNPAY_AUTO_{DateTime.Now:yyyyMMddHHmmss}";
                }
                else if (string.IsNullOrWhiteSpace(transactionRef))
                {
                    return Json(new { success = false, message = "Vui lòng nhập Mã Giao Dịch Ngân Hàng sau khi đã quét QR." });
                }

                // XỬ LÝ KHO BÃI
                // Nếu hàng còn nguyên: nhập lại kho vật lý đúng một lần.
                // Nếu hàng hỏng: không nhập lại kho, nhưng vẫn đóng cờ kho để tránh hoàn lặp.
                if (isProductIntact)
                {
                    if (order.IsStockDeducted)
                    {
                        foreach (var detail in order.OrderDetails)
                        {
                            int quantity = detail.Quantity ?? 0;
                            if (quantity <= 0 || detail.Variant == null) continue;

                            detail.Variant.Stock = (detail.Variant.Stock ?? 0) + quantity;
                            _context.InventoryTransactions.Add(new InventoryTransactions
                            {
                                VariantId = detail.VariantId ?? 0,
                                TransactionType = "ADJUST",
                                Quantity = quantity,
                                ReferenceId = order.OrderId,
                                TransactionDate = DateTime.Now,
                                Note = $"Nhập lại kho do hoàn trả đơn #{order.OrderId}"
                            });
                        }

                        order.IsStockDeducted = false;
                        order.StockDeductedAt = null;
                    }

                    adminNote = "[Nhập lại Kho] " + adminNote;
                }
                else
                {
                    order.IsStockDeducted = false;
                    order.StockDeductedAt = null;
                    adminNote = "[Hàng Phế Phẩm/Hỏng] " + adminNote;
                }

                // CẬP NHẬT TRẠNG THÁI KHIẾU NẠI VÀ ĐƠN HÀNG GỐC
                returnReq.Status = "Hoàn tiền thành công";
                returnReq.ResolvedAt = DateTime.Now;
                returnReq.AdminNote = adminNote;

                order.Status = "Đã hoàn trả"; // CHUẨN XÁC TRẠNG THÁI VÀO BẢNG ORDER

                if (payment != null)
                {
                    payment.PaymentStatus = "Đã hoàn tiền";
                    _context.Payments.Update(payment);
                }

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = "Đã hoàn trả",
                    UpdatedAt = DateTime.Now,
                    Note = $"[Hoàn Tiền Thành Công] {adminNote}. Mã GD: {transactionRef}"
                });

                // ÉP EF CORE RÓT DỮ LIỆU
                _context.Orders.Update(order);
                _context.OrderReturns.Update(returnReq);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Gửi Email Biên lai
                if (returnReq.Customer != null && returnReq.Customer.Account != null && !string.IsNullOrEmpty(returnReq.Customer.Account.Email))
                {
                    string emailBody = $"<h3>Chào {returnReq.Customer.FullName},</h3><p>Cửa hàng đã <b>Hoàn Tiền Thành Công</b> cho đơn hàng <b>#ORD-{order.OrderId}</b> của bạn.</p><p>Số tiền <b>{string.Format("{0:N0}", order.TotalAmount)}đ</b> đã được chuyển khoản. Mã GD ngân hàng: <b>{transactionRef}</b>.</p><p>Trân trọng,<br/>Đội ngũ PHONE.ST</p>";
                    _ = SendEmailAsync(returnReq.Customer.Account.Email, $"[PHONE.ST] Biên lai Hoàn tiền đơn #ORD-{order.OrderId}", emailBody);
                }

                return Json(new { success = true, message = "Đã chốt hoàn tiền và cập nhật trạng thái đơn hàng thành công!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi xử lý hệ thống: " + ex.Message });
            }
        }
    }
}