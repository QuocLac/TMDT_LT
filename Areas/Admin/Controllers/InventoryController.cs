using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    #region --- REQUEST DTO MODELS ---

    public class QuickProductReq
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
        public bool IsPublished { get; set; }
        public string? Specs { get; set; }
        public string? Description { get; set; }
        public string? Dimensions { get; set; }
        public string? ImageUrl { get; set; }
        public IFormFile? ImageFile { get; set; }
    }

    public class POItemReq
    {
        public int VariantId { get; set; }
        public int Quantity { get; set; }
        public decimal ImportPrice { get; set; }
        public decimal TaxRate { get; set; }
    }

    public class PORequestReq
    {
        public int SupplierId { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public decimal ShippingFee { get; set; }
        public decimal OtherFee { get; set; }
        public string? Note { get; set; }
        public List<POItemReq> Items { get; set; } = new List<POItemReq>();
    }

    public class SOItemReq
    {
        public int VariantId { get; set; }
        public int Quantity { get; set; }
        public decimal ExportPrice { get; set; }
        public decimal TaxRate { get; set; }
    }

    public class SORequestReq
    {
        public int StoreId { get; set; }
        public int FromWarehouseId { get; set; }
        public string? InvoiceNumber { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal ShippingCost { get; set; }
        public string? Notes { get; set; }
        public List<SOItemReq> Items { get; set; } = new List<SOItemReq>();
    }

    public class DeleteLotReq
    {
        public int LotId { get; set; }
        public int VariantId { get; set; }
        public int Quantity { get; set; }
        public string Reason { get; set; } = null!;
    }

    public class RestoreLotReq
    {
        public int LotId { get; set; }
        public int VariantId { get; set; }
        public string Reason { get; set; } = null!;
    }
    // Thêm vào vùng #region --- REQUEST DTO MODELS ---
    public class UpdateLotDetailReq
    {
        public int LotId { get; set; }
        public int VariantId { get; set; }
        public string? ProductName { get; set; } // 🔥 Bổ sung trường sửa tên sản phẩm gốc (Mới)
        public string? VariantName { get; set; } // Bổ sung sửa tên cấu hình biến thể
        public int RemainingQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal Price { get; set; }
        public string StoreLocation { get; set; } = null!;
        public string? ImageUrl { get; set; }
        public IFormFile? ImageFile { get; set; }
    }
    #endregion

    [Area("Admin")]
    public class InventoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        public InventoryController(ApplicationDbContext context) => _context = context;
        private int GetCurrentAccountId() => int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 1;

        // ==========================================
        // 1. TRANG INDEX - HIỂN THỊ TỒN KHO & BỘ LỌC
        // ==========================================
        public async Task<IActionResult> Index(int? year, DateTime? fromDate, DateTime? toDate)
        {
            int filterYear = year ?? DateTime.Now.Year;
            ViewBag.SelectedYear = filterYear;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            // 1. Khởi tạo truy vấn gốc độc lập cho Biến thể và Lô hàng b bãi
            var variantsQuery = _context.ProductVariants
                .Include(v => v.Product)
                .Where(v => v.IsActive == true && v.Price > 0);

            var lotsQuery = _context.InventoryLots
    .Include(l => l.Variant) // Tải thông tin Variant (chứa ImageUrl)
        .ThenInclude(v => v!.Product) // Tải thông tin Product gốc (chứa Name)
    .Where(l => l.IsDeleted == false)
    .AsQueryable();

            // 2. Áp dụng bộ lọc ngày riêng biệt cho từng thực thể tương ứng theo đúng trường nghiệp vụ
            if (fromDate.HasValue)
            {
                variantsQuery = variantsQuery.Where(v => v.CreatedDate >= fromDate.Value);
                lotsQuery = lotsQuery.Where(l => l.ReceivedDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                DateTime endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
                variantsQuery = variantsQuery.Where(v => v.CreatedDate <= endOfDay);
                lotsQuery = lotsQuery.Where(l => l.ReceivedDate <= endOfDay);
            }

            // Tải dữ liệu về bộ nhớ sau khi lọc - Sắp xếp theo thứ tự giảm dần thời gian của từng thực thể
            var variants = await variantsQuery.OrderByDescending(v => v.CreatedDate).ToListAsync();
            var inventoryLots = await lotsQuery.OrderByDescending(l => l.LotId).ToListAsync(); // 🔥 SỬA: Sắp xếp theo LotId để đảm bảo tính phân tách định danh rõ ràng

            // 3. Tính toán các chỉ số Overview hệ thống
            ViewBag.TotalProductsCount = await _context.InventoryLots
                .Where(l => l.IsDeleted == false && l.IsActive == true)
                .SumAsync(l => l.RemainingQuantity);

            ViewBag.TotalVariantsCount = variants.Count(v => (v.Stock ?? 0) > 0);
            ViewBag.LowStockCount = variants.Count(v => (v.Stock ?? 0) > 0 && (v.Stock ?? 0) <= 5);
            ViewBag.ActiveStoresCount = await _context.Stores.CountAsync(s => s.IsActive == true);

            decimal totalInventoryValueCost = inventoryLots
                .Where(l => l.IsActive == true && l.RemainingQuantity > 0 && l.IsDeleted == false)
                .Sum(l => l.RemainingQuantity * l.UnitCost);
            ViewBag.TotalInventoryValue = totalInventoryValueCost;

            decimal totalInventoryValueMarket = variants.Sum(v => (v.Stock ?? 0) * (v.Price ?? 0));
            ViewBag.TotalInventoryValueMarket = totalInventoryValueMarket;
            ViewBag.ExpectedProfit = totalInventoryValueMarket - totalInventoryValueCost;

            var agedThreshold = DateTime.Now.AddDays(-30);
            ViewBag.AgedStockCount = inventoryLots.Count(l => l.RemainingQuantity > 0 && l.ReceivedDate < agedThreshold && l.IsDeleted == false);

            // 4. Báo cáo doanh thu biến động tháng
            var rawMonthlyData = await _context.SalesOrderDetails
                .Include(d => d.SalesOrder)
                .Include(d => d.Variant).ThenInclude(v => v!.Product)
                .Where(d => d.SalesOrder!.OrderDate.Year == filterYear)
                .GroupBy(d => new { Month = d.SalesOrder!.OrderDate.Month, ProductName = d.Variant!.Product!.Name })
                .Select(g => new {
                    MonthNum = g.Key.Month,
                    ProductName = g.Key.ProductName,
                    TotalQty = g.Sum(x => x.Quantity),
                    AvgCost = g.Average(x => x.UnitCost),
                    AvgPrice = g.Average(x => x.UnitPrice),
                    Difference = g.Sum(x => x.Profit),
                    TotalAmount = g.Sum(x => x.TotalAmount)
                }).ToListAsync();

            ViewBag.MonthlyReport = rawMonthlyData.Select(r => new {
                Month = "T" + r.MonthNum.ToString("02"),
                r.ProductName,
                r.TotalQty,
                r.AvgCost,
                r.AvgPrice,
                r.Difference,
                Percent = r.TotalAmount > 0 ? (r.Difference / r.TotalAmount) * 100 : 0
            }).OrderBy(r => r.Month).ToList();

            // Gán tường minh danh sách lô độc lập hoàn toàn
            ViewBag.ActiveLotsTable = inventoryLots;

            return View(variants);
        }

        // ====================================================================
        // 2. TÌM KIẾM SẢN PHẨM BIẾN THỂ - ĐÃ SỬA LỖI NAV PROPERTY (DÒNG ~205)
        // ====================================================================
        [HttpGet]
        public async Task<IActionResult> SearchVariants(string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2) return Json(new List<object>());

            var variantsData = await _context.ProductVariants
                .Include(v => v.Product)
                .Where(v => v.Product.Name.Contains(q) || v.Color.Contains(q) || (v.Storage != null && v.Storage.Contains(q)))
                .Take(10)
                .ToListAsync();

            var result = new List<object>();

            foreach (var v in variantsData)
            {
                // SỬA LỖI: Thực hiện LINQ Join thủ công giữa PurchaseOrderDetails và PurchaseOrders thông qua Poid để lấy OrderDate
                var actualLastImportPrice = await _context.PurchaseOrderDetails
                    .Where(pod => pod.VariantId == v.VariantId)
                    .Join(_context.PurchaseOrders,
                          pod => pod.Poid,
                          po => po.Poid,
                          (pod, po) => new { pod.ImportPrice, po.OrderDate })
                    .OrderByDescending(x => x.OrderDate)
                    .Select(x => x.ImportPrice)
                    .FirstOrDefaultAsync();

                result.Add(new
                {
                    variantId = v.VariantId,
                    productName = v.Product!.Name,
                    color = v.Color,
                    storage = v.Storage + (string.IsNullOrEmpty(v.Ram) ? "" : $" ({v.Ram})"),
                    stock = v.Stock ?? 0,
                    lastImportPrice = actualLastImportPrice
                });
            }

            return Json(result);
        }

        // ==========================================
        // 3. GIAO DIỆN TẠO PHIẾU NHẬP (CREATE PO)
        // ==========================================
        public async Task<IActionResult> CreatePO()
        {
            ViewBag.Suppliers = await _context.Suppliers.Where(s => s.IsActive == true).ToListAsync();
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive == true).ToListAsync();
            ViewBag.Brands = await _context.Brands.ToListAsync();

            var today = DateTime.Now;
            var count = await _context.PurchaseOrders
                .Where(p => p.OrderDate.HasValue && p.OrderDate.Value.Year == today.Year && p.OrderDate.Value.Month == today.Month)
                .CountAsync();

            ViewBag.AutoInvoiceNumber = $"HD-{today:yyyyMM}-{count + 1:D4}";

            return View();
        }

        // ============================================================
        // 4. XỬ LÝ THÊM NHANH SẢN PHẨM MỚI
        // ============================================================
        [HttpPost]
        public async Task<IActionResult> QuickAddProduct([FromForm] QuickProductReq req)
        {
            if (req == null || string.IsNullOrEmpty(req.Name) || string.IsNullOrEmpty(req.Color) || string.IsNullOrEmpty(req.Storage))
                return Json(new { success = false, message = "Thiếu thông tin bắt buộc!" });

            if (req.ListPrice <= 0 || req.ImportPrice <= 0)
                return Json(new { success = false, message = "Giá niêm yết bán ra và giá vốn hạch toán bắt buộc phải > 0 đ!" });

            string pathImageUrl = "/images/products/default-product.png";
            if (req.ImageFile != null && req.ImageFile.Length > 0)
            {
                try
                {
                    string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "products");
                    if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(req.ImageFile.FileName);
                    string fullPath = Path.Combine(folderPath, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await req.ImageFile.CopyToAsync(stream);
                    }
                    pathImageUrl = "/uploads/products/" + fileName;
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = "Lỗi upload thiết lập tệp hình ảnh: " + ex.Message });
                }
            }
            else if (!string.IsNullOrEmpty(req.ImageUrl))
            {
                pathImageUrl = req.ImageUrl;
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var product = new Products
                {
                    Name = req.Name,
                    CategoryId = req.CategoryId,
                    BrandId = req.BrandId,
                    MainImage = pathImageUrl,
                    Chipset = req.Chipset,
                    OperatingSystem = req.OperatingSystem,
                    Description = req.Description,
                    Dimensions = req.Dimensions,
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    UpdatedDate = DateTime.Now
                };
                _context.Products.Add(product);
                await _context.SaveChangesAsync();

                var variant = new ProductVariants
                {
                    ProductId = product.ProductId,
                    Color = req.Color,
                    Storage = req.Storage,
                    Ram = req.Ram,
                    Price = req.ListPrice,
                    Stock = 0,
                    ImageUrl = pathImageUrl,
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
                        stock = 0,
                        imageUrl = pathImageUrl,
                        productId = product.ProductId
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // 5. XỬ LÝ NHẬP KHO CHỨNG TỪ (SUBMIT PO)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> SubmitPO([FromBody] PORequestReq req)
        {
            if (req == null || req.Items == null || !req.Items.Any())
                return Json(new { success = false, message = "Phiếu nhập chưa có cấu hình danh mục hàng hóa" });

            int currentUserId = GetCurrentAccountId();
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var poCode = $"PO-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";

                decimal totalWithTax = req.Items.Sum(d => d.Quantity * d.ImportPrice * (1 + d.TaxRate));
                decimal totalFinalAmount = totalWithTax + req.ShippingFee + req.OtherFee;

                var po = new PurchaseOrders
                {
                    SupplierId = req.SupplierId,
                    AccountId = currentUserId,
                    OrderDate = DateTime.Now,
                    TotalAmount = totalFinalAmount,
                    Status = "Hoàn thành",
                    Note = req.Note
                };
                _context.PurchaseOrders.Add(po);
                await _context.SaveChangesAsync();

                int totalQtyAllItems = req.Items.Sum(i => i.Quantity);

                foreach (var item in req.Items)
                {
                    if (item.Quantity <= 0 || item.ImportPrice <= 0) continue;

                    var poDetail = new PurchaseOrderDetails
                    {
                        Poid = po.Poid,
                        VariantId = item.VariantId,
                        Quantity = item.Quantity,
                        ImportPrice = item.ImportPrice
                    };
                    _context.PurchaseOrderDetails.Add(poDetail);

                    decimal itemTaxAmount = item.ImportPrice * item.TaxRate;
                    decimal allocatedShipping = totalQtyAllItems > 0 ? (req.ShippingFee / totalQtyAllItems) : 0;
                    decimal allocatedOther = totalQtyAllItems > 0 ? (req.OtherFee / totalQtyAllItems) : 0;
                    decimal finalUnitCost = item.ImportPrice + itemTaxAmount + allocatedShipping + allocatedOther;

                    var newLot = new InventoryLots
                    {
                        Poid = po.Poid,
                        VariantId = item.VariantId,
                        SupplierId = req.SupplierId,
                        ReceivedQuantity = item.Quantity,
                        RemainingQuantity = item.Quantity,
                        UnitCost = finalUnitCost,
                        ReceivedDate = req.ReceivedDate ?? DateTime.Now,
                        IsActive = true,
                        IsDeleted = false
                    };
                    _context.InventoryLots.Add(newLot);
                    await _context.SaveChangesAsync();

                    for (int i = 1; i <= item.Quantity; i++)
                    {
                        var serialEntry = new ProductSerials
                        {
                            VariantId = item.VariantId,
                            LotId = newLot.LotId,
                            SerialNumber = $"IMEI-{item.VariantId}-{newLot.LotId}-{i:0000}",
                            Status = "InStock",
                            CreatedDate = DateTime.Now
                        };
                        _context.ProductSerials.Add(serialEntry);
                    }

                    var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                    if (variant != null)
                    {
                        variant.Stock = (variant.Stock ?? 0) + item.Quantity;
                    }

                    var invLog = new InventoryTransactions
                    {
                        VariantId = item.VariantId,
                        TransactionType = "IN_PURCHASE",
                        Quantity = item.Quantity,
                        ReferenceId = po.Poid,
                        TransactionDate = DateTime.Now,
                        AccountId = currentUserId,
                        Note = $"Nhập kho đa mặt hàng từ Lô #{newLot.LotId}. Giá vốn gốc phân bổ: {finalUnitCost:N0} đ"
                    };
                    _context.InventoryTransactions.Add(invLog);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Json(new { success = true, poId = po.Poid, poCode = poCode });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi hệ thống chứng từ nhập: " + ex.Message });
            }
        }

        // ==========================================
        // 6. GIAO DIỆN TẠO PHIẾU XUẤT (CREATE SO)
        // ==========================================
        public async Task<IActionResult> CreateSO()
        {
            ViewBag.Stores = await _context.Stores.Where(s => s.IsActive == true).ToListAsync();
            return View();
        }

        // =========================================================================
        // 7. XỬ LÝ XUẤT KHO ĐẠI LÝ (SERIALIZABLE FIFO)
        // =========================================================================
        [HttpPost]
        public async Task<IActionResult> SubmitSO([FromBody] SORequestReq req)
        {
            if (req == null || req.Items == null || !req.Items.Any())
                return Json(new { success = false, message = "Phiếu xuất chưa cấu hình danh sách sản phẩm" });

            int currentUserId = GetCurrentAccountId();

            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var soCode = $"SO-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 6).ToUpper()}";

                var so = new SalesOrders
                {
                    SOCode = soCode,
                    StoreId = req.StoreId,
                    FromWarehouseId = req.FromWarehouseId,
                    OrderDate = DateTime.Now,
                    Status = "Completed",
                    InvoiceNumber = req.InvoiceNumber,
                    DiscountPercent = req.DiscountPercent,
                    ShippingCost = req.ShippingCost,
                    AccountId = currentUserId,
                    Notes = req.Notes
                };
                _context.SalesOrders.Add(so);
                await _context.SaveChangesAsync();

                decimal totalSalesAmount = 0;
                decimal totalCogsAmount = 0;

                foreach (var item in req.Items)
                {
                    if (item.Quantity <= 0 || item.ExportPrice <= 0) continue;

                    int requiredQty = item.Quantity;

                    var activeLots = await _context.InventoryLots
                        .Where(l => l.VariantId == item.VariantId
                                    && l.RemainingQuantity > 0
                                    && l.IsActive == true
                                    && l.IsDeleted == false)
                        .OrderBy(l => l.ReceivedDate)
                        .ToListAsync();

                    int totalAvailable = activeLots.Sum(l => l.RemainingQuantity);
                    if (totalAvailable < requiredQty)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { success = false, message = $"Sản phẩm mã #{item.VariantId} không đủ số lượng tồn kho theo lô để xuất (Cần: {requiredQty}, Còn: {totalAvailable})" });
                    }

                    foreach (var lot in activeLots)
                    {
                        if (requiredQty <= 0) break;

                        int takeQty = Math.Min(lot.RemainingQuantity, requiredQty);

                        decimal lineAmount = takeQty * item.ExportPrice * (1 + item.TaxRate);
                        decimal lineCost = takeQty * lot.UnitCost;
                        decimal lineProfit = lineAmount - lineCost;

                        var soDetail = new SalesOrderDetails
                        {
                            SOId = so.SOId,
                            VariantId = item.VariantId,
                            LotId = lot.LotId,
                            Quantity = takeQty,
                            UnitPrice = item.ExportPrice,
                            TaxRate = item.TaxRate,
                            UnitCost = lot.UnitCost,
                            TotalAmount = lineAmount,
                            TotalCost = lineCost,
                            Profit = lineProfit
                        };
                        _context.SalesOrderDetails.Add(soDetail);

                        var serialsToUpdate = await _context.ProductSerials
                            .Where(s => s.VariantId == item.VariantId && s.LotId == lot.LotId && s.Status == "InStock")
                            .OrderBy(s => s.CreatedDate)
                            .Take(takeQty)
                            .ToListAsync();

                        foreach (var serial in serialsToUpdate)
                        {
                            serial.Status = "Dispatched";
                        }

                        lot.RemainingQuantity -= takeQty;
                        requiredQty -= takeQty;

                        totalSalesAmount += lineAmount;
                        totalCogsAmount += lineCost;
                    }

                    var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                    if (variant != null)
                    {
                        variant.Stock = (variant.Stock ?? 0) - item.Quantity;
                    }

                    var invLog = new InventoryTransactions
                    {
                        VariantId = item.VariantId,
                        TransactionType = "OUT_ORDER",
                        Quantity = -item.Quantity,
                        ReferenceId = so.SOId,
                        TransactionDate = DateTime.Now,
                        AccountId = currentUserId,
                        Note = $"Xuất kho phân phối chuỗi đa cửa hàng đến mã Store #{req.StoreId} theo phiếu {soCode}"
                    };
                    _context.InventoryTransactions.Add(invLog);
                }

                decimal discountValue = totalSalesAmount * (req.DiscountPercent / 100);
                so.TotalAmount = totalSalesAmount - discountValue + req.ShippingCost;
                so.COGSTotal = totalCogsAmount;
                so.ProfitTotal = so.TotalAmount - totalCogsAmount;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, soId = so.SOId, profit = so.ProfitTotal, soCode = soCode });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi bốc dỡ xuất kho: " + ex.Message });
            }
        }

        // ==========================================
        // 8. ĐIỀU CHỈNH TỒN KHO THỰC TẾ (KIỂM KÊ)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> AdjustStock(int variantId, int actualStock, string note)
        {
            if (actualStock < 0) return Json(new { success = false, message = "Tồn kho thực tế không được âm." });
            if (string.IsNullOrWhiteSpace(note)) return Json(new { success = false, message = "Bắt buộc phải ghi rõ lý do kiểm kê." });

            int currentUserId = GetCurrentAccountId();
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var variant = await _context.ProductVariants.FindAsync(variantId);
                if (variant == null) return Json(new { success = false, message = "Không tìm thấy sản phẩm." });

                int currentStock = variant.Stock ?? 0;
                int diff = actualStock - currentStock;

                variant.Stock = actualStock;

                if (diff != 0)
                {
                    if (diff > 0)
                    {
                        var latestLot = await _context.InventoryLots
                            .Where(l => l.VariantId == variantId
                                        && l.IsActive == true
                                        && l.IsDeleted == false)
                            .OrderByDescending(l => l.ReceivedDate)
                            .FirstOrDefaultAsync();

                        if (latestLot != null)
                        {
                            latestLot.RemainingQuantity += diff;
                            latestLot.ReceivedQuantity += diff;
                        }
                        else
                        {
                            var newLot = new InventoryLots
                            {
                                VariantId = variantId,
                                Poid = 0,
                                SupplierId = 1,
                                ReceivedQuantity = diff,
                                RemainingQuantity = diff,
                                UnitCost = 0,
                                ReceivedDate = DateTime.Now,
                                IsActive = true,
                                IsDeleted = false
                            };
                            _context.InventoryLots.Add(newLot);
                        }
                    }
                    else
                    {
                        int tempDiff = Math.Abs(diff);
                        var activeLots = await _context.InventoryLots
                            .Where(l => l.VariantId == variantId
                                        && l.RemainingQuantity > 0
                                        && l.IsActive == true
                                        && l.IsDeleted == false)
                            .OrderBy(l => l.ReceivedDate)
                            .ToListAsync();

                        foreach (var lot in activeLots)
                        {
                            if (tempDiff <= 0) break;

                            if (lot.RemainingQuantity >= tempDiff)
                            {
                                lot.RemainingQuantity -= tempDiff;
                                tempDiff = 0;
                                break;
                            }
                            else
                            {
                                tempDiff -= lot.RemainingQuantity;
                                lot.RemainingQuantity = 0;
                            }
                        }
                    }
                }

                var invLog = new InventoryTransactions
                {
                    VariantId = variantId,
                    TransactionType = "ADJUST",
                    Quantity = diff,
                    TransactionDate = DateTime.Now,
                    AccountId = currentUserId,
                    Note = $"Kiểm kê kho: {note} (Hệ thống cũ: {currentStock} -> Thực tế: {actualStock})"
                };
                _context.InventoryTransactions.Add(invLog);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, newStock = actualStock, diff = diff });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi hệ thống hạch toán: " + ex.Message });
            }
        }

        // ==========================================
        // 9. XÓA MỀM LÔ HÀNG (SOFT DELETE)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> DeleteLotPhysical([FromBody] DeleteLotReq req)
        {
            if (req == null || req.LotId <= 0 || req.VariantId <= 0)
                return Json(new { success = false, message = "Thông số định danh lô hàng không hợp lệ." });

            if (string.IsNullOrWhiteSpace(req.Reason))
                return Json(new { success = false, message = "Vui lòng nhập lý do xóa lô hàng." });

            int currentUserId = GetCurrentAccountId();
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var lot = await _context.InventoryLots.FirstOrDefaultAsync(l => l.LotId == req.LotId);
                if (lot == null)
                    return Json(new { success = false, message = "Không tìm thấy lô hàng." });

                if (lot.IsDeleted)
                    return Json(new { success = false, message = "Lô hàng đã bị xóa trước đó." });

                bool hasSalesOrder = await _context.SalesOrderDetails.AnyAsync(s => s.LotId == req.LotId);
                string warningMessage = hasSalesOrder ? " (Lô hàng đã có giao dịch xuất kho trong quá khứ.)" : "";

                var variant = await _context.ProductVariants.FindAsync(lot.VariantId);
                if (variant != null)
                {
                    variant.Stock = Math.Max(0, (variant.Stock ?? 0) - lot.RemainingQuantity);
                }

                lot.IsDeleted = true;
                lot.IsActive = false;

                var invLog = new InventoryTransactions
                {
                    VariantId = lot.VariantId,
                    TransactionType = "ADJUST",
                    Quantity = -lot.RemainingQuantity,
                    TransactionDate = DateTime.Now,
                    AccountId = currentUserId,
                    Note = $"[XÓA MỀM Lô #{req.LotId}] Đã đánh dấu xóa. Lý do: {req.Reason}{warningMessage}"
                };
                _context.InventoryTransactions.Add(invLog);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = $"Đã đánh dấu xóa mềm lô hàng #{req.LotId} thành công!{warningMessage}"
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }
        // =========================================================================
        // 9. API CẬP NHẬT CHI TIẾT LÔ HÀNG VÀ THÔNG TIN SẢN PHẨM (MỚI)
        // =========================================================================
        [HttpPost]
        public async Task<IActionResult> UpdateLotDetail([FromForm] UpdateLotDetailReq req)
        {
            if (req == null || req.LotId <= 0 || req.VariantId <= 0)
                return Json(new { success = false, message = "Thông tin lô hàng hạch toán không hợp lệ." });

            if (req.RemainingQuantity < 0)
                return Json(new { success = false, message = "Số lượng tồn kho của lô không được nhỏ hơn 0." });

            if (req.UnitCost < 0 || req.Price < 0)
                return Json(new { success = false, message = "Giá cả hạch toán tài chính không được nhỏ hơn 0 đ." });

            int currentUserId = GetCurrentAccountId();
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Cập nhật bảng Lô kho InventoryLots
                var lot = await _context.InventoryLots.FirstOrDefaultAsync(l => l.LotId == req.LotId);
                if (lot == null) return Json(new { success = false, message = "Không tìm thấy lô hàng trên hệ thống." });

                int oldLotRemaining = lot.RemainingQuantity;
                decimal oldUnitCost = lot.UnitCost;

                lot.RemainingQuantity = req.RemainingQuantity;
                lot.UnitCost = req.UnitCost;
                // Tận dụng trường Poid để lưu vết bãi kho phụ thuộc (Chẵn: HCM, Lẻ: HN như logic View)
                lot.Poid = req.StoreLocation.Contains("HCM") ? 2 : 3;

                // 2. Cập nhật bảng Biến thể ProductVariants (Giá bán, Hình ảnh, Tồn kho tổng)
                var variant = await _context.ProductVariants.FindAsync(req.VariantId);
                if (variant != null)
                {
                    variant.Price = req.Price;

                    // Xử lý Upload ảnh mới nếu thủ kho chọn tệp tin
                    if (req.ImageFile != null && req.ImageFile.Length > 0)
                    {
                        string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "products");
                        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                        string fileName = Guid.NewGuid().ToString() + Path.GetExtension(req.ImageFile.FileName);
                        string fullPath = Path.Combine(folderPath, fileName);

                        using (var stream = new FileStream(fullPath, FileMode.Create))
                        {
                            await req.ImageFile.CopyToAsync(stream);
                        }
                        variant.ImageUrl = "/uploads/products/" + fileName;
                    }
                    else if (!string.IsNullOrEmpty(req.ImageUrl))
                    {
                        variant.ImageUrl = req.ImageUrl;
                    }

                    // Đồng bộ lại tổng kho tổng thể của SKU thiết bị này
                    int diffQty = req.RemainingQuantity - oldLotRemaining;
                    variant.Stock = Math.Max(0, (variant.Stock ?? 0) + diffQty);

                    // Ghi nhận lịch sử biến động kho chi tiết
                    var invLog = new InventoryTransactions
                    {
                        VariantId = variant.VariantId,
                        TransactionType = "ADJUST",
                        Quantity = diffQty,
                        TransactionDate = DateTime.Now,
                        AccountId = currentUserId,
                        Note = $"[CẬP NHẬT LÔ #LOT-00{req.LotId}] Sửa tồn lô ({oldLotRemaining}->{req.RemainingQuantity}), Vốn ({oldUnitCost:N0}->{req.UnitCost:N0}), Giá bán niêm yết: {req.Price:N0}đ tại {req.StoreLocation}"
                    };
                    _context.InventoryTransactions.Add(invLog);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, message = "Cập nhật thông tin chi tiết thiết bị và lô hàng thành công!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi hệ thống đồng bộ dữ liệu: " + ex.Message });
            }
        }
        // ==========================================
        // 10. KHÔI PHỤC LÔ HÀNG (RESTORE)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> RestoreLot([FromBody] RestoreLotReq req)
        {
            if (req == null || req.LotId <= 0 || req.VariantId <= 0)
                return Json(new { success = false, message = "Thông số định danh lô hàng không hợp lệ." });

            if (string.IsNullOrWhiteSpace(req.Reason))
                return Json(new { success = false, message = "Vui lòng nhập lý do khôi phục." });

            int currentUserId = GetCurrentAccountId();
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var lot = await _context.InventoryLots.FirstOrDefaultAsync(l => l.LotId == req.LotId);
                if (lot == null)
                    return Json(new { success = false, message = "Không tìm thấy lô hàng." });

                if (!lot.IsDeleted)
                    return Json(new { success = false, message = "Lô hàng chưa bị xóa, không cần khôi phục." });

                var variant = await _context.ProductVariants.FindAsync(lot.VariantId);
                if (variant != null)
                {
                    variant.Stock = (variant.Stock ?? 0) + lot.RemainingQuantity;
                }

                lot.IsDeleted = false;
                lot.IsActive = true;

                var invLog = new InventoryTransactions
                {
                    VariantId = lot.VariantId,
                    TransactionType = "ADJUST",
                    Quantity = lot.RemainingQuantity,
                    TransactionDate = DateTime.Now,
                    AccountId = currentUserId,
                    Note = $"[KHÔI PHỤC Lô #{req.LotId}] Khôi phục từ trạng thái đã xóa. Lý do: {req.Reason}"
                };
                _context.InventoryTransactions.Add(invLog);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = $"Đã khôi phục lô hàng #{req.LotId} thành công!"
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // ==========================================
        // 11. LỊCH SỬ NHẬP - XUẤT (PO HISTORY)
        // ==========================================
        public async Task<IActionResult> POHistory(int? poId)
        {
            var purchaseOrdersQuery = _context.PurchaseOrders.Include(p => p.Supplier).Include(p => p.Account).AsQueryable();
            if (poId.HasValue)
            {
                purchaseOrdersQuery = purchaseOrdersQuery.Where(p => p.Poid == poId.Value);
            }

            var purchaseOrders = await purchaseOrdersQuery.ToListAsync();
            var salesOrders = await _context.SalesOrders.Include(s => s.Store).Include(s => s.Account).ToListAsync();

            ViewBag.TotalPoAmount = purchaseOrders.Sum(p => p.TotalAmount ?? 0);
            ViewBag.TotalSoAmount = salesOrders.Sum(s => s.TotalAmount);
            ViewBag.NetProfitDistribution = salesOrders.Sum(s => s.ProfitTotal);

            ViewBag.TargetPOId = poId;
            ViewBag.SalesOrdersList = salesOrders.OrderByDescending(s => s.OrderDate).ToList();
            return View(purchaseOrders.OrderByDescending(p => p.OrderDate).ToList());
        }

        // ==========================================
        // 12. LỊCH SỬ BIẾN ĐỘNG CHI TIẾT SKU (HISTORY)
        // ==========================================
        public async Task<IActionResult> History(int? id)
        {
            if (id == null) return NotFound();

            var variant = await _context.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.VariantId == id);

            if (variant == null) return NotFound();

            var transactions = await _context.InventoryTransactions
                .Include(t => t.Account)
                .Where(t => t.VariantId == id)
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();

            ViewBag.ActiveLots = await _context.InventoryLots
                .Where(l => l.VariantId == id
                            && l.RemainingQuantity > 0
                            && l.IsActive == true
                            && l.IsDeleted == false)
                .OrderBy(l => l.ReceivedDate)
                .ToListAsync();

            ViewBag.VariantInfo = variant;
            return View(transactions);
        }

        // ==========================================
        // 13. API LẤY CHI TIẾT CHỨNG TỪ PO/SO
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetPODetails(int id, bool isSo = false)
        {
            try
            {
                if (isSo)
                {
                    var details = await _context.SalesOrderDetails
                        .Include(d => d.Variant)
                        .ThenInclude(v => v!.Product)
                        .Where(d => d.SOId == id)
                        .Select(d => new
                        {
                            d.VariantId,
                            ProductName = d.Variant!.Product!.Name,
                            VariantDesc = $"Màu {d.Variant.Color} | Bản {d.Variant.Storage}",
                            d.Quantity,
                            d.UnitPrice,
                            TotalPrice = d.Quantity * d.UnitPrice
                        })
                        .ToListAsync();
                    return Json(details);
                }
                else
                {
                    var details = await _context.PurchaseOrderDetails
                        .Include(d => d.Variant)
                        .ThenInclude(v => v!.Product)
                        .Where(d => d.Poid == id)
                        .Select(d => new
                        {
                            d.VariantId,
                            ProductName = d.Variant!.Product!.Name,
                            VariantDesc = $"Màu {d.Variant.Color} | Bản {d.Variant.Storage}",
                            d.Quantity,
                            d.ImportPrice,
                            TotalPrice = d.Quantity * d.ImportPrice
                        })
                        .ToListAsync();
                    return Json(details);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // ==========================================
        // 14. HÀNG TỒN ĐỌNG (AGED STOCK)
        // ==========================================
        public async Task<IActionResult> AgedStock(int days = 30)
        {
            var threshold = DateTime.Now.AddDays(-days);

            var agedLots = await _context.InventoryLots
                .Include(l => l.Variant)
                .ThenInclude(v => v!.Product)
                .Where(l => l.ReceivedDate <= threshold
                            && l.RemainingQuantity > 0
                            && l.IsActive == true
                            && l.IsDeleted == false)
                .OrderBy(l => l.ReceivedDate)
                .ToListAsync();

            ViewBag.DaysThreshold = days;
            return View(agedLots);
        }

        // =========================================================================
        // 15. API LẤY DANH SÁCH SẢN PHẨM KÈM GIÁ THỰC - ĐÃ SỬA LỖI NAV PROPERTY (DÒNG ~983)
        // =========================================================================
        [HttpGet]
        public async Task<IActionResult> GetAllProductsWithStock()
        {
            try
            {
                var variantsData = await _context.ProductVariants
                    .Include(v => v.Product).ThenInclude(p => p!.Brand)
                    .Include(v => v.Product).ThenInclude(p => p!.Category)
                    .Where(v => v.IsActive == true && v.Price > 0)
                    .OrderBy(v => v.Product!.Name)
                    .ToListAsync();

                var productsList = new List<object>();

                foreach (var v in variantsData)
                {
                    // SỬA LỖI: Sử dụng LINQ Join thủ công tương tự hàm SearchVariants để tính giá nhập gần nhất
                    var actualLastImportPrice = await _context.PurchaseOrderDetails
                        .Where(pod => pod.VariantId == v.VariantId)
                        .Join(_context.PurchaseOrders,
                              pod => pod.Poid,
                              po => po.Poid,
                              (pod, po) => new { pod.ImportPrice, po.OrderDate })
                        .OrderByDescending(x => x.OrderDate)
                        .Select(x => x.ImportPrice)
                        .FirstOrDefaultAsync();

                    productsList.Add(new
                    {
                        variantId = v.VariantId,
                        productName = v.Product!.Name,
                        color = v.Color,
                        storage = v.Storage,
                        price = v.Price,
                        stock = v.Stock ?? 0,
                        imageUrl = v.ImageUrl,
                        brandId = v.Product.BrandId,
                        brandName = v.Product.Brand != null ? v.Product.Brand.BrandName : "N/A",
                        categoryId = v.Product.CategoryId,
                        categoryName = v.Product.Category != null ? v.Product.Category.CategoryName : "N/A",
                        lastImportPrice = actualLastImportPrice
                    });
                }

                return Json(productsList);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // ==========================================
        // 16. API TỰ TẠO SỐ HÓA ĐƠN NHẬP KHO (PO)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GenerateInvoiceNumber()
        {
            var today = DateTime.Now;
            var count = await _context.PurchaseOrders
                .Where(p => p.OrderDate.HasValue && p.OrderDate.Value.Year == today.Year && p.OrderDate.Value.Month == today.Month)
                .CountAsync();

            var invoiceNumber = $"HD-{today:yyyyMM}-{count + 1:D4}";
            return Json(new { invoiceNumber = invoiceNumber });
        }

        // ==========================================
        // 17. API TÍNH PHÍ VẬN CHUYỂN LOGISTICS
        // ==========================================
        [HttpPost]
        public IActionResult CalculateShipping([FromBody] ShippingCalcReq req)
        {
            try
            {
                decimal baseFee = 30000;
                decimal perUnitFee = 5000;
                decimal totalQty = req.Items?.Sum(i => i.Quantity) ?? 0;

                decimal discount = 0;
                if (totalQty >= 50) discount = 0.3m;
                else if (totalQty >= 20) discount = 0.2m;
                else if (totalQty >= 10) discount = 0.1m;

                decimal shippingFee = baseFee + (perUnitFee * totalQty);
                shippingFee = shippingFee * (1 - discount);
                shippingFee = Math.Ceiling(shippingFee / 1000) * 1000;

                return Json(new
                {
                    success = true,
                    shippingFee = shippingFee,
                    totalQty = totalQty,
                    discountPercent = discount * 100,
                    note = $"Phí vận chuyển dựa trên {totalQty} sản phẩm. Áp dụng giảm {discount * 100}% cho số lượng lớn."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class ShippingCalcReq
        {
            public List<POItemReq>? Items { get; set; }
        }

        // ==========================================
        // 18. API TỰ TẠO SỐ CHỨNG TỪ XUẤT KHO (SO)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GenerateSOInvoiceNumber()
        {
            var today = DateTime.Now;
            var count = await _context.SalesOrders
                .Where(s => s.OrderDate.Year == today.Year && s.OrderDate.Month == today.Month)
                .CountAsync();

            var invoiceNumber = $"XK-{today:yyyyMM}-{count + 1:D4}";
            return Json(new { invoiceNumber = invoiceNumber });
        }

        [HttpGet]
        public async Task<IActionResult> SearchVariantsTable(string q) => await SearchVariants(q);
        [HttpPost]
        public async Task<IActionResult> QuickAddProductTable([FromForm] QuickProductReq req) => await QuickAddProduct(req);
        [HttpPost]
        public async Task<IActionResult> AdjustStockTable(int variantId, int actualStock, string note) => await AdjustStock(variantId, actualStock, note);
    }
}