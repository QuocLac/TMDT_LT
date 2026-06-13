using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string CART_SESSION_KEY = "PhoneStCartSession";

        public CartController(ApplicationDbContext context, PromotionEngine promotionEngine)
        {
            _context = context;
            _promotionEngine = promotionEngine;
        }

        // =======================================================
        // 1. MÀN HÌNH GIỎ HÀNG: ĐỐI SOÁT GIÁ & THỰC THI RULE ENGINE
        // =======================================================
        public async Task<IActionResult> Index()
        {
            var cart = GetCartFromSession();

            // Lớp bảo vệ kế toán: Đồng bộ lại Giá và Tồn kho từ CSDL
            await RefreshCartMetadataAsync(cart);
            SaveCartToSession(cart);

            decimal subtotal = cart.Sum(x => x.TotalPrice);

            // Triển khai động cơ rà quét điều kiện mã giảm giá
            var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal);

            // Bơm nguyên liệu ra giao diện xử lý thanh tiến trình Progress Bar
            ViewBag.VoucherStates = voucherStates;
            ViewBag.Subtotal = subtotal;

            return View(cart);
        }

        // =======================================================
        // 2. XỬ LÝ THÊM VÀO GIỎ & ĐIỀU HƯỚNG LUỒNG MUA NGAY
        // =======================================================
        [HttpPost]
        public async Task<IActionResult> AddToCart(int variantId, int quantity = 1, string? actionType = "add")
        {
            var variant = await _context.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.VariantId == variantId && v.IsActive == true);

            if (variant == null) return NotFound();

            var cart = GetCartFromSession();
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

            SaveCartToSession(cart);

            // -----------------------------------------------------------
            // XỬ LÝ AJAX (CHỐNG TẢI LẠI TRANG) GIỐNG SHOPEE
            // -----------------------------------------------------------
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          Request.Headers["Accept"].ToString().Contains("application/json");

            if (isAjax)
            {
                int totalCartItems = cart.Sum(x => x.Quantity);

                // Kịch bản Mua ngay -> Báo trình duyệt chuyển hướng qua Checkout
                if (actionType == "buy")
                {
                    return Json(new { success = true, action = "redirect", url = "/Cart/Checkout" });
                }

                // Kịch bản Thêm vào giỏ -> Trả về con số để nhảy Icon trên Header
                return Json(new { success = true, action = "update", cartCount = totalCartItems });
            }

            // Fallback (Trường hợp trình duyệt không chạy JS)
            if (actionType == "buy") return RedirectToAction("Checkout", "Cart");
            string previousUrl = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrWhiteSpace(previousUrl)) return Redirect(previousUrl);
            return RedirectToAction("Index", "Store");
        }

        // =======================================================
        // 3. API CẬP NHẬT SỐ LƯỢNG TẠI CHỖ
        // =======================================================
        [HttpPost]
        public async Task<IActionResult> UpdateQuantity(int variantId, int quantity)
        {
            if (quantity < 1) return Json(new { success = false, message = "Số lượng tối thiểu là 1" });

            var variant = await _context.ProductVariants.FindAsync(variantId);
            if (variant == null) return Json(new { success = false, message = "Biến thể không tồn tại" });

            var cart = GetCartFromSession();
            var item = cart.FirstOrDefault(x => x.VariantId == variantId);

            int safeStock = variant.Stock ?? 0;

            if (item != null)
            {
                item.Quantity = Math.Min(quantity, safeStock);
                SaveCartToSession(cart);

                decimal newSubtotal = cart.Sum(x => x.TotalPrice);
                var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(newSubtotal);

                return Json(new
                {
                    success = true,
                    itemTotalPrice = item.TotalPrice.ToString("N0") + " đ",
                    subtotal = newSubtotal.ToString("N0") + " đ",
                    vouchers = voucherStates
                });
            }

            return Json(new { success = false, message = "Sản phẩm không có trong giỏ" });
        }

        // =======================================================
        // 4. XOÁ VẬT PHẨM KHỎI PHÂN HỆ GIỎ HÀNG
        // =======================================================
        [HttpPost]
        public IActionResult RemoveItem(int variantId)
        {
            var cart = GetCartFromSession();
            var item = cart.FirstOrDefault(x => x.VariantId == variantId);

            if (item != null)
            {
                cart.Remove(item);
                SaveCartToSession(cart);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Checkout()
        {
            return Content("Trang Checkout chi tiết sẽ được phát triển tiếp theo tại đây.");
        }

        // =======================================================
        // CÁC PHƯƠNG THỨC TRỢ NĂNG SESSION
        // =======================================================
        private List<CartItemVM> GetCartFromSession()
        {
            var json = HttpContext.Session.GetString(CART_SESSION_KEY);
            return json == null ? new List<CartItemVM>() : JsonSerializer.Deserialize<List<CartItemVM>>(json) ?? new List<CartItemVM>();
        }

        private void SaveCartToSession(List<CartItemVM> cart)
        {
            HttpContext.Session.SetString(CART_SESSION_KEY, JsonSerializer.Serialize(cart));
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