using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ShippingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ShippingController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. Tải danh sách các đơn vị vận chuyển
        public async Task<IActionResult> Index()
        {
            // Tự động Seed (khởi tạo) dữ liệu mẫu nếu bảng đang trống để bạn dễ test giao diện
            if (!_context.ShippingCarriers.Any())
            {
                _context.ShippingCarriers.AddRange(
                    new ShippingCarriers { CarrierCode = "GHTK", CarrierName = "Giao Hàng Tiết Kiệm", LogoUrl = "https://cdn.haitrieu.com/wp-content/uploads/2022/05/Logo-GHTK-Green.png", IsActive = true, IsDefault = true },
                    new ShippingCarriers { CarrierCode = "GHN", CarrierName = "Giao Hàng Nhanh", LogoUrl = "https://cdn.haitrieu.com/wp-content/uploads/2022/05/Logo-GHN-Orange.png", IsActive = true, IsDefault = false },
                    new ShippingCarriers { CarrierCode = "VTP", CarrierName = "Viettel Post", LogoUrl = "https://cdn.haitrieu.com/wp-content/uploads/2022/05/Logo-Viettel-Post-Red.png", IsActive = false, IsDefault = false }
                );
                await _context.SaveChangesAsync();
            }

            var carriers = await _context.ShippingCarriers
                .OrderByDescending(c => c.IsDefault) // Ưu tiên thằng mặc định lên đầu
                .ThenBy(c => c.CarrierName)
                .ToListAsync();

            return View(carriers);
        }

        // 2. API AJAX: Bật/Tắt trạng thái hoạt động của một hãng
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var carrier = await _context.ShippingCarriers.FindAsync(id);
            if (carrier == null) return NotFound();

            // Không cho phép tắt nếu đang là phương thức mặc định
            if (carrier.IsDefault && carrier.IsActive)
            {
                return Json(new { success = false, message = "Không thể tắt đơn vị vận chuyển đang được đặt làm mặc định!" });
            }

            carrier.IsActive = !carrier.IsActive;
            await _context.SaveChangesAsync();

            return Json(new { success = true, isActive = carrier.IsActive });
        }

        // 3. API AJAX: Đặt một hãng làm phương thức giao hàng mặc định
        [HttpPost]
        public async Task<IActionResult> SetDefault(int id)
        {
            var carrier = await _context.ShippingCarriers.FindAsync(id);
            if (carrier == null || !carrier.IsActive)
                return Json(new { success = false, message = "Chỉ có thể đặt mặc định cho đơn vị đang hoạt động!" });

            // Gỡ cờ mặc định của tất cả các hãng khác
            var currentDefaults = await _context.ShippingCarriers.Where(c => c.IsDefault).ToListAsync();
            foreach (var item in currentDefaults)
            {
                item.IsDefault = false;
            }

            // Đặt cờ mặc định cho hãng được chọn
            carrier.IsDefault = true;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
    }
}