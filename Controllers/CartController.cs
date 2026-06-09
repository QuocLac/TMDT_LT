using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;
using TMDT_LT.Models.ViewModels;

namespace TMDT_LT.Controllers
{
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const string CartSessionKey = "PhoneStore_Cart";

        public CartController(ApplicationDbContext context)
        {
            _context = context;
        }

        private List<CartItemSession> GetCartItems()
        {
            return HttpContext.Session.Get<List<CartItemSession>>(CartSessionKey) ?? new List<CartItemSession>();
        }

        // 1. Render giao diện chính của Giỏ hàng
        public IActionResult Index()
        {
            var cart = GetCartItems();
            return View(cart);
        }

        // 2. Thêm vào giỏ hàng (Từ trang Chi tiết)
        [HttpPost]
        public async Task<IActionResult> AddToCart(int variantId, int quantity)
        {
            var variant = await _context.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.VariantId == variantId && v.IsActive == true);

            if (variant == null || variant.Stock < quantity)
            {
                TempData["Error"] = "Sản phẩm không đủ số lượng tồn kho.";
                return Redirect(Request.Headers["Referer"].ToString());
            }

            var cart = GetCartItems();
            var existingItem = cart.FirstOrDefault(c => c.VariantId == variantId);

            if (existingItem != null)
            {
                if (existingItem.Quantity + quantity <= variant.Stock)
                    existingItem.Quantity += quantity;
                else
                    existingItem.Quantity = variant.Stock ?? 0;
            }
            else
            {
                cart.Add(new CartItemSession
                {
                    VariantId = variant.VariantId,
                    ProductId = variant.ProductId,
                    ProductName = variant.Product.Name,
                    ImageUrl = variant.ImageUrl ?? variant.Product.MainImage ?? "",
                    Color = variant.Color ?? "",
                    Storage = variant.Storage ?? "",
                    RAM = variant.Ram ?? "",
                    UnitPrice = variant.DiscountPrice ?? variant.Price ?? 0,
                    Quantity = quantity,
                    MaxStock = variant.Stock ?? 0
                });
            }

            HttpContext.Session.Set(CartSessionKey, cart);
            return RedirectToAction("Index");
        }

        // 3. API Bất đồng bộ: Cập nhật số lượng (Dùng cho nút + / -)
        [HttpPost]
        public IActionResult UpdateQuantityAjax([FromBody] CartUpdateRequest request)
        {
            var cart = GetCartItems();
            var item = cart.FirstOrDefault(c => c.VariantId == request.VariantId);

            if (item != null)
            {
                // Ràng buộc bảo mật: Không cho phép âm và không vượt quá tồn kho
                if (request.Quantity > 0 && request.Quantity <= item.MaxStock)
                {
                    item.Quantity = request.Quantity;
                    HttpContext.Session.Set(CartSessionKey, cart);

                    return Json(new
                    {
                        success = true,
                        itemTotal = item.TotalPrice,
                        cartTotal = cart.Sum(x => x.TotalPrice),
                        totalItems = cart.Sum(x => x.Quantity)
                    });
                }
                return Json(new { success = false, message = $"Số lượng tối đa có thể mua là {item.MaxStock}." });
            }
            return Json(new { success = false, message = "Không tìm thấy sản phẩm." });
        }

        // 4. API Bất đồng bộ: Xóa sản phẩm
        [HttpPost]
        public IActionResult RemoveAjax([FromBody] CartUpdateRequest request)
        {
            var cart = GetCartItems();
            var item = cart.FirstOrDefault(c => c.VariantId == request.VariantId);

            if (item != null)
            {
                cart.Remove(item);
                HttpContext.Session.Set(CartSessionKey, cart);
                return Json(new
                {
                    success = true,
                    cartTotal = cart.Sum(x => x.TotalPrice),
                    totalItems = cart.Sum(x => x.Quantity),
                    isEmpty = !cart.Any()
                });
            }
            return Json(new { success = false });
        }
    }

    // Class phụ trợ để bắt Request JSON từ Frontend
    public class CartUpdateRequest
    {
        public int VariantId { get; set; }
        public int Quantity { get; set; }
    }
}