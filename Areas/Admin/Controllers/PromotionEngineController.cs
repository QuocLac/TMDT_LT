using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using System.Collections.Generic;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class PromotionEngineController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PromotionEngineController(ApplicationDbContext context)
        {
            _context = context;
        }

        // =======================================================
        // 1. API: MÔ PHỎNG VÀ KÉO DỮ LIỆU ĐA MỤC TIÊU (MULTI-TARGET)
        // Dùng để kéo toàn bộ sản phẩm của 1 Danh mục / Nhãn hàng vào List Tạm
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> GetVariantsByTarget(string targetType, int? targetId)
        {
            var query = _context.ProductVariants
                .Include(v => v.Product).ThenInclude(p => p.Brand)
                .Include(v => v.Product).ThenInclude(p => p.Category)
                .Where(v => v.IsActive == true && v.Product.IsActive == true);

            // Phân giải cây thư mục động
            if (targetType == "Category" && targetId.HasValue)
                query = query.Where(v => v.Product.CategoryId == targetId.Value);
            else if (targetType == "Brand" && targetId.HasValue)
                query = query.Where(v => v.Product.BrandId == targetId.Value);
            else if (targetType == "Product" && targetId.HasValue)
                query = query.Where(v => v.ProductId == targetId.Value);
            // Nếu TargetType == "All", không filter -> Lấy toàn sàn

            var variants = await query.Select(v => new {
                VariantId = v.VariantId,
                ProductId = v.ProductId,
                ProductName = v.Product.Name,
                Attribute = $"{v.Storage} - {v.Color}",
                Category = v.Product.Category.CategoryName,
                Brand = v.Product.Brand.BrandName,
                CurrentPrice = v.Price ?? 0,
                CostPrice = v.CostPrice ?? 0, // Giá vốn vừa thêm để tính Margin
                Stock = v.Stock ?? 0,
                Image = v.ImageUrl ?? v.Product.MainImage
            }).ToListAsync();

            return Json(new { success = true, data = variants });
        }

        // =======================================================
        // 2. API: TÌM KIẾM ĐÍCH DANH (GỢI Ý TỰ ĐỘNG)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> SearchVariants(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return Json(new { success = true, data = new List<object>() });

            var kw = keyword.Trim().ToLower();
            var variants = await _context.ProductVariants
                .Include(v => v.Product).ThenInclude(p => p.Brand)
                .Where(v => v.Product.Name.ToLower().Contains(kw) || v.VariantId.ToString() == kw)
                .Take(20) // Limit 20 record để bảo vệ RAM
                .Select(v => new {
                    VariantId = v.VariantId,
                    ProductId = v.ProductId,
                    ProductName = v.Product.Name,
                    Attribute = $"{v.Storage} - {v.Color}",
                    Brand = v.Product.Brand.BrandName,
                    CurrentPrice = v.Price ?? 0,
                    CostPrice = v.CostPrice ?? 0,
                    Stock = v.Stock ?? 0,
                    Image = v.ImageUrl ?? v.Product.MainImage
                }).ToListAsync();

            return Json(new { success = true, data = variants });
        }

        // =======================================================
        // 3. API: THUẬT TOÁN KHOA HỌC DSI (GỢI Ý DỌN KHO)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> GetDeadStockSuggestions()
        {
            var thirtyDaysAgo = DateTime.Now.AddDays(-30);

            // BƯỚC 1: Kéo tất cả sản phẩm đang còn tồn kho
            var allVariants = await _context.ProductVariants
                .Include(v => v.Product)
                .Where(v => v.Stock > 0 && v.IsActive == true)
                .Select(v => new {
                    v.VariantId,
                    v.ProductId,
                    v.Product.Name,
                    v.Color,
                    v.Storage,
                    v.Stock,
                    v.Price,
                    v.CostPrice,
                    Image = v.ImageUrl ?? v.Product.MainImage
                })
                .ToListAsync();

            // BƯỚC 2: Tính tổng số lượng bán ra trong 30 ngày qua cho từng Variant
            var soldData = await _context.OrderDetails
                .Include(od => od.Order)
                .Where(od => od.Order.Status == "Hoàn thành" && od.Order.OrderDate >= thirtyDaysAgo)
                .GroupBy(od => od.VariantId)
                .Select(g => new {
                    VariantId = g.Key,
                    TotalSold = g.Sum(od => od.Quantity ?? 0)
                })
                .ToDictionaryAsync(x => x.VariantId ?? 0, x => x.TotalSold);

            var suggestions = new List<object>();

            // BƯỚC 3: Xử lý công thức DSI (Days Sales of Inventory)
            foreach (var v in allVariants)
            {
                int soldIn30Days = soldData.ContainsKey(v.VariantId) ? soldData[v.VariantId] : 0;

                double dsi = 999; // Mặc định là vô cực (Cực kỳ ế)
                if (soldIn30Days > 0)
                {
                    dsi = ((double)v.Stock / soldIn30Days) * 30;
                }

                // TIÊU CHÍ ĐỀ XUẤT: Bán được <= 2 cái/tháng, HOẶC DSI > 60 ngày (Chôn vốn hơn 2 tháng)
                if (soldIn30Days <= 2 || dsi > 60)
                {
                    suggestions.Add(new
                    {
                        VariantId = v.VariantId,
                        ProductId = v.ProductId,
                        ProductName = v.Name,
                        Attribute = $"{v.Storage} - {v.Color}",
                        Stock = v.Stock,
                        Sold30Days = soldIn30Days,
                        CurrentPrice = v.Price ?? 0,
                        CostPrice = v.CostPrice ?? 0,
                        Image = v.Image,
                        Reason = soldIn30Days == 0
                                 ? "Báo động: 0 lượt mua trong tháng"
                                 : $"Tồn đọng vốn (DSI: {Math.Round(dsi, 0)} ngày)"
                    });
                }
            }

            // Trả về Top 40 sản phẩm ế ẩm nhất để UI render
            var finalData = suggestions
                .OrderBy(x => (int)x.GetType().GetProperty("Sold30Days").GetValue(x, null))
                .Take(40)
                .ToList();

            return Json(new { success = true, data = finalData });
        }
    }
}