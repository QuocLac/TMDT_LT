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
using TMDT_LT.Models.ViewModels;

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

        public async Task<IActionResult> Create()
        {
            ViewBag.Categories = new SelectList(await _context.Categories.Where(c => c.IsActive == true).ToListAsync(), "CategoryId", "CategoryName");
            ViewBag.Brands = new SelectList(await _context.Brands.ToListAsync(), "BrandId", "BrandName");
            return View(new ProductCreateVM());
        }

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

                var product = new Products
                {
                    Name = model.Name,
                    CategoryId = model.CategoryId,
                    BrandId = model.BrandId,
                    MainImage = mainImagePath,
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
                            ImageUrl = string.IsNullOrEmpty(variantImagePath) ? mainImagePath : variantImagePath,
                            Stock = 0,
                            IsActive = true,
                            CreatedDate = DateTime.Now
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

                TempData["Success"] = "Khởi tạo thông tin thiết bị thành công.";
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
                Weight = product.Weight,
                Description = product.Description,
                ExistingGallery = product.ProductImages.ToList(),
                Variants = product.ProductVariants.Select(v => new VariantEditVM
                {
                    VariantId = v.VariantId,
                    Color = v.Color,
                    Ram = v.Ram,
                    Storage = v.Storage,
                    Price = v.Price ?? 0,
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
                product.Weight = model.Weight;
                product.Description = model.Description;

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSingleVariant([FromForm] int productId, [FromForm] string color, [FromForm] string ram, [FromForm] string storage, [FromForm] decimal price, IFormFile? variantImageFile)
        {
            if (string.IsNullOrWhiteSpace(color) || string.IsNullOrWhiteSpace(storage) || price < 0)
                return Json(new { success = false, message = "Vui lòng điền đầy đủ các thông tin bắt buộc (*)." });

            try
            {
                var product = await _context.Products.FindAsync(productId);
                if (product == null) return Json(new { success = false, message = "Không tìm thấy sản phẩm gốc." });

                string imagePath = product.MainImage;
                if (variantImageFile != null && variantImageFile.Length > 0)
                    imagePath = await ProcessUploadImageAsync(variantImageFile);

                var newVariant = new ProductVariants
                {
                    ProductId = productId,
                    Color = color.Trim(),
                    Ram = ram?.Trim() ?? string.Empty,
                    Storage = storage.Trim(),
                    Price = price,
                    ImageUrl = imagePath,
                    Stock = 0,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };

                _context.ProductVariants.Add(newVariant);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

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

            await _context.SaveChangesAsync();
            return Json(new { success = true, currentPrice = variant.Price, newDiscount = variant.DiscountPrice });
        }

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

    public class DiscountRequest
    {
        public int VariantId { get; set; }
        public decimal DiscountPrice { get; set; }
    }
}