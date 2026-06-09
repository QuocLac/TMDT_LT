using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;

namespace TMDT_LT.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Lấy toàn bộ dữ liệu hiển thị lên trang chủ
        public async Task<IActionResult> Index(string? searchTerm, int? categoryId, int? brandId)
        {
            // 1. Lấy danh sách Thương hiệu và Danh mục để làm Menu lọc ở Sidebar
            ViewBag.Brands = await _context.Brands.ToListAsync();
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive == true).ToListAsync();

            // Lưu lại bộ lọc hiện tại để highlight trên UI
            ViewBag.CurrentSearch = searchTerm;
            ViewBag.CurrentCategory = categoryId;
            ViewBag.CurrentBrand = brandId;

            // 2. Xây dựng truy vấn lấy danh sách sản phẩm
            var query = _context.Products
                .Include(p => p.ProductVariants)
                .Where(p => p.IsActive == true);

            // Lọc theo từ khóa tìm kiếm nếu có
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(p => p.Name.Contains(searchTerm) || p.Chipset.Contains(searchTerm));
            }

            // Lọc theo danh mục sản phẩm nếu có
            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            // Lọc theo thương hiệu nếu có
            if (brandId.HasValue)
            {
                query = query.Where(p => p.BrandId == brandId.Value);
            }

            // Sắp xếp sản phẩm mới lên trước
            var products = await query.OrderByDescending(p => p.CreatedDate).ToListAsync();

            return View(products);
        }

        // Thêm vào trong class HomeController
        public async Task<IActionResult> Detail(int? id)
        {
            if (id == null) return NotFound();

            // 1. Lấy thông tin sản phẩm chính
            var product = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants.Where(v => v.IsActive == true))
                .FirstOrDefaultAsync(p => p.ProductId == id && p.IsActive == true);

            if (product == null) return NotFound();

            // =================================================================
            // THUẬT TOÁN GỢI Ý SẢN PHẨM (CONTENT-BASED FILTERING)
            // =================================================================

            // Xác định giá bán hiện tại của sản phẩm đang xem để làm mốc so sánh
            var currentVariant = product.ProductVariants.FirstOrDefault();
            decimal currentPrice = currentVariant?.DiscountPrice ?? currentVariant?.Price ?? 0;

            // Bước A: Kéo danh sách thô từ Database (Ràng buộc cứng: Cùng danh mục, Đang bán)
            var rawRelatedQuery = await _context.Products
                .Include(p => p.ProductVariants)
                .Where(p => p.CategoryId == product.CategoryId && p.ProductId != id && p.IsActive == true)
                .ToListAsync();

            // Bước B: Tính toán trọng số và xếp hạng trên RAM
            var relatedProducts = rawRelatedQuery
                .Select(p => new {
                    Product = p,
                    // Xác định giá của từng sản phẩm liên quan
                    Price = p.ProductVariants.FirstOrDefault()?.DiscountPrice ?? p.ProductVariants.FirstOrDefault()?.Price ?? 0,
                    // Trọng số 1: Cùng hãng (1 = True, 0 = False)
                    IsSameBrand = p.BrandId == product.BrandId ? 1 : 0
                })
                .OrderByDescending(x => x.IsSameBrand) // Ưu tiên 1: Đưa sản phẩm cùng hãng lên đầu
                .ThenBy(x => Math.Abs(x.Price - currentPrice)) // Ưu tiên 2: Khoảng cách giá càng nhỏ (gần bằng giá SP đang xem) càng xếp trên
                .Select(x => x.Product)
                .Take(5) // Trích xuất 5 sản phẩm tối ưu nhất
                .ToList();

            // =================================================================

            // 3. Xử lý phần Đánh giá (Reviews) - Giữ nguyên như cũ
            var variantIds = product.ProductVariants.Select(v => v.VariantId).ToList();

            var reviews = await _context.ReviewDetails
                .Include(rd => rd.Review)
                    .ThenInclude(r => r.Customer)
                .Include(rd => rd.Variant)
                .Where(rd => rd.VariantId.HasValue && variantIds.Contains(rd.VariantId.Value))
                .OrderByDescending(rd => rd.Review.CreatedAt)
                .ToListAsync();

            double averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating ?? 0), 1) : 0;
            int totalReviews = reviews.Count;

            // Đẩy dữ liệu ra View
            ViewBag.RelatedProducts = relatedProducts;
            ViewBag.Reviews = reviews;
            ViewBag.AverageRating = averageRating;
            ViewBag.TotalReviews = totalReviews;

            return View(product);
        }
    }
}