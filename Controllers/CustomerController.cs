using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Controllers
{
    [Authorize]
    public class CustomerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public CustomerController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // =================================================================
        // 1. TRANG DANH SÁCH ĐƠN HÀNG
        // =================================================================
        [HttpGet]
        public async Task<IActionResult> Orders(string status = "TatCa")
        {
            string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
            int customerId = int.Parse(userIdStr);

            var query = _context.Orders
                .Include(o => o.Payments)
                .Include(o => o.OrderHistories)
                .Include(o => o.OrderDetails).ThenInclude(od => od.Variant).ThenInclude(v => v.Product)
                .Where(o => o.CustomerId == customerId);

            if (status == "Trả hàng/Hoàn tiền") 
            {
                var returnStatuses = new[] { "Chờ duyệt", "Chờ khách trả hàng", "Đang kiểm định", "Đã hoàn trả", "Trả hàng/Hoàn tiền" };
                query = query.Where(o => returnStatuses.Contains(o.Status));
            } 
            else if (status != "TatCa") 
            {
                query = query.Where(o => o.Status == status);
            }

            var orders = await query.OrderByDescending(o => o.OrderDate).ToListAsync();
            ViewBag.CurrentStatus = status; 
            ViewBag.CurrentTime = DateTime.Now;

            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> OrderDetail(int id)
        {
            string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
            int customerId = int.Parse(userIdStr);

            var order = await _context.Orders
                .Include(o => o.Payments)
                .Include(o => o.OrderHistories)
                .Include(o => o.OrderDetails).ThenInclude(od => od.Variant).ThenInclude(v => v.Product)
                .Include(o => o.OrderReturns)
                .FirstOrDefaultAsync(o => o.OrderId == id && o.CustomerId == customerId);

            if (order == null) return RedirectToAction("Orders");
            return View(order);
        }

        // =================================================================
        // 2. API: HỦY ĐƠN HÀNG (TRƯỚC KHI GIAO)
        // =================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestCancelOrder(int orderId, string reason)
        {
            string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
            int customerId = int.Parse(userIdStr);

            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

            if (order == null) return Json(new { success = false, message = "Đơn hàng không tồn tại." });
            if (order.Status != "Chờ xác nhận") return Json(new { success = false, message = "Đơn hàng đã được xử lý, không thể tự hủy lúc này." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                order.Status = "Đã hủy";
                order.CancellationReason = reason;
                order.CancellationRequestedBy = "Customer";

                foreach (var detail in order.OrderDetails) 
                    if (detail.Variant != null) detail.Variant.Stock = (detail.Variant.Stock ?? 0) + (detail.Quantity ?? 0);

                var payment = order.Payments.FirstOrDefault();
                if (payment != null) payment.PaymentStatus = payment.PaymentMethod == "VNPAY" || payment.PaymentMethod == "BankTransfer" ? "Chờ hoàn tiền" : "Đã hủy";

                _context.OrderHistories.Add(new OrderHistory { OrderId = order.OrderId, Status = "Đã hủy", UpdatedAt = DateTime.Now, Note = $"[Khách Hàng Hủy] Lý do: {reason}. Đã hoàn trả tồn kho." });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Json(new { success = true, message = "Hủy đơn hàng thành công." });
            }
            catch (Exception ex) { await transaction.RollbackAsync(); return Json(new { success = false, message = "Lỗi: " + ex.Message }); }
        }

        // =================================================================
        // 3. API: KHÁCH HÀNG YÊU CẦU HOÀN TRẢ
        // =================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitOrderReturn(int orderId, string reason, string description, string bankCode, string accountNumber, string accountName, List<IFormFile> files)
        {
            string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
            int customerId = int.Parse(userIdStr);

            var order = await _context.Orders
                .Include(o => o.OrderHistories)
                .Include(o => o.Payments) // Tải dữ liệu dòng tiền
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

            if (order == null || order.Status != "Đã giao") 
                return Json(new { success = false, message = "Chỉ có thể khiếu nại đối với đơn hàng đang ở trạng thái 'Đã giao'." });

            var deliveredLog = order.OrderHistories.OrderByDescending(h => h.UpdatedAt).FirstOrDefault(h => h.Status == "Đã giao");
            if (deliveredLog == null || (DateTime.Now - deliveredLog.UpdatedAt).TotalDays > 7) 
                return Json(new { success = false, message = "Đã quá thời hạn 7 ngày đổi trả." });

            string dbMediaPaths = "";
            if (files != null && files.Count > 0)
            {
                string returnFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "returns");
                if (!Directory.Exists(returnFolder)) Directory.CreateDirectory(returnFolder);

                var savedList = new List<string>();
                foreach (var file in files)
                {
                    if (file.Length > 0)
                    {
                        string targetName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(file.FileName);
                        using (var stream = new FileStream(Path.Combine(returnFolder, targetName), FileMode.Create)) await file.CopyToAsync(stream);
                        savedList.Add($"/uploads/returns/{targetName}");
                    }
                }
                dbMediaPaths = string.Join(",", savedList);
            }

            var returnRecord = new OrderReturns 
            { 
                OrderId = order.OrderId, CustomerId = customerId, Reason = reason, Description = description, 
                MediaUrls = dbMediaPaths, Status = "Chờ duyệt", CreatedAt = DateTime.Now, 
                RefundBankCode = bankCode, RefundAccountNumber = accountNumber, RefundAccountName = accountName 
            };
            
            // 1. ÉP RÓT TRẠNG THÁI ĐƠN GỐC LÀ "CHỜ DUYỆT"
            order.Status = "Chờ duyệt"; 

            // 2. CHUYỂN DÒNG TIỀN SANG "CHỜ HOÀN TIỀN" ĐỂ TRÁNH HIỂU LẦM
            var payment = order.Payments.FirstOrDefault();
            if (payment != null && payment.PaymentStatus == "Đã thanh toán")
            {
                payment.PaymentStatus = "Chờ hoàn tiền";
            }

            _context.OrderHistories.Add(new OrderHistory { OrderId = order.OrderId, Status = "Chờ duyệt", UpdatedAt = DateTime.Now, Note = $"Khách hàng mở khiếu nại. Lý do: {reason}." });
            _context.OrderReturns.Add(returnRecord);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Đã gửi hồ sơ khiếu nại thành công. Vui lòng theo dõi tiến trình trong chi tiết đơn hàng." });
        }

        // =================================================================
        // 4. API: ĐÁNH GIÁ SẢN PHẨM
        // =================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitProductReview(int orderId, int detailId, int productId, int rating, string descriptionMatch, string comment, List<IFormFile> reviewFiles)
        {
            string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
            int customerId = int.Parse(userIdStr);

            var detail = await _context.OrderDetails.Include(d => d.Order).ThenInclude(o => o.OrderHistories).FirstOrDefaultAsync(d => d.OrderDetailId == detailId && d.Order.CustomerId == customerId);
            if (detail == null || detail.Order.Status != "Hoàn thành") return Json(new { success = false, message = "Chỉ có thể đánh giá khi đơn hàng đã 'Hoàn thành'." });
            if (detail.IsReviewed) return Json(new { success = false, message = "Bạn đã gửi đánh giá rồi." });

            string finalComment = $"Đúng với mô tả: {descriptionMatch}\nĐánh giá: {comment}";
            string dbMediaPaths = "";

            if (reviewFiles != null && reviewFiles.Count > 0)
            {
                string reviewFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "reviews");
                if (!Directory.Exists(reviewFolder)) Directory.CreateDirectory(reviewFolder);
                var savedList = new List<string>();
                foreach (var file in reviewFiles)
                {
                    if (file.Length > 0)
                    {
                        string targetName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(file.FileName);
                        using (var stream = new FileStream(Path.Combine(reviewFolder, targetName), FileMode.Create)) await file.CopyToAsync(stream);
                        savedList.Add($"/uploads/reviews/{targetName}");
                    }
                }
                dbMediaPaths = string.Join(",", savedList);
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var reviewMaster = new Reviews { ProductId = productId, CustomerId = customerId, OrderId = orderId, Rating = rating, Comment = finalComment, CreatedAt = DateTime.Now, IsHidden = false, IsRead = false };
                _context.Reviews.Add(reviewMaster);
                await _context.SaveChangesAsync();

                var reviewDetail = new ReviewDetails { ReviewId = reviewMaster.ReviewId, VariantId = detail.VariantId, Rating = rating, Comment = finalComment, MediaUrls = dbMediaPaths, IsRead = false, IsHidden = false, CreatedAt = DateTime.Now };
                _context.ReviewDetails.Add(reviewDetail);
                detail.IsReviewed = true;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Json(new { success = true, message = "Cảm ơn bạn đã đánh giá!" });
            }
            catch (Exception ex) { await transaction.RollbackAsync(); return Json(new { success = false, message = "Lỗi: " + ex.Message }); }
        }

        // =================================================================
        // 5. API: KHÁCH HÀNG TỰ CHỐT ĐƠN & TÍCH ĐIỂM
        // =================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmOrderCompleted(int orderId)
        {
            var order = await _context.Orders.Include(o => o.Customer).Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderId == orderId);
            if (order != null && order.Status == "Đã giao")
            {
                order.Status = "Hoàn thành";
                
                // Đảm bảo đơn COD khi hoàn thành sẽ có trạng thái Đã thanh toán
                var payment = order.Payments.FirstOrDefault();
                if (payment != null && payment.PaymentStatus != "Đã thanh toán") {
                    payment.PaymentStatus = "Đã thanh toán";
                }

                // TÍCH ĐIỂM THƯỞNG KHI KHÁCH TỰ CHỐT ĐƠN
                int pointsEarned = (int)((order.TotalAmount ?? 0) / 100000);
                if (pointsEarned > 0 && order.Customer != null)
                {
                    order.Customer.RewardPoints += pointsEarned;
                    int cp = order.Customer.RewardPoints;
                    order.Customer.CustomerType = cp >= 600 ? "Kim Cương" : cp >= 300 ? "Vàng" : cp >= 100 ? "Bạc" : "Newbie";
                }

                string noteAdd = pointsEarned > 0 ? $" [Hệ thống: +{pointsEarned} điểm. Hạng: {order.Customer?.CustomerType}]." : "";

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = orderId,
                    Status = "Hoàn thành",
                    UpdatedAt = DateTime.Now,
                    Note = "Khách hàng xác nhận đã nhận hàng." + noteAdd
                });

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }
    }
}