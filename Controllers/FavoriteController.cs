using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims; // BẮT BUỘC PHẢI CÓ THƯ VIỆN NÀY ĐỂ ĐỌC CLAIMS
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Controllers
{
    public class FavoriteController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FavoriteController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Toggle(int variantId)
        {
            // 1. Kiểm tra người dùng đã đăng nhập chưa
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return Json(new { success = false, requireLogin = true, message = "Vui lòng đăng nhập để lưu sản phẩm yêu thích!" });
            }

            // ==========================================================
            // LỖI ĐƯỢC SỬA Ở ĐÂY: Lấy Email chuẩn xác từ ClaimTypes.Email
            // ==========================================================
            var email = User.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(email))
            {
                return Json(new { success = false, message = "Lỗi phiên đăng nhập: Không tìm thấy Email tài khoản." });
            }

            // 3. Lấy thông tin Customer dựa vào Email
            var customer = await _context.Customer
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Account.Email == email);

            if (customer == null)
            {
                return Json(new { success = false, message = "Lỗi xác thực: Không tìm thấy thông tin khách hàng." });
            }

            // 4. Tìm danh sách yêu thích (Favorites) của khách này
            var favorite = await _context.Favorites
                .Include(f => f.FavoriteDetails)
                .FirstOrDefaultAsync(f => f.CustomerId == customer.CustomerId);

            if (favorite == null)
            {
                favorite = new Favorites
                {
                    CustomerId = customer.CustomerId,
                    CreatedAt = DateTime.Now
                };
                _context.Favorites.Add(favorite);
                await _context.SaveChangesAsync(); // Lưu trước để Database tự sinh ra FavoriteId
            }

            // 5. Kiểm tra xem Biến thể (Variant) này đã có trong danh sách yêu thích chưa
            var detail = favorite.FavoriteDetails.FirstOrDefault(d => d.VariantId == variantId);
            bool isAdded = false;

            if (detail != null)
            {
                // Nếu ĐÃ CÓ -> Xóa khỏi danh sách (Bỏ yêu thích)
                _context.FavoriteDetails.Remove(detail);
            }
            else
            {
                // Nếu CHƯA CÓ -> Thêm vào danh sách (Thêm yêu thích)
                _context.FavoriteDetails.Add(new FavoriteDetails
                {
                    FavoriteId = favorite.FavoriteId,
                    VariantId = variantId
                });
                isAdded = true;
            }

            await _context.SaveChangesAsync();

            // 6. Trả kết quả JSON về cho Javascript xử lý giao diện hiển thị Toast
            return Json(new
            {
                success = true,
                isAdded = isAdded,
                message = isAdded ? "Đã thêm vào danh sách yêu thích!" : "Đã xóa khỏi danh sách yêu thích!"
            });
        }
        // Thêm hàm này vào bên trong lớp FavoriteController

        [HttpGet]
        [Route("Account/Favorites")] // Bắt đường dẫn từ _Layout.cshtml vào đây
        public async Task<IActionResult> Index()
        {
            // 1. Kiểm tra đăng nhập
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Account/Favorites" });
            }

            var email = User.FindFirstValue(ClaimTypes.Email);

            // 2. Tìm Khách hàng
            var customer = await _context.Customer
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Account.Email == email);

            if (customer == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            // 3. Lấy toàn bộ danh sách Yêu thích của khách hàng này, nối (Join) các bảng để lấy Tên máy, Ảnh, Giá...
            var favorite = await _context.Favorites
                .Include(f => f.FavoriteDetails)
                    .ThenInclude(fd => fd.Variant)
                        .ThenInclude(v => v.Product)
                .FirstOrDefaultAsync(f => f.CustomerId == customer.CustomerId);

            // Chỉ lấy những biến thể còn đang Active (đề phòng Admin đã ẩn/xóa sản phẩm)
            var favoriteList = favorite?.FavoriteDetails
                .Where(fd => fd.Variant != null && fd.Variant.IsActive == true && fd.Variant.Product != null && fd.Variant.Product.IsActive == true)
                .OrderByDescending(fd => fd.FavoriteDetailId) // Xếp mới nhất lên đầu
                .ToList() ?? new List<FavoriteDetails>();

            return View("~/Views/Favorite/Index.cshtml", favoriteList);
        }
    }
}