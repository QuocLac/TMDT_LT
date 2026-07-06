using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Storefront;
using TMDT_LT.Services;

namespace TMDT_LT.Controllers
{
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PromotionEngine _promotionEngine;
        private const string CART_COOKIE_KEY = "PhoneStCartCookie";

        public CartController(ApplicationDbContext context, PromotionEngine promotionEngine)
        {
            _context = context;
            _promotionEngine = promotionEngine;
        }

        private int? GetCurrentCustomerId()
        {
            if (User.Identity?.IsAuthenticated != true) return null;
            var raw = User.FindFirst("CustomerId")?.Value;
            return int.TryParse(raw, out int customerId) && customerId > 0 ? customerId : null;
        }

        public async Task<IActionResult> Index()
        {
            var cart = await GetCartAsync();

            await RefreshCartMetadataAsync(cart);
            await SaveCartAsync(cart);

            decimal subtotal = cart.Sum(x => x.TotalPrice);
            var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal, GetCurrentCustomerId());

            var activeFlashSale = await _context.FlashSales
                .Where(f => f.IsActive && f.StartTime <= DateTime.Now && f.EndTime >= DateTime.Now)
                .OrderByDescending(f => f.StartTime)
                .FirstOrDefaultAsync();

            if (activeFlashSale != null)
            {
                ViewBag.FlashSaleEndTime = activeFlashSale.EndTime.ToString("yyyy-MM-ddTHH:mm:ss");
                ViewBag.FlashSaleStartTime = activeFlashSale.StartTime.ToString("yyyy-MM-ddTHH:mm:ss");
            }
            else
            {
                ViewBag.FlashSaleEndTime = "";
                ViewBag.FlashSaleStartTime = "";
            }

            ViewBag.VoucherStates = voucherStates;
            ViewBag.Subtotal = subtotal;

