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
using TMDT_LT.Services;

namespace TMDT_LT.Controllers
{
    [Authorize]
    public class CustomerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IOrderInventoryService _orderInventoryService;
        private readonly IOrderStateService _orderStateService;
        private readonly VnPayService _vnPayService;

        public CustomerController(
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment,
            IOrderInventoryService orderInventoryService,
            IOrderStateService orderStateService,
            VnPayService vnPayService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _orderInventoryService = orderInventoryService;
            _orderStateService = orderStateService;
            _vnPayService = vnPayService;
        }

        [HttpGet]
        public async Task<IActionResult> Orders(string status = "TatCa")
        {
            int customerId = GetCurrentCustomerId();

            var query = _context.Orders
                .Include(o => o.Payments)
                .Include(o => o.OrderHistories)
                .Include(o => o.OrderDetails).ThenInclude(od => od.Variant).ThenInclude(v => v.Product)
                .Where(o => o.CustomerId == customerId);

            if (status == "Trả hàng/Hoàn tiền")
            {
                var returnStatuses = new[]
                {
                    OrderStatuses.ReturnPending,
                    OrderStatuses.ReturnAwaitingCustomer,
                    OrderStatuses.ReturnInspecting,
                    OrderStatuses.Returned,
                    "Trả hàng/Hoàn tiền"
                };
                query = query.Where(o => returnStatuses.Contains(o.Status));
            }
            else if (status != "TatCa")
            {
                query = query.Where(o => o.Status == status);
            }

            ViewBag.CurrentStatus = status;
            ViewBag.CurrentTime = DateTime.Now;
            return View(await query.OrderByDescending(o => o.OrderDate).ToListAsync());
        }

        [HttpGet]
        public async Task<IActionResult> OrderDetail(int id)
        {
            int customerId = GetCurrentCustomerId();

            var order = await _context.Orders
                .Include(o => o.Payments)
                .Include(o => o.OrderHistories)
                .Include(o => o.OrderDetails).ThenInclude(od => od.Variant).ThenInclude(v => v.Product)
                .Include(o => o.OrderReturns)
                .FirstOrDefaultAsync(o => o.OrderId == id && o.CustomerId == customerId);

            return order == null ? RedirectToAction("Orders") : View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestCancelOrder(int orderId, string reason)
        {
            int customerId = GetCurrentCustomerId();
            reason = reason?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(reason))
            {
                return Json(new { success = false, message = "Vui lòng nhập lý do hủy đơn." });
            }

            using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderDetails).ThenInclude(d => d.Variant)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

