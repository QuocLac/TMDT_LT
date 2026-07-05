using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Admin;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ProductController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProductController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // ============================================================
        // 1. INDEX - HIỂN THỊ DANH SÁCH SẢN PHẨM
        // ============================================================
        public async Task<IActionResult> Index(string searchKeyword, int? categoryId, int? brandId, string status)
        {
            ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "CategoryId", "CategoryName", categoryId);
            ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName", brandId);
            ViewBag.Keyword = searchKeyword;
            ViewBag.Status = status;

            var query = _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants)
                .AsQueryable();

            if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);
            if (brandId.HasValue) query = query.Where(p => p.BrandId == brandId.Value);

            if (!string.IsNullOrEmpty(status))
            {
                bool isActive = status == "active";
                query = query.Where(p => p.IsActive == isActive);
            }

            ViewBag.IsFuzzyMatch = false;

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                string keyword = searchKeyword.Trim().ToLower();
                var exactMatchQuery = query.Where(p => p.Name.ToLower().Contains(keyword));

                if (await exactMatchQuery.AnyAsync())
                {
                    query = exactMatchQuery;
                }
                else
                {
                    var tokens = keyword.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

        // ============================================================
        // 2. CREATE - TẠO SẢN PHẨM MỚI
        // ============================================================
        public async Task<IActionResult> Create()
        {
            ViewBag.Categories = new SelectList(await _context.Categories.Where(c => c.IsActive == true).ToListAsync(), "CategoryId", "CategoryName");
            ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName");
            return View(new ProductCreateVM());
        }

        // ============================================================
        // 2. CREATE - TẠO SẢN PHẨM MỚI (CẬP NHẬT)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductCreateVM model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Categories = new SelectList(await _context.Categories.Where(c => c.IsActive == true).ToListAsync(), "CategoryId", "CategoryName", model.CategoryId);
                ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName", model.BrandId);
                return View(model);
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                string mainImagePath = await ProcessUploadImageAsync(model.MainImageFile);

                // Lấy thông số kỹ thuật từ Form (dynamic fields)
                var form = Request.Form;
                var product = new Products
                {
                    Name = model.Name,
                    CategoryId = model.CategoryId,
                    BrandId = model.BrandId,
                    MainImage = mainImagePath,
                    Chipset = form["Chipset"].ToString(),
                    OperatingSystem = form["OperatingSystem"].ToString(),
                    BatteryCapacity = string.IsNullOrEmpty(form["BatteryCapacity"]) ? (short?)null : short.Parse(form["BatteryCapacity"]),
                    ScreenSize = string.IsNullOrEmpty(form["ScreenSize"]) ? (decimal?)null : decimal.Parse(form["ScreenSize"]),
                    ScreenTech = form["ScreenTech"].ToString(),
                    RefreshRate = string.IsNullOrEmpty(form["RefreshRate"]) ? (short?)null : short.Parse(form["RefreshRate"]),
                    RearCamera = form["RearCamera"].ToString(),
                    FrontCamera = form["FrontCamera"].ToString(),
                    Weight = string.IsNullOrEmpty(form["Weight"]) ? (decimal?)null : decimal.Parse(form["Weight"]),
                    Dimensions = form["Dimensions"].ToString(),
                    Description = model.Description,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now,
                    IsActive = true
                };

                _context.Products.Add(product);
                await _context.SaveChangesAsync();

                if (model.Variants != null)
                {
                    foreach (var v in model.Variants)
                    {
                        string variantImagePath = await ProcessUploadImageAsync(v.VariantImageFile);

                        var variant = new ProductVariants
                        {
                            ProductId = product.ProductId,
                            Color = v.Color,
                            Ram = v.Ram,
                            Storage = v.Storage,
                            Price = v.Price,
                            DiscountPrice = v.DiscountPrice,
                            Stock = v.Stock ?? 0,
                            ImageUrl = string.IsNullOrEmpty(variantImagePath) ? mainImagePath : variantImagePath,
                            IsActive = true,
                            CreatedDate = DateTime.Now,
                            UpdatedDate = DateTime.Now
                        };
                        _context.ProductVariants.Add(variant);
                    }
                }

                if (model.GalleryFiles != null && model.GalleryFiles.Any())
                {
                    foreach (var file in model.GalleryFiles)
                    {
                        string galleryPath = await ProcessUploadImageAsync(file);
                        if (!string.IsNullOrEmpty(galleryPath))
                        {
                            _context.ProductImages.Add(new ProductImages
                            {
                                ProductId = product.ProductId,
                                ImageUrl = galleryPath
                            });
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] = $"Sản phẩm \"{product.Name}\" đã được tạo thành công!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", "Lỗi lưu tệp và dữ liệu: " + ex.Message);
                ViewBag.Categories = new SelectList(await _context.Categories.Where(c => c.IsActive == true).ToListAsync(), "CategoryId", "CategoryName", model.CategoryId);
                ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName", model.BrandId);
                return View(model);
            }
        }

        // ============================================================
        // 3. EDIT - CHỈNH SỬA SẢN PHẨM
        // ============================================================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var product = await _context.Products
                .Include(p => p.ProductVariants)
                .Include(p => p.ProductImages)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return NotFound();

            var model = new ProductEditVM
            {
                ProductId = product.ProductId,
                Name = product.Name,
                CategoryId = (int)product.CategoryId,
                BrandId = (int)product.BrandId,
                MainImage = product.MainImage,
                ScreenSize = product.ScreenSize,
                ScreenTech = product.ScreenTech,
                RefreshRate = product.RefreshRate,
                Chipset = product.Chipset,
                OperatingSystem = product.OperatingSystem,
                BatteryCapacity = product.BatteryCapacity,
                RearCamera = product.RearCamera,
                FrontCamera = product.FrontCamera,
                Weight = product.Weight,
                Dimensions = product.Dimensions,
                Description = product.Description,
                ExistingGallery = product.ProductImages.ToList(),
                Variants = product.ProductVariants.Select(v => new VariantEditVM
                {
                    VariantId = v.VariantId,
                    Color = v.Color,
                    Ram = v.Ram,
                    Storage = v.Storage,
                    Price = v.Price ?? 0,
                    DiscountPrice = v.DiscountPrice,
                    ImageUrl = v.ImageUrl,
                    Stock = v.Stock
                }).ToList()
            };

            ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "CategoryId", "CategoryName", product.CategoryId);
            ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName", product.BrandId);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductEditVM model)
        {
            if (id != model.ProductId) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "CategoryId", "CategoryName", model.CategoryId);
                ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName", model.BrandId);
                return View(model);
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var product = await _context.Products.FindAsync(id);
                if (product == null) return NotFound();

                if (model.MainImageFile != null && model.MainImageFile.Length > 0)
                {
                    product.MainImage = await ProcessUploadImageAsync(model.MainImageFile);
                }

                product.Name = model.Name;
                product.CategoryId = model.CategoryId;
                product.BrandId = model.BrandId;
                product.ScreenSize = model.ScreenSize;
                product.ScreenTech = model.ScreenTech;
                product.RefreshRate = model.RefreshRate;
                product.Chipset = model.Chipset;
                product.OperatingSystem = model.OperatingSystem;
                product.BatteryCapacity = model.BatteryCapacity;
                product.RearCamera = model.RearCamera;
                product.FrontCamera = model.FrontCamera;
                product.Weight = model.Weight;
                product.Dimensions = model.Dimensions;
                product.Description = model.Description;
                product.UpdatedDate = DateTime.Now;

                foreach (var vModel in model.Variants)
                {
                    var variant = await _context.ProductVariants.FindAsync(vModel.VariantId);
                    if (variant != null)
                    {
                        if (vModel.VariantImageFile != null && vModel.VariantImageFile.Length > 0)
                        {
                            variant.ImageUrl = await ProcessUploadImageAsync(vModel.VariantImageFile);
                        }

                        variant.Color = vModel.Color;
                        variant.Ram = vModel.Ram;
                        variant.Storage = vModel.Storage;
                        variant.Price = vModel.Price;
                        variant.DiscountPrice = vModel.DiscountPrice;
                        variant.UpdatedDate = DateTime.Now;
                    }
                }

                if (model.GalleryFiles != null && model.GalleryFiles.Any())
                {
                    foreach (var file in model.GalleryFiles)
                    {
                        string galleryPath = await ProcessUploadImageAsync(file);
                        if (!string.IsNullOrEmpty(galleryPath))
                        {
                            _context.ProductImages.Add(new ProductImages
                            {
                                ProductId = product.ProductId,
                                ImageUrl = galleryPath
                            });
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] = "Cập nhật thông tin thiết bị thành công.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", "Lỗi cập nhật dữ liệu: " + ex.Message);
                ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "CategoryId", "CategoryName", model.CategoryId);
                ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName", model.BrandId);
                return View(model);
            }
        }

        // ============================================================
        // 4. TOGGLE STATUS - BẬT/TẮT TRẠNG THÁI SẢN PHẨM
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var product = await _context.Products
                .Include(p => p.ProductVariants)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return Json(new { success = false, message = "Không tìm thấy sản phẩm" });

            product.IsActive = !product.IsActive;

            foreach (var variant in product.ProductVariants)
            {
                variant.IsActive = product.IsActive;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, isActive = product.IsActive });
        }

        // ============================================================
        // 5. ADD SINGLE VARIANT - THÊM BIẾN THỂ MỚI
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSingleVariant(
            [FromForm] int productId,
            [FromForm] string color,
            [FromForm] string ram,
            [FromForm] string storage,
            [FromForm] decimal price,
            [FromForm] decimal? discountPrice,
            [FromForm] int stock,
            IFormFile? variantImageFile)
        {
            if (string.IsNullOrWhiteSpace(color) || string.IsNullOrWhiteSpace(storage) || price < 0)
                return Json(new { success = false, message = "Vui lòng điền đầy đủ các thông tin bắt buộc (*)." });

            if (discountPrice.HasValue && discountPrice.Value >= price)
                return Json(new { success = false, message = "Giá khuyến mãi phải nhỏ hơn giá niêm yết!" });

            try
            {
                var product = await _context.Products.FindAsync(productId);
                if (product == null) return Json(new { success = false, message = "Không tìm thấy sản phẩm gốc." });

                string imagePath = product.MainImage;
                if (variantImageFile != null && variantImageFile.Length > 0)
                {
                    if (variantImageFile.Length > 5 * 1024 * 1024)
                        return Json(new { success = false, message = "Dung lượng file vượt quá 5MB!" });
                    imagePath = await ProcessUploadImageAsync(variantImageFile);
                }

                var newVariant = new ProductVariants
                {
                    ProductId = productId,
                    Color = color.Trim(),
                    Ram = ram?.Trim() ?? string.Empty,
                    Storage = storage.Trim(),
                    Price = price,
                    DiscountPrice = discountPrice,
                    Stock = stock,
                    ImageUrl = imagePath,
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now
                };

                _context.ProductVariants.Add(newVariant);
                await _context.SaveChangesAsync();

                return Json(new { success = true, variantId = newVariant.VariantId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        // ============================================================
        // 6. DELETE GALLERY IMAGE - XÓA ẢNH THƯ VIỆN
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> DeleteGalleryImage(int id)
        {
            var image = await _context.ProductImages.FindAsync(id);
            if (image == null) return Json(new { success = false, message = "Không tìm thấy ảnh" });

            var filePath = Path.Combine(_webHostEnvironment.WebRootPath, image.ImageUrl.TrimStart('/'));
            if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);

            _context.ProductImages.Remove(image);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // ============================================================
        // 7. PROMOTIONS - QUẢN LÝ KHUYẾN MÃI
        // ============================================================
        public async Task<IActionResult> Promotions()
        {
            var variants = await _context.ProductVariants
                .Include(v => v.Product)
                .Where(v => v.IsActive == true)
                .ToListAsync();

            var warningStock = variants.Where(v => v.Stock >= 20 && v.DiscountPrice == null).ToList();
            var activePromotions = variants.Where(v => v.DiscountPrice != null).ToList();

            ViewBag.WarningStock = warningStock;
            ViewBag.ActivePromotions = activePromotions;

            return View(variants);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateDiscount([FromBody] DiscountRequest req)
        {
            var variant = await _context.ProductVariants.FindAsync(req.VariantId);
            if (variant == null) return Json(new { success = false, message = "Không tìm thấy biến thể" });

            if (req.DiscountPrice >= variant.Price)
                return Json(new { success = false, message = "Giá khuyến mãi phải nhỏ hơn giá gốc!" });

            if (req.DiscountPrice <= 0)
                variant.DiscountPrice = null;
            else
                variant.DiscountPrice = req.DiscountPrice;

            variant.UpdatedDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return Json(new { success = true, currentPrice = variant.Price, newDiscount = variant.DiscountPrice });
        }

        // ============================================================
        // 8. GET PRODUCT BY ID - LẤY THÔNG TIN SẢN PHẨM (API)
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> GetProductById(int id)
        {
            var product = await _context.Products
                .Include(p => p.ProductVariants)
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null) return Json(new { success = false, message = "Không tìm thấy sản phẩm" });

            return Json(new
            {
                success = true,
                product = new
                {
                    productId = product.ProductId,
                    name = product.Name,
                    brandName = product.Brand?.BrandName,
                    categoryName = product.Category?.CategoryName,
                    mainImage = product.MainImage,
                    description = product.Description,
                    chipset = product.Chipset,
                    operatingSystem = product.OperatingSystem,
                    screenSize = product.ScreenSize,
                    screenTech = product.ScreenTech,
                    refreshRate = product.RefreshRate,
                    batteryCapacity = product.BatteryCapacity,
                    rearCamera = product.RearCamera,
                    frontCamera = product.FrontCamera,
                    weight = product.Weight,
                    dimensions = product.Dimensions,
                    variants = product.ProductVariants.Select(v => new
                    {
                        variantId = v.VariantId,
                        color = v.Color,
                        ram = v.Ram,
                        storage = v.Storage,
                        price = v.Price,
                        discountPrice = v.DiscountPrice,
                        stock = v.Stock,
                        imageUrl = v.ImageUrl,
                        isActive = v.IsActive
                    })
                }
            });
        }

        // ============================================================
        // 9. QUICK ADD PRODUCT FROM PO - THÊM SẢN PHẨM NHANH TỪ PHIẾU NHẬP
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> QuickAddProductFromPO([FromForm] QuickProductFromPO req)
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Color) || string.IsNullOrWhiteSpace(req.Storage))
                return Json(new { success = false, message = "Thiếu thông tin bắt buộc!" });

            if (req.ImportPrice <= 0 || req.ListPrice <= 0)
                return Json(new { success = false, message = "Giá nhập và giá bán phải lớn hơn 0!" });

            if (req.ImportPrice >= req.ListPrice)
                return Json(new { success = false, message = "Giá nhập phải nhỏ hơn giá bán!" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                string imagePath = "/images/products/default-product.png";
                if (req.ImageFile != null && req.ImageFile.Length > 0)
                {
                    if (req.ImageFile.Length > 5 * 1024 * 1024)
                        return Json(new { success = false, message = "Dung lượng file vượt quá 5MB!" });
                    imagePath = await ProcessUploadImageAsync(req.ImageFile);
                }

                // Tạo sản phẩm mới
                var product = new Products
                {
                    Name = req.Name,
                    CategoryId = req.CategoryId,
                    BrandId = req.BrandId,
                    MainImage = imagePath,
                    Chipset = req.Chipset,
                    OperatingSystem = req.OperatingSystem,
                    Description = req.Description,
                    Dimensions = req.Dimensions,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now,
                    IsActive = true
                };

                _context.Products.Add(product);
                await _context.SaveChangesAsync();

                // Tạo biến thể
                var variant = new ProductVariants
                {
                    ProductId = product.ProductId,
                    Color = req.Color,
                    Storage = req.Storage,
                    Ram = req.Ram,
                    Price = req.ListPrice,
                    Stock = req.Quantity,
                    ImageUrl = imagePath,
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now
                };

                _context.ProductVariants.Add(variant);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    variantId = variant.VariantId,
                    productId = product.ProductId,
                    newVariant = new
                    {
                        variantId = variant.VariantId,
                        productName = req.Name,
                        color = req.Color,
                        storage = req.Storage,
                        lastImportPrice = req.ImportPrice,
                        stock = req.Quantity,
                        imageUrl = imagePath
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi tạo sản phẩm: " + ex.Message });
            }
        }

        // ============================================================
        // 10. PROCESS UPLOAD IMAGE - XỬ LÝ UPLOAD HÌNH ẢNH
        // ============================================================
        private async Task<string> ProcessUploadImageAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0) return string.Empty;

            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "products");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            return $"/uploads/products/{uniqueFileName}";
        }
    }

    // ============================================================
    // REQUEST DTO MODELS
    // ============================================================
    public class DiscountRequest
    {
        public int VariantId { get; set; }
        public decimal DiscountPrice { get; set; }
    }

    public class QuickProductFromPO
    {
        public string Name { get; set; } = null!;
        public int CategoryId { get; set; }
        public int BrandId { get; set; }
        public string Color { get; set; } = null!;
        public string Storage { get; set; } = null!;
        public string? Ram { get; set; }
        public string? Chipset { get; set; }
        public string? OperatingSystem { get; set; }
        public decimal ImportPrice { get; set; }
        public decimal ListPrice { get; set; }
        public int Quantity { get; set; }
        public string? Description { get; set; }
        public string? Dimensions { get; set; }
        public IFormFile? ImageFile { get; set; }
    }

}