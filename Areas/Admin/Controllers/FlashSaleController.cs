using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class FlashSaleController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FlashSaleController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive == true).ToListAsync();
            ViewBag.Brands = await _context.Brands.ToListAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateCampaign([FromBody] FlashSalePayload payload)
        {
            var validationMessage = await ValidateFlashSalePayloadAsync(payload);
            if (!string.IsNullOrEmpty(validationMessage))
            {
                return Json(new { success = false, message = validationMessage });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var fs = new FlashSales
                {
                    Name = payload.Name.Trim(),
                    StartTime = payload.StartTime,
                    EndTime = payload.EndTime,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                _context.FlashSales.Add(fs);
                await _context.SaveChangesAsync();

                var variants = await _context.ProductVariants
                    .Where(v => payload.Items.Select(i => i.VariantId).Contains(v.VariantId))
                    .ToDictionaryAsync(v => v.VariantId);

                var fsItems = new List<FlashSaleItems>();
                foreach (var item in payload.Items)
                {
                    var variant = variants[item.VariantId];
                    int stock = variant.Stock ?? 0;
                    int quantity = item.UseAllStock ? stock : item.Quantity;
                    int maxPerUser = item.IsUnlimitedPerUser ? 0 : item.MaxPerUser;

                    fsItems.Add(new FlashSaleItems
                    {
                        FlashSaleId = fs.FlashSaleId,
                        VariantId = item.VariantId,
                        FlashSalePrice = item.FlashSalePrice,
                        Quantity = quantity,
                        MaxPerUser = maxPerUser,
                        Sold = 0
                    });
                }

                _context.FlashSaleItems.AddRange(fsItems);
                await _context.SaveChangesAsync();

                AddFlashSaleLog(fs.FlashSaleId, null, "CREATE", "Campaign", null, "Khởi tạo chiến dịch Flash Sale", "Tạo mới chiến dịch");
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        public async Task<IActionResult> Index()
        {
            var flashSales = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();

            return View(flashSales);
        }

        public async Task<IActionResult> Details(int id)
        {
            var flashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                    .ThenInclude(i => i.Variant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(f => f.FlashSaleId == id);

            if (flashSale == null) return NotFound();

            decimal totalActualRevenue = 0;
            decimal totalActualProfit = 0;
            int totalQuantity = 0;
            int totalSold = 0;

            foreach (var item in flashSale.FlashSaleItems)
            {
                totalQuantity += item.Quantity;
                totalSold += item.Sold;

                decimal itemRev = item.Sold * item.FlashSalePrice;
                decimal itemProfit = item.Sold * (item.FlashSalePrice - (item.Variant?.CostPrice ?? 0));

                totalActualRevenue += itemRev;
                totalActualProfit += itemProfit;
            }

            double sellThroughRate = totalQuantity > 0 ? Math.Round((double)totalSold / totalQuantity * 100, 1) : 0;

            ViewBag.TotalRevenue = totalActualRevenue;
            ViewBag.TotalProfit = totalActualProfit;
            ViewBag.TotalQuantity = totalQuantity;
            ViewBag.TotalSold = totalSold;
            ViewBag.SellThroughRate = sellThroughRate;
            ViewBag.EditPolicy = BuildEditPolicy(flashSale);
            ViewBag.ChangeLogs = await _context.FlashSaleChangeLogs
                .Include(l => l.AdminAccount)
                .Where(l => l.FlashSaleId == id)
                .OrderByDescending(l => l.CreatedAt)
                .Take(100)
                .ToListAsync();

            return View(flashSale);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var flashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                    .ThenInclude(i => i.Variant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(f => f.FlashSaleId == id);

            if (flashSale == null) return NotFound();

            ViewBag.EditPolicy = BuildEditPolicy(flashSale);
            return View(flashSale);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateCampaign([FromBody] FlashSaleEditPayload payload)
        {
            if (payload == null || payload.FlashSaleId <= 0)
            {
                return Json(new { success = false, message = "Dữ liệu điều chỉnh không hợp lệ." });
            }

            var flashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                    .ThenInclude(i => i.Variant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(f => f.FlashSaleId == payload.FlashSaleId);

            if (flashSale == null) return Json(new { success = false, message = "Không tìm thấy chương trình Flash Sale." });

            var policy = BuildEditPolicy(flashSale);
            if (policy.IsEnded)
            {
                return Json(new { success = false, message = "Chương trình đã kết thúc và đã chốt dữ liệu bán hàng. Không thể thay đổi giá, số lượng hoặc thời gian để bảo toàn lịch sử giao dịch." });
            }

            var validationMessage = await ValidateFlashSaleEditPayloadAsync(payload, flashSale, policy);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                return Json(new { success = false, message = validationMessage });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                string reason = payload.Reason.Trim();
                DateTime now = DateTime.Now;
                DateTime nextStartTime = policy.HasStarted ? flashSale.StartTime : payload.StartTime;
                DateTime nextEndTime = payload.EndTime;

                AddLogIfChanged(flashSale.FlashSaleId, null, "UPDATE", "Name", flashSale.Name, payload.Name.Trim(), reason);
                if (!policy.HasStarted)
                {
                    AddLogIfChanged(flashSale.FlashSaleId, null, "UPDATE", "StartTime", flashSale.StartTime, nextStartTime, reason);
                }
                AddLogIfChanged(flashSale.FlashSaleId, null, "UPDATE", "EndTime", flashSale.EndTime, nextEndTime, reason);
                AddLogIfChanged(flashSale.FlashSaleId, null, payload.IsActive ? "ENABLE" : "DISABLE", "IsActive", flashSale.IsActive, payload.IsActive, reason);

                flashSale.Name = payload.Name.Trim();
                if (!policy.HasStarted)
                {
                    flashSale.StartTime = nextStartTime;
                }
                flashSale.EndTime = nextEndTime;
                flashSale.IsActive = payload.IsActive;

                var payloadItemsById = payload.Items.Where(i => i.ItemId > 0).ToDictionary(i => i.ItemId, i => i);
                var existingItems = flashSale.FlashSaleItems.ToList();

                foreach (var oldItem in existingItems)
                {
                    if (!payloadItemsById.ContainsKey(oldItem.ItemId))
                    {
                        if (policy.HasSold || oldItem.Sold > 0)
                        {
                            throw new InvalidOperationException("Chương trình đã phát sinh đơn nên không thể xóa sản phẩm khỏi danh sách bán.");
                        }

                        AddFlashSaleLog(flashSale.FlashSaleId, oldItem.ItemId, "REMOVE_ITEM", "Item", oldItem.VariantId.ToString(), "Đã xóa khỏi Flash Sale", reason);
                        _context.FlashSaleItems.Remove(oldItem);
                    }
                }

                foreach (var itemPayload in payload.Items)
                {
                    FlashSaleItems? existingItem = null;
                    if (itemPayload.ItemId > 0)
                    {
                        existingItem = existingItems.First(i => i.ItemId == itemPayload.ItemId);
                    }

                    var variant = await _context.ProductVariants
                        .Include(v => v.Product)
                        .FirstAsync(v => v.VariantId == itemPayload.VariantId);

                    int sold = existingItem?.Sold ?? 0;
                    int quantity = itemPayload.UseAllStock ? ((variant.Stock ?? 0) + sold) : itemPayload.Quantity;
                    int maxPerUser = itemPayload.IsUnlimitedPerUser ? 0 : itemPayload.MaxPerUser;

                    if (existingItem == null)
                    {
                        var newItem = new FlashSaleItems
                        {
                            FlashSaleId = flashSale.FlashSaleId,
                            VariantId = itemPayload.VariantId,
                            FlashSalePrice = itemPayload.FlashSalePrice,
                            Quantity = quantity,
                            MaxPerUser = maxPerUser,
                            Sold = 0
                        };

                        _context.FlashSaleItems.Add(newItem);
                        AddFlashSaleLog(flashSale.FlashSaleId, null, "ADD_ITEM", "Item", null, $"Variant #{itemPayload.VariantId}", reason);
                    }
                    else
                    {
                        AddLogIfChanged(flashSale.FlashSaleId, existingItem.ItemId, "UPDATE_ITEM", "VariantId", existingItem.VariantId, itemPayload.VariantId, reason);
                        AddLogIfChanged(flashSale.FlashSaleId, existingItem.ItemId, "UPDATE_ITEM", "FlashSalePrice", existingItem.FlashSalePrice, itemPayload.FlashSalePrice, reason);
                        AddLogIfChanged(flashSale.FlashSaleId, existingItem.ItemId, "UPDATE_ITEM", "Quantity", existingItem.Quantity, quantity, reason);
                        AddLogIfChanged(flashSale.FlashSaleId, existingItem.ItemId, "UPDATE_ITEM", "MaxPerUser", existingItem.MaxPerUser, maxPerUser, reason);

                        existingItem.VariantId = itemPayload.VariantId;
                        existingItem.FlashSalePrice = itemPayload.FlashSalePrice;
                        existingItem.Quantity = quantity;
                        existingItem.MaxPerUser = maxPerUser;
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, message = "Cập nhật chương trình thành công. Hệ thống đã lưu lịch sử điều chỉnh để phục vụ đối soát." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi cập nhật: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EndNow([FromBody] EndFlashSalePayload payload)
        {
            if (payload == null || payload.FlashSaleId <= 0)
            {
                return Json(new { success = false, message = "Dữ liệu dừng chương trình không hợp lệ." });
            }
            if (string.IsNullOrWhiteSpace(payload.Reason))
            {
                return Json(new { success = false, message = "Vui lòng nhập lý do dừng chương trình để phục vụ đối soát vận hành." });
            }

            var flashSale = await _context.FlashSales.FirstOrDefaultAsync(f => f.FlashSaleId == payload.FlashSaleId);
            if (flashSale == null) return Json(new { success = false, message = "Không tìm thấy chương trình Flash Sale." });

            if (DateTime.Now > flashSale.EndTime)
            {
                return Json(new { success = false, message = "Chương trình đã được chốt trước đó, không thể thao tác thêm." });
            }

            DateTime oldEnd = flashSale.EndTime;
            bool oldActive = flashSale.IsActive;
            flashSale.EndTime = DateTime.Now;
            flashSale.IsActive = false;

            AddFlashSaleLog(flashSale.FlashSaleId, null, "END_EARLY", "EndTime", oldEnd.ToString("dd/MM/yyyy HH:mm:ss"), flashSale.EndTime.ToString("dd/MM/yyyy HH:mm:ss"), payload.Reason.Trim());
            AddFlashSaleLog(flashSale.FlashSaleId, null, "DISABLE", "IsActive", oldActive.ToString(), "False", payload.Reason.Trim());

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Đã dừng chương trình và chốt thời gian bán." });
        }

        private async Task<string?> ValidateFlashSalePayloadAsync(FlashSalePayload payload)
        {
            if (payload == null) return "Dữ liệu chiến dịch không hợp lệ.";
            if (string.IsNullOrWhiteSpace(payload.Name)) return "Tên chương trình không được để trống.";
            if (payload.EndTime <= payload.StartTime) return "Thời gian kết thúc phải lớn hơn thời gian bắt đầu.";
            if (payload.Items == null || !payload.Items.Any()) return "Chưa có sản phẩm nào trong danh sách.";

            var duplicatedVariantId = payload.Items
                .GroupBy(i => i.VariantId)
                .FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicatedVariantId.HasValue)
            {
                return $"Biến thể #{duplicatedVariantId.Value} bị thêm trùng trong cùng một chiến dịch.";
            }

            var variantIds = payload.Items.Select(i => i.VariantId).ToList();
            var variants = await _context.ProductVariants
                .Include(v => v.Product)
                .Where(v => variantIds.Contains(v.VariantId))
                .ToDictionaryAsync(v => v.VariantId);

            foreach (var item in payload.Items)
            {
                if (!variants.TryGetValue(item.VariantId, out var variant))
                {
                    return $"Không tìm thấy biến thể sản phẩm #{item.VariantId}.";
                }

                string productName = variant.Product?.Name ?? $"Mã biến thể #{variant.VariantId}";
                decimal currentPrice = variant.DiscountPrice > 0 ? variant.DiscountPrice.Value : (variant.Price ?? 0);
                int stock = variant.Stock ?? 0;
                int quantity = item.UseAllStock ? stock : item.Quantity;
                int maxPerUser = item.IsUnlimitedPerUser ? 0 : item.MaxPerUser;

                if (variant.IsActive != true) return $"'{productName}' đang bị ẩn, không thể đưa vào Flash Sale.";
                if (stock <= 0) return $"'{productName}' đã hết tồn kho.";
                if (item.FlashSalePrice <= 0) return $"Giá Flash Sale của '{productName}' phải lớn hơn 0.";
                if (currentPrice <= 0) return $"'{productName}' chưa có giá bán hợp lệ.";
                if (item.FlashSalePrice >= currentPrice) return $"Giá Flash Sale của '{productName}' phải nhỏ hơn giá bán hiện tại.";
                if (variant.CostPrice.HasValue && item.FlashSalePrice < variant.CostPrice.Value)
                {
                    return $"Giá Flash Sale của '{productName}' đang thấp hơn giá vốn.";
                }

                if (quantity <= 0) return $"Số lượng Flash Sale của '{productName}' phải lớn hơn 0.";
                if (quantity > stock) return $"Số lượng Flash Sale của '{productName}' không được vượt tồn kho hiện tại ({stock}).";
                if (maxPerUser < 0) return $"Giới hạn mua của '{productName}' không được âm.";
                if (maxPerUser > 0 && maxPerUser > quantity) return $"Giới hạn mỗi khách của '{productName}' không được vượt tổng suất Flash Sale.";

                bool hasOverlap = await _context.FlashSaleItems
                    .Include(i => i.FlashSale)
                    .AnyAsync(i => i.VariantId == item.VariantId
                        && i.FlashSale != null
                        && i.FlashSale.IsActive
                        && i.FlashSale.StartTime < payload.EndTime
                        && i.FlashSale.EndTime > payload.StartTime);

                if (hasOverlap)
                {
                    return $"'{productName}' đã nằm trong một Flash Sale khác bị trùng thời gian.";
                }
            }

            return null;
        }

        private async Task<string?> ValidateFlashSaleEditPayloadAsync(FlashSaleEditPayload payload, FlashSales flashSale, FlashSaleEditPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(payload.Name)) return "Tên chương trình không được để trống.";
            if (string.IsNullOrWhiteSpace(payload.Reason)) return "Vui lòng nhập lý do điều chỉnh để phục vụ đối soát nội bộ.";
            if (payload.Items == null || !payload.Items.Any()) return "Chương trình phải có ít nhất một sản phẩm mở bán.";

            DateTime now = DateTime.Now;
            DateTime proposedStart = policy.HasStarted ? flashSale.StartTime : payload.StartTime;
            DateTime proposedEnd = payload.EndTime;

            if (policy.HasStarted && payload.StartTime != flashSale.StartTime)
            {
                return "Chương trình đã mở bán nên không thể thay đổi thời gian bắt đầu.";
            }
            if (proposedEnd <= proposedStart)
            {
                return "Thời gian kết thúc phải lớn hơn thời gian bắt đầu.";
            }
            if (payload.IsActive && proposedEnd <= now)
            {
                return "Chương trình đang mở bán phải có thời gian kết thúc lớn hơn hiện tại. Nếu muốn dừng bán, dùng nút Dừng chương trình.";
            }

            if (policy.HasSold)
            {
                var currentItemIds = flashSale.FlashSaleItems.Select(i => i.ItemId).OrderBy(i => i).ToList();
                var payloadItemIds = payload.Items.Where(i => i.ItemId > 0).Select(i => i.ItemId).OrderBy(i => i).ToList();
                if (!currentItemIds.SequenceEqual(payloadItemIds))
                {
                    return "Chương trình đã phát sinh đơn nên không thể thêm hoặc xóa sản phẩm. Chỉ được điều chỉnh các thông tin không ảnh hưởng lịch sử giao dịch.";
                }
            }

            var duplicatedVariantId = payload.Items
                .GroupBy(i => i.VariantId)
                .FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicatedVariantId.HasValue)
            {
                return $"Biến thể #{duplicatedVariantId.Value} bị trùng trong danh sách cập nhật.";
            }

            var existingItems = flashSale.FlashSaleItems.ToDictionary(i => i.ItemId);

            foreach (var item in payload.Items)
            {
                FlashSaleItems? existingItem = null;
                if (item.ItemId > 0 && !existingItems.TryGetValue(item.ItemId, out existingItem))
                {
                    return $"Không tìm thấy dòng Flash Sale item #{item.ItemId}.";
                }

                if (policy.HasSold && existingItem == null)
                {
                    return "Chương trình đã phát sinh đơn nên không thể thêm sản phẩm mới.";
                }

                var variant = await _context.ProductVariants
                    .Include(v => v.Product)
                    .FirstOrDefaultAsync(v => v.VariantId == item.VariantId);

                if (variant == null) return $"Không tìm thấy biến thể sản phẩm #{item.VariantId}.";

                string productName = variant.Product?.Name ?? $"Mã biến thể #{variant.VariantId}";
                decimal currentPrice = variant.DiscountPrice > 0 ? variant.DiscountPrice.Value : (variant.Price ?? 0);
                int sold = existingItem?.Sold ?? 0;
                int availableStockForAdditionalSale = variant.Stock ?? 0;
                int quantity = item.UseAllStock ? availableStockForAdditionalSale + sold : item.Quantity;
                int maxPerUser = item.IsUnlimitedPerUser ? 0 : item.MaxPerUser;

                if (variant.IsActive != true) return $"'{productName}' đang bị ẩn, không thể đưa vào Flash Sale.";
                if (item.FlashSalePrice <= 0) return $"Giá Flash Sale của '{productName}' phải lớn hơn 0.";
                if (currentPrice <= 0) return $"'{productName}' chưa có giá bán hợp lệ.";

                if (existingItem != null && existingItem.Sold > 0)
                {
                    if (existingItem.VariantId != item.VariantId)
                    {
                        return $"'{productName}' đã có lượt bán nên không được đổi sang biến thể khác.";
                    }
                    if (existingItem.FlashSalePrice != item.FlashSalePrice)
                    {
                        return $"'{productName}' đã có lượt bán nên không được đổi giá Flash Sale.";
                    }
                }
                else
                {
                    if (item.FlashSalePrice >= currentPrice) return $"Giá Flash Sale của '{productName}' phải nhỏ hơn giá bán hiện tại.";
                    if (variant.CostPrice.HasValue && item.FlashSalePrice < variant.CostPrice.Value)
                    {
                        return $"Giá Flash Sale của '{productName}' đang thấp hơn giá vốn.";
                    }
                }

                if (quantity <= 0) return $"Số lượng Flash Sale của '{productName}' phải lớn hơn 0.";
                if (quantity < sold) return $"Số lượng Flash Sale của '{productName}' không được nhỏ hơn số đã bán ({sold}).";
                if ((quantity - sold) > availableStockForAdditionalSale)
                {
                    return $"Số suất Flash Sale còn lại của '{productName}' không được vượt tồn kho khả dụng hiện tại ({availableStockForAdditionalSale}).";
                }
                if (maxPerUser < 0) return $"Giới hạn mua của '{productName}' không được âm.";
                if (maxPerUser > 0 && maxPerUser > quantity) return $"Giới hạn mỗi khách của '{productName}' không được vượt tổng suất Flash Sale.";

                if (existingItem != null && existingItem.Sold > 0 && maxPerUser > 0)
                {
                    int maxBought = await GetMaxPurchasedPerCustomerAsync(existingItem.ItemId);
                    if (maxPerUser < maxBought)
                    {
                        return $"Giới hạn mỗi khách của '{productName}' không được nhỏ hơn số lượng khách đã mua cao nhất ({maxBought}).";
                    }
                }

                bool hasOverlap = await _context.FlashSaleItems
                    .Include(i => i.FlashSale)
                    .AnyAsync(i => i.VariantId == item.VariantId
                        && i.FlashSaleId != flashSale.FlashSaleId
                        && i.FlashSale != null
                        && i.FlashSale.IsActive
                        && i.FlashSale.StartTime < proposedEnd
                        && i.FlashSale.EndTime > proposedStart);

                if (hasOverlap)
                {
                    return $"'{productName}' đã nằm trong một Flash Sale khác bị trùng thời gian.";
                }
            }

            return null;
        }

        private FlashSaleEditPolicy BuildEditPolicy(FlashSales flashSale)
        {
            DateTime now = DateTime.Now;
            int totalSold = flashSale.FlashSaleItems?.Sum(i => i.Sold) ?? 0;
            bool hasStarted = now >= flashSale.StartTime;
            bool isRunning = flashSale.IsActive && now >= flashSale.StartTime && now <= flashSale.EndTime;
            bool isEnded = now > flashSale.EndTime;

            string mode = now < flashSale.StartTime
                ? "Sắp mở bán"
                : isRunning && totalSold == 0
                    ? "Đang mở bán - chưa phát sinh đơn"
                    : isRunning && totalSold > 0
                        ? "Đang mở bán - đã phát sinh đơn"
                        : !flashSale.IsActive && !isEnded
                            ? "Tạm dừng bán"
                            : "Đã chốt chương trình";

            return new FlashSaleEditPolicy
            {
                Mode = mode,
                TotalSold = totalSold,
                HasSold = totalSold > 0,
                HasStarted = hasStarted,
                IsRunning = isRunning,
                IsEnded = isEnded,
                CanEditCampaign = !isEnded,
                CanEditStartTime = !hasStarted && totalSold == 0,
                CanEditPrice = totalSold == 0,
                CanAddOrRemoveItems = totalSold == 0,
                CanEndEarly = !isEnded && flashSale.IsActive
            };
        }

        private async Task<int> GetMaxPurchasedPerCustomerAsync(int flashSaleItemId)
        {
            var cancelledStatuses = new[] { "Đã hủy", "Đã hoàn trả" };

            var result = await _context.OrderDetails
                .Include(d => d.Order)
                .Where(d => d.FlashSaleItemId == flashSaleItemId
                    && d.IsFlashSaleItem
                    && d.Order != null
                    && d.Order.CustomerId != null
                    && !cancelledStatuses.Contains(d.Order.Status ?? ""))
                .GroupBy(d => d.Order!.CustomerId)
                .Select(g => g.Sum(d => d.Quantity ?? 0))
                .DefaultIfEmpty(0)
                .MaxAsync();

            return result;
        }

        private int? GetCurrentAdminAccountId()
        {
            string? accountId = User.FindFirstValue("AccountId")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("AdminId");

            return int.TryParse(accountId, out int parsedId) ? parsedId : null;
        }

        private void AddLogIfChanged(int flashSaleId, int? itemId, string actionType, string fieldName, object? oldValue, object? newValue, string reason)
        {
            string oldText = FormatLogValue(oldValue);
            string newText = FormatLogValue(newValue);
            if (oldText == newText) return;

            AddFlashSaleLog(flashSaleId, itemId, actionType, fieldName, oldText, newText, reason);
        }

        private void AddFlashSaleLog(int flashSaleId, int? itemId, string actionType, string fieldName, string? oldValue, string? newValue, string? reason)
        {
            _context.FlashSaleChangeLogs.Add(new FlashSaleChangeLogs
            {
                FlashSaleId = flashSaleId,
                ItemId = itemId,
                AdminAccountId = GetCurrentAdminAccountId(),
                ActionType = actionType,
                FieldName = fieldName,
                OldValue = oldValue,
                NewValue = newValue,
                Reason = reason,
                CreatedAt = DateTime.Now
            });
        }

        private static string FormatLogValue(object? value)
        {
            if (value == null) return "";
            if (value is DateTime dt) return dt.ToString("dd/MM/yyyy HH:mm:ss");
            if (value is decimal dc) return dc.ToString("N0");
            return value.ToString() ?? "";
        }
    }

    public class FlashSalePayload
    {
        public string Name { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<FlashSaleItemPayload> Items { get; set; } = new List<FlashSaleItemPayload>();
    }

    public class FlashSaleItemPayload
    {
        public int VariantId { get; set; }
        public decimal FlashSalePrice { get; set; }
        public int Quantity { get; set; }

        // true: hệ thống lấy toàn bộ tồn kho hiện tại làm tổng suất Flash Sale.
        public bool UseAllStock { get; set; } = false;

        // true: khách mua tùy thích theo tồn/suất còn lại; backend quy về MaxPerUser = 0.
        public bool IsUnlimitedPerUser { get; set; } = false;

        public int MaxPerUser { get; set; }
    }

    public class FlashSaleEditPayload
    {
        public int FlashSaleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsActive { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<FlashSaleEditItemPayload> Items { get; set; } = new List<FlashSaleEditItemPayload>();
    }

    public class FlashSaleEditItemPayload : FlashSaleItemPayload
    {
        public int ItemId { get; set; }
    }

    public class EndFlashSalePayload
    {
        public int FlashSaleId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class FlashSaleEditPolicy
    {
        public string Mode { get; set; } = string.Empty;
        public int TotalSold { get; set; }
        public bool HasSold { get; set; }
        public bool HasStarted { get; set; }
        public bool IsRunning { get; set; }
        public bool IsEnded { get; set; }
        public bool CanEditCampaign { get; set; }
        public bool CanEditStartTime { get; set; }
        public bool CanEditPrice { get; set; }
        public bool CanAddOrRemoveItems { get; set; }
        public bool CanEndEarly { get; set; }
    }
}
