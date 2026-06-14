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

        public async Task<IActionResult> Index()
        {
            var cart = await GetCartAsync();

            await RefreshCartMetadataAsync(cart);
            await SaveCartAsync(cart);

            decimal subtotal = 0;
            var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal);

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

            if (variant == null) return NotFound();

            var cart = await GetCartAsync();
            var existingItem = cart.FirstOrDefault(x => x.VariantId == variantId);

            int safeStock = variant.Stock ?? 0;
            decimal safePrice = variant.DiscountPrice > 0 ? variant.DiscountPrice.Value : (variant.Price ?? 0);

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
                if (existingItem.Quantity > safeStock) existingItem.Quantity = safeStock;
            }
            else
            {
                cart.Add(new CartItemVM
                {
                    VariantId = variant.VariantId,
                    ProductName = variant.Product?.Name ?? "Thiết bị di động",
                    Color = variant.Color ?? "",
                    Storage = variant.Storage ?? "",
                    ImageUrl = !string.IsNullOrEmpty(variant.ImageUrl) ? variant.ImageUrl : (variant.Product?.MainImage ?? ""),
                    Price = safePrice,
                    Quantity = Math.Min(quantity, safeStock),
                    Stock = safeStock
                });
            }

            await SaveCartAsync(cart);

            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers["Accept"].ToString().Contains("application/json");

            if (isAjax)
            {
                int totalCartItems = cart.Sum(x => x.Quantity);
                if (actionType == "buy")
                {
                    return Json(new { success = true, action = "redirect", url = "/Cart/Checkout" });
                }
                return Json(new { success = true, action = "update", cartCount = totalCartItems });
            }

            if (actionType == "buy") return RedirectToAction("Checkout", "Cart");
            string previousUrl = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrWhiteSpace(previousUrl)) return Redirect(previousUrl);
            return RedirectToAction("Index", "Store");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateQuantity(int variantId, int quantity)
        {
            if (quantity < 1) return Json(new { success = false, message = "Số lượng tối thiểu là 1" });

            var variant = await _context.ProductVariants.FindAsync(variantId);
            if (variant == null) return Json(new { success = false, message = "Biến thể không tồn tại" });

            var cart = await GetCartAsync();
            var item = cart.FirstOrDefault(x => x.VariantId == variantId);

            int safeStock = variant.Stock ?? 0;

            if (item != null)
            {
                bool isLimitReached = quantity > safeStock;
                item.Quantity = Math.Min(quantity, safeStock);

                await SaveCartAsync(cart);

                return Json(new
                {
                    success = true,
                    actualQuantity = item.Quantity,
                    limitReached = isLimitReached,
                    message = isLimitReached ? $"Chỉ còn {safeStock} sản phẩm trong kho." : "",
                    itemTotalPrice = item.TotalPrice.ToString("N0") + " đ"
                });
            }

            return Json(new { success = false, message = "Sản phẩm không có trong giỏ" });
        }

        [HttpPost]
        public async Task<IActionResult> GetCartSummary([FromBody] List<int> selectedVariantIds)
        {
            var cart = await GetCartAsync();
            var selectedItems = cart.Where(x => selectedVariantIds.Contains(x.VariantId)).ToList();
            decimal subtotal = selectedItems.Sum(x => x.TotalPrice);
            var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal);

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

        [HttpGet]
        public IActionResult Checkout(string selectedItems)
        {
            ViewBag.SelectedItems = selectedItems;
            return Content($"Trang Checkout. Các sản phẩm bạn chọn mua có ID là: {selectedItems}");
        }

        // =======================================================
        // ENGINE LƯU TRỮ KÉP (COOKIE CHO GUEST - DATABASE CHO MEMBER)
        // =======================================================
        private async Task<List<CartItemVM>> GetCartAsync()
        {
            // NẾU LÀ THÀNH VIÊN: ĐỌC TỪ DATABASE
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
                            Price = c.Variant.DiscountPrice > 0 ? c.Variant.DiscountPrice.Value : (c.Variant.Price ?? 0),
                            Quantity = c.Quantity ?? 1,
                            Stock = c.Variant.Stock ?? 0
                        }).ToListAsync();
                    return dbCart;
                }
            }

            // NẾU LÀ KHÁCH VÃNG LAI: ĐỌC TỪ COOKIE
            var json = HttpContext.Request.Cookies[CART_COOKIE_KEY];
            return json == null ? new List<CartItemVM>() : JsonSerializer.Deserialize<List<CartItemVM>>(json) ?? new List<CartItemVM>();
        }

        private async Task SaveCartAsync(List<CartItemVM> cart)
        {
            // NẾU LÀ THÀNH VIÊN: GHI ĐÈ VÀO DATABASE
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var customerIdStr = User.FindFirstValue("CustomerId");
                if (int.TryParse(customerIdStr, out int cusId))
                {
                    var existing = _context.CartItems.Where(c => c.CustomerId == cusId);
                    _context.CartItems.RemoveRange(existing); // Xóa cũ

                    var newItems = cart.Select(item => new CartItems
                    {
                        CustomerId = cusId,
                        VariantId = item.VariantId,
                        Quantity = item.Quantity,
                        CreatedDate = DateTime.Now
                    });
                    _context.CartItems.AddRange(newItems); // Lưu mới
                    await _context.SaveChangesAsync();
                    return;
                }
            }

            // NẾU LÀ KHÁCH VÃNG LAI: GHI VÀO COOKIE (Sống 30 ngày)
            var options = new CookieOptions
            {
                Expires = DateTime.Now.AddDays(30),
                HttpOnly = true,
                IsEssential = true
            };
            HttpContext.Response.Cookies.Append(CART_COOKIE_KEY, JsonSerializer.Serialize(cart), options);
        }

        private async Task RefreshCartMetadataAsync(List<CartItemVM> cart)
        {
            foreach (var item in cart)
            {
                var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                if (variant != null)
                {
                    item.Price = variant.DiscountPrice > 0 ? variant.DiscountPrice.Value : (variant.Price ?? 0);
                    item.Stock = variant.Stock ?? 0;
                    if (item.Quantity > item.Stock) item.Quantity = item.Stock;
                }
            }
        }
    }
}