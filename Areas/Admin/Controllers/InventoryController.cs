using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class InventoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        public InventoryController(ApplicationDbContext context) => _context = context;

        // 1. MÀN HÌNH DANH SÁCH TỒN KHO (Giữ nguyên)
        public async Task<IActionResult> Index()
        {
            var variants = await _context.ProductVariants
                .Include(v => v.Product)
                .OrderByDescending(v => v.CreatedDate)
                .ToListAsync();

            return View(variants);
        }

        // 2. MÀN HÌNH TẠO PHIẾU NHẬP (GET)
        public async Task<IActionResult> CreatePO()
        {
            // Lấy dữ liệu cho Dropdown
            ViewBag.Suppliers = await _context.Suppliers.Where(s => s.IsActive == true).ToListAsync();
            ViewBag.Categories = await _context.Categories.Where(c => c.IsActive == true).ToListAsync();
            // Lưu ý: Brands không có cột IsActive như chúng ta đã fix lỗi trước đó
            ViewBag.Brands = await _context.Brands.ToListAsync();

            return View();
        }

        // =====================================================================
        // CÁC API DÀNH CHO AJAX GỌI TỪ MÀN HÌNH NHẬP KHO
        // =====================================================================

        // API 1: Tìm kiếm biến thể có sẵn (Smart Search)
        [HttpGet]
        public async Task<IActionResult> SearchVariants(string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2) return Json(new List<object>());

            var variants = await _context.ProductVariants
                .Include(v => v.Product)
                .Where(v => v.Product.Name.Contains(q) || v.Color.Contains(q) || v.Storage.Contains(q))
                .Select(v => new {
                    variantId = v.VariantId,
                    productName = v.Product.Name,
                    color = v.Color,
                    storage = v.Storage + (string.IsNullOrEmpty(v.Ram) ? "" : $" ({v.Ram})"),
                    stock = v.Stock ?? 0,
                    lastImportPrice = v.Price * 0.7m // Giả lập giá nhập bằng 70% giá bán (hoặc lấy từ lịch sử nhập kho thực tế)
                })
                .Take(10) // Giới hạn 10 kết quả để tối ưu tốc độ
                .ToListAsync();

            return Json(variants);
        }

        // API 2: Tạo nhanh sản phẩm mới ngay trong lúc nhập kho (Modal)
        [HttpPost]
        public async Task<IActionResult> QuickAddProduct([FromBody] QuickProductReq req)
        {
            if (string.IsNullOrEmpty(req.Name) || string.IsNullOrEmpty(req.Color) || string.IsNullOrEmpty(req.Storage))
                return Json(new { success = false, message = "Thiếu thông tin bắt buộc" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Bước 1: Sinh sản phẩm cha
                var product = new Products
                {
                    Name = req.Name,
                    CategoryId = req.CategoryId,
                    BrandId = req.BrandId,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };
                _context.Products.Add(product);
                await _context.SaveChangesAsync();

                // Bước 2: Sinh biến thể con (Stock khởi tạo = 0)
                var variant = new ProductVariants
                {
                    ProductId = product.ProductId,
                    Color = req.Color,
                    Storage = req.Storage,
                    Stock = 0,
                    Price = 0,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };
                _context.ProductVariants.Add(variant);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                // Trả về JSON theo đúng format mà JS ở giao diện đang đợi để nhét vào bảng
                return Json(new
                {
                    success = true,
                    newVariant = new
                    {
                        variantId = variant.VariantId,
                        productName = product.Name,
                        color = variant.Color,
                        storage = variant.Storage,
                        stock = 0,
                        lastImportPrice = 0
                    }
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = ex.Message });
            }
        }

        // API 3: Xác nhận chốt phiếu nhập và cộng Tồn kho (Transaction)
        [HttpPost]
        public async Task<IActionResult> SubmitPO([FromBody] PORequestReq req)
        {
            if (req.Items == null || !req.Items.Any())
                return Json(new { success = false, message = "Phiếu nhập chưa có hàng hóa" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Tạo Phiếu Nhập
                decimal totalAmount = req.Items.Sum(d => d.Quantity * d.ImportPrice);
                var po = new PurchaseOrders
                {
                    SupplierId = req.SupplierId,
                    AccountId = 1, // Giả lập Admin thao tác
                    OrderDate = DateTime.Now,
                    TotalAmount = totalAmount,
                    Status = "Hoàn thành",
                    Note = req.Note
                };
                _context.PurchaseOrders.Add(po);
                await _context.SaveChangesAsync();

                foreach (var item in req.Items)
                {
                    if (item.Quantity <= 0 || item.ImportPrice < 0) continue;

                    // 2. Lưu chi tiết phiếu
                    var poDetail = new PurchaseOrderDetails
                    {
                        Poid = po.Poid, // Dùng Poid theo đúng cấu trúc database của bạn
                        VariantId = item.VariantId,
                        Quantity = item.Quantity,
                        ImportPrice = item.ImportPrice
                    };
                    _context.PurchaseOrderDetails.Add(poDetail);

                    // 3. Cộng Tồn kho
                    var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                    if (variant != null)
                    {
                        variant.Stock = (variant.Stock ?? 0) + item.Quantity;
                    }

                    // 4. Ghi log thẻ kho
                    var invLog = new InventoryTransactions
                    {
                        VariantId = item.VariantId,
                        TransactionType = "IN_PURCHASE",
                        Quantity = item.Quantity,
                        ReferenceId = po.Poid,
                        TransactionDate = DateTime.Now,
                        AccountId = 1,
                        Note = $"Nhập kho từ nhà cung cấp"
                    };
                    _context.InventoryTransactions.Add(invLog);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        // =====================================================================

        // 3. XEM THẺ KHO (Giữ nguyên)
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

            ViewBag.VariantInfo = variant;
            return View(transactions);
        }

        // GET: Areas/Admin/Inventory/POHistory
        // Màn hình hiển thị danh sách toàn bộ các Phiếu nhập kho lịch sử
        public async Task<IActionResult> POHistory()
        {
            var purchaseOrders = await _context.PurchaseOrders
                .Include(p => p.Supplier)
                .Include(p => p.Account)
                .OrderByDescending(p => p.OrderDate)
                .ToListAsync();

            return View(purchaseOrders);
        }

        // API AJAX: Lấy danh sách sản phẩm chi tiết bên trong một Phiếu nhập kho cụ thể
        [HttpGet]
        public async Task<IActionResult> GetPODetails(int id)
        {
            var poDetails = await _context.PurchaseOrderDetails
                .Include(pd => pd.Variant)
                    .ThenInclude(v => v.Product)
                .Where(pd => pd.Poid == id)
                .Select(pd => new {
                    productName = pd.Variant.Product.Name,
                    variantDesc = $"Màu: {pd.Variant.Color} | Bản: {pd.Variant.Storage}",
                    qty = pd.Quantity,
                    importPrice = pd.ImportPrice,
                    totalPrice = pd.Quantity * pd.ImportPrice
                })
                .ToListAsync();

            return Json(poDetails);
        }
        // API AJAX: Xử lý Kiểm kê / Điều chỉnh kho (Stock Adjustment)
        [HttpPost]
        public async Task<IActionResult> AdjustStock(int variantId, int actualStock, string note)
        {
            if (actualStock < 0) return Json(new { success = false, message = "Tồn kho thực tế không được âm." });
            if (string.IsNullOrWhiteSpace(note)) return Json(new { success = false, message = "Bắt buộc phải ghi rõ lý do điều chỉnh." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var variant = await _context.ProductVariants.FindAsync(variantId);
                if (variant == null) return Json(new { success = false, message = "Không tìm thấy sản phẩm." });

                int currentStock = variant.Stock ?? 0;
                int diff = actualStock - currentStock; // Tính số lượng chênh lệch

                if (diff == 0) return Json(new { success = false, message = "Số lượng thực tế khớp với hệ thống, không cần điều chỉnh." });

                // 1. Cập nhật tồn kho mới
                variant.Stock = actualStock;

                // 2. Ghi Thẻ kho
                var invLog = new InventoryTransactions
                {
                    VariantId = variantId,
                    TransactionType = "ADJUST",
                    Quantity = diff, // Có thể là số âm (nếu mất hàng) hoặc dương (nếu dư hàng)
                    TransactionDate = DateTime.Now,
                    AccountId = 1, // Giả lập Admin thao tác
                    Note = $"Kiểm kê kho: {note} (Hệ thống cũ: {currentStock} -> Thực tế đếm: {actualStock})"
                };
                _context.InventoryTransactions.Add(invLog);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, newStock = actualStock, diff = diff });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }
    }

    // Các Class Models phụ trợ để đón dữ liệu JSON từ màn hình AJAX gửi xuống
    public class QuickProductReq
    {
        public string Name { get; set; }
        public int CategoryId { get; set; }
        public int BrandId { get; set; }
        public string Color { get; set; }
        public string Storage { get; set; }
    }

    public class POItemReq
    {
        public int VariantId { get; set; }
        public int Quantity { get; set; }
        public decimal ImportPrice { get; set; }
    }

    public class PORequestReq
    {
        public int SupplierId { get; set; }
        public string Note { get; set; }
        public List<POItemReq> Items { get; set; }
    }
}