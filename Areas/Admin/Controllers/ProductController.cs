using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ProductController : Controller
    {
        private readonly ApplicationDbContext _context;
        public ProductController(ApplicationDbContext context) => _context = context;

        // 1. DANH SÁCH SẢN PHẨM & TÌM KIẾM THÔNG MINH
        public async Task<IActionResult> Index(string searchKeyword, int? categoryId, int? brandId, string status)
        {
            // Đổ dữ liệu Dropdown cho Bộ lọc
            ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "CategoryId", "CategoryName", categoryId);
            ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName", brandId);
            ViewBag.Keyword = searchKeyword;
            ViewBag.Status = status;

            // Truy vấn gốc (Bao gồm dữ liệu bảng cha và bảng con)
            var query = _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants)
                .AsQueryable();

            // Áp dụng bộ lọc đa chiều (Faceted Navigation)
            if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);
            if (brandId.HasValue) query = query.Where(p => p.BrandId == brandId.Value);

            if (!string.IsNullOrEmpty(status))
            {
                bool isActive = status == "active";
                query = query.Where(p => p.IsActive == isActive);
            }

            ViewBag.IsFuzzyMatch = false; // Cờ đánh dấu tìm kiếm tương đối

            // Xử lý Tìm kiếm (Smart Search)
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                string keyword = searchKeyword.Trim().ToLower();

                // Cấp độ 1: Tìm kiếm chính xác (Exact Match)
                var exactMatchQuery = query.Where(p => p.Name.ToLower().Contains(keyword));

                if (await exactMatchQuery.AnyAsync())
                {
                    query = exactMatchQuery;
                }
                else
                {
                    // Cấp độ 2: Tìm kiếm tương đối (Fuzzy Search)
                    // Tách từ khóa và quét sâu vào các trường thông số (RAM, Chip, ROM) của bản thân và biến thể con
                    var tokens = keyword.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    // Kỹ thuật xử lý trên RAM (Client Evaluation) do EF Core khó phân giải truy vấn động với Any
                    var allFilteredProducts = await query.ToListAsync();

                    var fuzzyResults = allFilteredProducts.Where(p =>
                        tokens.Any(t => p.Name.ToLower().Contains(t)) ||
                        (p.Chipset != null && tokens.Any(t => p.Chipset.ToLower().Contains(t))) ||
                        p.ProductVariants.Any(v => tokens.Any(t => v.Storage.ToLower().Contains(t) || v.Color.ToLower().Contains(t)))
                    ).ToList();

                    ViewBag.IsFuzzyMatch = true;
                    return View(fuzzyResults.OrderByDescending(p => p.CreatedDate));
                }
            }

            var products = await query.OrderByDescending(p => p.CreatedDate).ToListAsync();
            return View(products);
        }

        // 2. FORM THÊM SẢN PHẨM (CREATE GET)
        public async Task<IActionResult> Create()
        {
            ViewBag.Categories = new SelectList(await _context.Categories.Where(c => c.IsActive == true).ToListAsync(), "CategoryId", "CategoryName");
            ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName");
            return View(new ProductCreateVM());
        }

        // 3. XỬ LÝ LƯU SẢN PHẨM (CREATE POST)
        [HttpPost]
        public async Task<IActionResult> Create(ProductCreateVM model)
        {
            if (!ModelState.IsValid || !model.Variants.Any())
            {
                ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "CategoryId", "CategoryName");
                ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName");
                TempData["Error"] = "Vui lòng nhập đủ thông tin và ít nhất 1 biến thể.";
                return View(model);
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Bước A: Lưu sản phẩm chính
                var product = new Products
                {
                    Name = model.Name,
                    CategoryId = model.CategoryId,
                    BrandId = model.BrandId,
                    MainImage = model.MainImage,
                    ScreenSize = model.ScreenSize,
                    ScreenTech = model.ScreenTech,
                    RefreshRate = model.RefreshRate,
                    Chipset = model.Chipset,
                    OperatingSystem = model.OperatingSystem,
                    BatteryCapacity = model.BatteryCapacity,
                    RearCamera = model.RearCamera,
                    Weight = model.Weight,
                    Description = model.Description,
                    CreatedDate = DateTime.Now,
                    IsActive = true
                };

                _context.Products.Add(product);
                await _context.SaveChangesAsync(); // Lấy ProductId

                // Bước B: Lưu danh sách biến thể đi kèm
                foreach (var v in model.Variants)
                {
                    var variant = new ProductVariants
                    {
                        ProductId = product.ProductId, // Gắn ID vừa tạo
                        Color = v.Color,
                        Ram = v.RAM,
                        Storage = v.Storage,
                        Price = v.Price,
                        ImageUrl = v.ImageUrl,
                        Stock = 0, // Sản phẩm mới tạo, tồn kho luôn bằng 0. Phải đi qua phiếu nhập kho mới có hàng.
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    };
                    _context.ProductVariants.Add(variant);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] = "Thêm sản phẩm thành công!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Lỗi hệ thống: " + ex.Message;
                return View(model);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var product = await _context.Products
                .Include(p => p.ProductVariants)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            return View(product);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Products product)
        {
            if (ModelState.IsValid)
            {
                _context.Update(product);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(product);
        }

        // 4. XÓA MỀM / KHÔI PHỤC (TOGGLE STATUS - AJAX)
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var product = await _context.Products
                .Include(p => p.ProductVariants)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return Json(new { success = false, message = "Không tìm thấy sản phẩm" });

            // Đảo ngược trạng thái
            product.IsActive = !product.IsActive;

            // Cascading: Đảo ngược trạng thái tất cả biến thể con
            foreach (var variant in product.ProductVariants)
            {
                variant.IsActive = product.IsActive;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, isActive = product.IsActive });
        }

        // 2. Màn hình Chiến dịch Khuyến mãi & Gợi ý xả kho
        public async Task<IActionResult> Promotions()
        {
            // Lấy tất cả biến thể để phân tích
            var variants = await _context.ProductVariants
                .Include(v => v.Product)
                .Where(v => v.IsActive == true)
                .ToListAsync();

            // THUẬT TOÁN HỖ TRỢ QUYẾT ĐỊNH (Decision Support Algorithm):
            // Phân loại các biến thể đang có số lượng tồn kho TỪ 20 TRỞ LÊN thành "Hàng Cần Xả"
            var warningStock = variants.Where(v => v.Stock >= 20 && v.DiscountPrice == null).ToList();

            // Phân loại các biến thể đang áp dụng khuyến mãi
            var activePromotions = variants.Where(v => v.DiscountPrice != null).ToList();

            ViewBag.WarningStock = warningStock;
            ViewBag.ActivePromotions = activePromotions;

            return View(variants);
        }

        // 3. API xử lý cập nhật Giá Khuyến Mãi (AJAX)
        [HttpPost]
        public async Task<IActionResult> UpdateDiscount([FromBody] DiscountRequest req)
        {
            var variant = await _context.ProductVariants.FindAsync(req.VariantId);
            if (variant == null) return Json(new { success = false, message = "Không tìm thấy biến thể" });

            if (req.DiscountPrice >= variant.Price)
                return Json(new { success = false, message = "Giá khuyến mãi phải nhỏ hơn giá gốc!" });

            if (req.DiscountPrice <= 0)
                variant.DiscountPrice = null; // Nếu truyền 0 tức là Hủy khuyến mãi
            else
                variant.DiscountPrice = req.DiscountPrice;

            await _context.SaveChangesAsync();
            return Json(new { success = true, currentPrice = variant.Price, newDiscount = variant.DiscountPrice });
        }
    }

    public class DiscountRequest
    {
        public int VariantId { get; set; }
        public decimal DiscountPrice { get; set; }
    }
}