            return View(cart);
        }

        [HttpPost]
        public async Task<IActionResult> AddToCart(int variantId, int quantity = 1, string? actionType = "add")
        {
            var variant = await _context.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.VariantId == variantId && v.IsActive == true);

            if (variant == null) return Json(new { success = false, message = "Sản phẩm không tồn tại." });

            var cart = await GetCartAsync();
            var existingItem = cart.FirstOrDefault(x => x.VariantId == variantId);

            int currentQtyInCart = existingItem != null ? existingItem.Quantity : 0;
            int attemptedQty = currentQtyInCart + quantity;

            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers["Accept"].ToString().Contains("application/json");

            // LOGIC MỚI: CHỈ CHẶN NẾU VƯỢT QUÁ TỒN KHO VẬT LÝ TUYỆT ĐỐI
            if (attemptedQty > (variant.Stock ?? 0))
            {
                string msg = $"Tồn kho vật lý chỉ còn {variant.Stock} sản phẩm.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction("Index", "Cart");
            }

            if (existingItem != null)
            {
                existingItem.Quantity = attemptedQty;
            }
            else
            {
                cart.Add(new CartItemVM
                {
                    VariantId = variant.VariantId,
                    ProductName = variant.Product?.Name ?? "Thiết bị",
                    Color = variant.Color ?? "",
                    Storage = variant.Storage ?? "",
                    ImageUrl = !string.IsNullOrEmpty(variant.ImageUrl) ? variant.ImageUrl : (variant.Product?.MainImage ?? ""),
                    Quantity = attemptedQty,
                    Stock = variant.Stock ?? 0
                });
            }

            // Đẩy qua Engine để "Chẻ" số lượng (Bao nhiêu cái Sale, bao nhiêu cái Thường)
            await RefreshCartMetadataAsync(cart);
            await SaveCartAsync(cart);

            if (isAjax)
            {
                int totalCartItems = cart.Sum(x => x.Quantity);
                if (actionType == "buy") return Json(new { success = true, action = "redirect", url = "/Checkout/Index" });
                return Json(new { success = true, message = "Đã thêm sản phẩm vào giỏ hàng!", cartCount = totalCartItems });
            }

            if (actionType == "buy") return RedirectToAction("Index", "Checkout");

            string previousUrl = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrWhiteSpace(previousUrl)) return Redirect(previousUrl);

            return RedirectToAction("Index", "Store");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateQuantity(int variantId, int quantity)
        {
            if (quantity < 1) return Json(new { success = false, message = "Số lượng tối thiểu là 1" });

            var cart = await GetCartAsync();
            var item = cart.FirstOrDefault(x => x.VariantId == variantId);

            if (item != null)
            {
                item.Quantity = quantity;
                await RefreshCartMetadataAsync(cart);

                // Sau khi Refresh, nếu lượng bị ép xuống nghĩa là lố Tồn kho vật lý
                bool isLimitReached = item.Quantity < quantity;
                string msg = isLimitReached ? $"Tồn kho vật lý chỉ còn {item.Quantity} sản phẩm." : "";

                await SaveCartAsync(cart);

                return Json(new
                {
                    success = true,
                    actualQuantity = item.Quantity,
                    limitReached = isLimitReached,
                    message = msg,
                    itemTotalPrice = item.TotalPrice.ToString("N0") + " đ"
                });
            }

            return Json(new { success = false, message = "Sản phẩm không có trong giỏ" });
        }

        [HttpPost]
        public async Task<IActionResult> GetCartSummary([FromBody] List<int> selectedVariantIds)
        {
            var cart = await GetCartAsync();
            await RefreshCartMetadataAsync(cart);

            var selectedItems = cart.Where(x => selectedVariantIds.Contains(x.VariantId)).ToList();
            decimal subtotal = selectedItems.Sum(x => x.TotalPrice); // Dùng TotalPrice thông minh mới
            var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal, GetCurrentCustomerId());

            return Json(new
            {
                success = true,
                rawSubtotal = subtotal,
                subtotal = subtotal.ToString("N0") + " đ",
                vouchers = voucherStates
            });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveItem(int variantId)
        {
            var cart = await GetCartAsync();
            var item = cart.FirstOrDefault(x => x.VariantId == variantId);

            if (item != null)
            {
                cart.Remove(item);
                await SaveCartAsync(cart);
            }

            return RedirectToAction(nameof(Index));
        }

        // =======================================================
        // ENGINE LỌC DỮ LIỆU & BẢO VỆ GIÁ (CORE SECURITY - ĐÃ NÂNG CẤP TÁCH GIÁ)
        // =======================================================
        private async Task RefreshCartMetadataAsync(List<CartItemVM> cart)
        {
            var now = DateTime.Now;
            var activeFlashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                .Where(f => f.IsActive && f.StartTime <= now && f.EndTime >= now)
                .OrderByDescending(f => f.StartTime)
                .FirstOrDefaultAsync();

            foreach (var item in cart)
            {
                var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                if (variant != null)
                {
                    // 1. Chốt tồn kho vật lý
                    item.Stock = variant.Stock ?? 0;
                    if (item.Quantity > item.Stock) item.Quantity = item.Stock;

                    // 2. Thiết lập Giá gốc (Normal Price) làm mặc định
                    item.RegularPrice = variant.DiscountPrice > 0 ? variant.DiscountPrice.Value : (variant.Price ?? 0);
                    item.RegularQty = item.Quantity; // Mặc định ban đầu toàn bộ là giá thường

                    item.IsFlashSale = false;
                    item.FlashSalePrice = 0;
                    item.FlashSaleQty = 0;
                    item.MaxPerUser = 0;

                    // 3. THUẬT TOÁN TÁCH FLASH SALE
                    if (activeFlashSale != null)
                    {
                        var fsItem = activeFlashSale.FlashSaleItems.FirstOrDefault(i => i.VariantId == item.VariantId);

                        // Nếu đang có Sale và chưa bán hết suất
                        if (fsItem != null && fsItem.Sold < fsItem.Quantity)
                        {
                            item.IsFlashSale = true;
                            item.FlashSalePrice = fsItem.FlashSalePrice;
                            item.MaxPerUser = fsItem.MaxPerUser;

                            int availableSaleStock = fsItem.Quantity - fsItem.Sold;

                            // Xác định số lượng được hưởng giá Sale
                            int eligibleSaleQty = item.Quantity;
                            if (eligibleSaleQty > availableSaleStock) eligibleSaleQty = availableSaleStock;
                            if (item.MaxPerUser > 0 && eligibleSaleQty > item.MaxPerUser) eligibleSaleQty = item.MaxPerUser;

                            item.FlashSaleQty = eligibleSaleQty;

                            // Số lượng dư ra sẽ bị tính về Giá Thường
                            item.RegularQty = item.Quantity - eligibleSaleQty;
                        }
                    }
                }
            }
        }

        // =======================================================
        // ENGINE LƯU TRỮ KÉP (COOKIE CHO GUEST - DATABASE CHO MEMBER)
        // =======================================================
        private async Task<List<CartItemVM>> GetCartAsync()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var customerIdStr = User.FindFirstValue("CustomerId");
                if (int.TryParse(customerIdStr, out int cusId))
                {
                    var dbCart = await _context.CartItems
                        .Include(c => c.Variant).ThenInclude(v => v.Product)
                        .Where(c => c.CustomerId == cusId)
                        .Select(c => new CartItemVM
                        {
                            VariantId = c.VariantId ?? 0,
                            ProductName = c.Variant.Product.Name,
                            Color = c.Variant.Color ?? "",
                            Storage = c.Variant.Storage ?? "",
                            ImageUrl = !string.IsNullOrEmpty(c.Variant.ImageUrl) ? c.Variant.ImageUrl : c.Variant.Product.MainImage,
                            Quantity = c.Quantity ?? 1,
                            Stock = c.Variant.Stock ?? 0
                        }).ToListAsync();
                    return dbCart;
                }
            }

            var json = HttpContext.Request.Cookies[CART_COOKIE_KEY];
            return json == null ? new List<CartItemVM>() : JsonSerializer.Deserialize<List<CartItemVM>>(json) ?? new List<CartItemVM>();
        }

        private async Task SaveCartAsync(List<CartItemVM> cart)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var customerIdStr = User.FindFirstValue("CustomerId");
                if (int.TryParse(customerIdStr, out int cusId))
                {
                    var existing = _context.CartItems.Where(c => c.CustomerId == cusId);
                    _context.CartItems.RemoveRange(existing);

                    var newItems = cart.Select(item => new CartItems
                    {
                        CustomerId = cusId,
                        VariantId = item.VariantId,
                        Quantity = item.Quantity, // Lưu tổng số lượng khách muốn mua
                        CreatedDate = DateTime.Now
                    });
                    _context.CartItems.AddRange(newItems);
                    await _context.SaveChangesAsync();
                    return;
                }
            }

            var options = new CookieOptions
            {
                Expires = DateTime.Now.AddDays(30),
                HttpOnly = true,
                IsEssential = true
            };
            HttpContext.Response.Cookies.Append(CART_COOKIE_KEY, JsonSerializer.Serialize(cart), options);
        }
    }
}