                if (order == null)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Đơn hàng không tồn tại." });
                }

                if (order.Status == OrderStatuses.Cancelled)
                {
                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "Đơn hàng đã được hủy trước đó." });
                }

                if (order.Status != OrderStatuses.Pending)
                {
                    await transaction.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "Đơn hàng đã được xử lý, không thể tự hủy lúc này."
                    });
                }

                bool restored = await _orderInventoryService.RestoreOrderStockAsync(
                    order.OrderId,
                    "Hoàn kho do khách hàng hủy đơn",
                    restoreFlashSaleSlots: true,
                    occurredAt: DateTime.Now,
                    cancellationToken: HttpContext.RequestAborted);

                order.CancellationReason = reason;
                order.CancellationRequestedBy = "Customer";

                var payment = order.Payments.FirstOrDefault();
                if (payment != null && payment.PaymentStatus == PaymentStatuses.Paid)
                {
                    if (string.Equals(payment.PaymentMethod, PaymentMethods.VnPay, StringComparison.OrdinalIgnoreCase))
                    {
                        string transactionDate = payment.PaymentDate?.ToString("yyyyMMddHHmmss")
                            ?? DateTime.Now.ToString("yyyyMMddHHmmss");

                        bool refunded = await _vnPayService.RequestBankRefundAsync(
                            order.OrderId,
                            order.TotalAmount ?? 0,
                            transactionDate,
                            User.Identity?.Name ?? "customer");

                        if (!refunded)
                        {
                            throw new InvalidOperationException(
                                "Không thể hoàn tiền VNPAY nên đơn chưa được hủy. Vui lòng liên hệ hỗ trợ.");
                        }

                        payment.PaymentStatus = PaymentStatuses.Refunded;
                    }
                    else
                    {
                        payment.PaymentStatus = PaymentStatuses.AwaitingRefund;
                    }
                }
                else if (payment != null)
                {
                    payment.PaymentStatus = PaymentStatuses.Cancelled;
                }

                string note = $"[Khách hàng hủy] Lý do: {reason}."
                    + (restored
                        ? " Đã hoàn kho và hoàn suất Flash Sale nếu có."
                        : " Tồn kho đã được hoàn trước đó hoặc đơn chưa từng trừ kho.");

                _orderStateService.Transition(
                    order,
                    OrderStatuses.Cancelled,
                    note,
                    DateTime.Now);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Json(new { success = true, message = "Hủy đơn hàng thành công." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Không thể hủy đơn: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitOrderReturn(
            int orderId,
            string reason,
            string description,
            string bankCode,
            string accountNumber,
            string accountName,
            List<IFormFile> files)
        {
            int customerId = GetCurrentCustomerId();
            var savedPhysicalFiles = new List<string>();

            using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderHistories)
                    .Include(o => o.Payments)
                    .Include(o => o.OrderReturns)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

                if (order == null || order.Status != OrderStatuses.Delivered)
                {
                    await transaction.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "Chỉ có thể khiếu nại đối với đơn hàng đang ở trạng thái 'Đã giao'."
                    });
                }

                bool hasActiveRequest = order.OrderReturns.Any(r =>
                    r.Status == ReturnStatuses.Pending
                    || r.Status == ReturnStatuses.AwaitingCustomer
                    || r.Status == ReturnStatuses.Inspecting);

                if (hasActiveRequest)
                {
                    await transaction.CommitAsync();
                    return Json(new
                    {
                        success = true,
                        message = "Đơn hàng đã có một hồ sơ đổi trả đang được xử lý."
                    });
                }

                var deliveredLog = order.OrderHistories
                    .OrderByDescending(h => h.UpdatedAt)
                    .FirstOrDefault(h => h.Status == OrderStatuses.Delivered);

                if (deliveredLog == null || (DateTime.Now - deliveredLog.UpdatedAt).TotalDays > 7)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Đã quá thời hạn 7 ngày đổi trả." });
                }

                string mediaPaths = await SaveReturnFilesAsync(files, savedPhysicalFiles);

                var returnRecord = new OrderReturns
                {
                    OrderId = order.OrderId,
                    CustomerId = customerId,
                    Reason = reason?.Trim() ?? string.Empty,
                    Description = description?.Trim(),
                    MediaUrls = mediaPaths,
                    Status = ReturnStatuses.Pending,
                    CreatedAt = DateTime.Now,
                    RefundBankCode = bankCode?.Trim(),
                    RefundAccountNumber = accountNumber?.Trim(),
                    RefundAccountName = accountName?.Trim()
                };

                var payment = order.Payments.FirstOrDefault();
                if (payment != null && payment.PaymentStatus == PaymentStatuses.Paid)
                {
                    payment.PaymentStatus = PaymentStatuses.AwaitingRefund;
                }

                _context.OrderReturns.Add(returnRecord);
                _orderStateService.Transition(
                    order,
                    OrderStatuses.ReturnPending,
                    $"Khách hàng mở yêu cầu trả hàng. Lý do: {reason?.Trim()}.",
                    DateTime.Now);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = "Đã gửi hồ sơ khiếu nại thành công. Vui lòng theo dõi tiến trình."
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                DeleteSavedFiles(savedPhysicalFiles);
                return Json(new { success = false, message = "Không thể gửi hồ sơ: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitProductReview(
            int orderId,
            int detailId,
            int productId,
            int rating,
            string descriptionMatch,
            string comment,
            List<IFormFile> reviewFiles)
        {
            int customerId = GetCurrentCustomerId();

            var detail = await _context.OrderDetails
                .Include(d => d.Order)
                .ThenInclude(o => o.OrderHistories)
                .FirstOrDefaultAsync(d => d.OrderDetailId == detailId
                    && d.OrderId == orderId
                    && d.Order != null
                    && d.Order.CustomerId == customerId);

            if (detail == null || detail.Order?.Status != OrderStatuses.Completed)
            {
                return Json(new { success = false, message = "Chỉ có thể đánh giá khi đơn hàng đã hoàn thành." });
            }

            if (detail.IsReviewed)
            {
                return Json(new { success = false, message = "Bạn đã gửi đánh giá rồi." });
            }

            string finalComment = $"Đúng với mô tả: {descriptionMatch}\nĐánh giá: {comment}";
            string mediaPaths = string.Empty;

            if (reviewFiles != null && reviewFiles.Count > 0)
            {
                string reviewFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "reviews");
                Directory.CreateDirectory(reviewFolder);
                var savedList = new List<string>();

                foreach (var file in reviewFiles.Where(file => file.Length > 0))
                {
                    string targetName = Guid.NewGuid() + "_" + Path.GetFileName(file.FileName);
                    await using var stream = new FileStream(
                        Path.Combine(reviewFolder, targetName),
                        FileMode.CreateNew);
                    await file.CopyToAsync(stream);
                    savedList.Add($"/uploads/reviews/{targetName}");
                }

                mediaPaths = string.Join(",", savedList);
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var reviewMaster = new Reviews
                {
                    ProductId = productId,
                    CustomerId = customerId,
                    OrderId = orderId,
                    Rating = rating,
                    Comment = finalComment,
                    CreatedAt = DateTime.Now,
                    IsHidden = false,
                    IsRead = false
                };

                _context.Reviews.Add(reviewMaster);
                await _context.SaveChangesAsync();

                _context.ReviewDetails.Add(new ReviewDetails
                {
                    ReviewId = reviewMaster.ReviewId,
                    VariantId = detail.VariantId,
                    Rating = rating,
                    Comment = finalComment,
                    MediaUrls = mediaPaths,
                    IsRead = false,
                    IsHidden = false,
                    CreatedAt = DateTime.Now
                });

                detail.IsReviewed = true;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Json(new { success = true, message = "Cảm ơn bạn đã đánh giá!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmOrderCompleted(int orderId)
        {
            int customerId = GetCurrentCustomerId();

            using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            try
            {
                var order = await _context.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

                if (order == null)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng." });
                }

                if (order.Status == OrderStatuses.Completed)
                {
                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "Đơn hàng đã hoàn thành trước đó." });
                }

                if (order.Status != OrderStatuses.Delivered)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Đơn hàng chưa ở trạng thái đã giao." });
                }

                var payment = order.Payments.FirstOrDefault();
                if (payment != null)
                {
                    if (string.Equals(payment.PaymentMethod, PaymentMethods.Cod, StringComparison.OrdinalIgnoreCase))
                    {
                        payment.PaymentStatus = PaymentStatuses.Paid;
                        payment.PaymentDate = DateTime.Now;
                    }
                    else if (payment.PaymentStatus != PaymentStatuses.Paid)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { success = false, message = "Đơn hàng chưa được xác nhận thanh toán." });
                    }
                }

                int pointsEarned = (int)((order.TotalAmount ?? 0) / 100000);
                if (pointsEarned > 0 && order.Customer != null)
                {
                    order.Customer.RewardPoints += pointsEarned;
                    int points = order.Customer.RewardPoints;
                    order.Customer.CustomerType = points >= 600
                        ? "Kim Cương"
                        : points >= 300
                            ? "Vàng"
                            : points >= 100
                                ? "Bạc"
                                : "Newbie";
                }

                string note = "Khách hàng xác nhận đã nhận hàng."
                    + (pointsEarned > 0
                        ? $" [Hệ thống: +{pointsEarned} điểm. Hạng: {order.Customer?.CustomerType}]."
                        : string.Empty);

                _orderStateService.Transition(
                    order,
                    OrderStatuses.Completed,
                    note,
                    DateTime.Now);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Không thể hoàn thành đơn: " + ex.Message });
            }
        }

        private int GetCurrentCustomerId()
        {
            string rawId = User.FindFirst("CustomerId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "0";

            return int.TryParse(rawId, out int customerId) ? customerId : 0;
        }

        private async Task<string> SaveReturnFilesAsync(
            List<IFormFile>? files,
            List<string> savedPhysicalFiles)
        {
            if (files == null || files.Count == 0)
            {
                return string.Empty;
            }

            string returnFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "returns");
            Directory.CreateDirectory(returnFolder);
            var publicPaths = new List<string>();

            foreach (var file in files.Where(file => file.Length > 0))
            {
                string targetName = Guid.NewGuid() + "_" + Path.GetFileName(file.FileName);
                string physicalPath = Path.Combine(returnFolder, targetName);

                await using var stream = new FileStream(physicalPath, FileMode.CreateNew);
                await file.CopyToAsync(stream);

                savedPhysicalFiles.Add(physicalPath);
                publicPaths.Add($"/uploads/returns/{targetName}");
            }

            return string.Join(",", publicPaths);
        }

        private static void DeleteSavedFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }
                catch
                {
                    // Không che lỗi nghiệp vụ chính nếu dọn file thất bại.
                }
            }
        }
    }
}
