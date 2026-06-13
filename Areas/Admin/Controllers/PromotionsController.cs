using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Admin;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class PromotionsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PromotionsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // HÀM 1: LIỆT KÊ DANH SÁCH KHUYẾN MÃI
        public async Task<IActionResult> Index()
        {
            var promotions = await _context.Promotions
                .Include(p => p.PromotionRules)
                .OrderByDescending(p => p.PromotionId)
                .ToListAsync();

            return View(promotions);
        }

        // HÀM 2: MỞ GIAO DIỆN TẠO MỚI
        [HttpGet]
        public IActionResult Create()
        {
            return View(new PromotionCreateVM());
        }

        // HÀM 3: XỬ LÝ DỮ LIỆU TẠO MỚI (CÓ BẢO VỆ TRANSACTION & TỰ ĐỘNG PHÁT VOUCHER)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PromotionCreateVM vm)
        {
            if (!ModelState.IsValid) return View(vm);

            if (vm.EndDate <= vm.StartDate)
            {
                ModelState.AddModelError("EndDate", "Lỗi: Thời gian kết thúc phải diễn ra sau thời gian bắt đầu.");
                return View(vm);
            }

            bool isDuplicate = await _context.Promotions.AnyAsync(p => p.Code.ToUpper() == vm.Code.ToUpper());
            if (isDuplicate)
            {
                ModelState.AddModelError("Code", "Mã khuyến mãi này đã tồn tại trên hệ thống.");
                return View(vm);
            }

            if (vm.DiscountType == 0 && vm.DiscountValue > 100)
            {
                ModelState.AddModelError("DiscountValue", "Nếu giảm theo %, mức giảm không được vượt quá 100%.");
                return View(vm);
            }

            // XỬ LÝ LƯU VÀO DATABASE (PROMOTIONS + RULES + WALLETS)
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Tạo Chiến dịch lõi
                var promotion = new Promotions
                {
                    Code = vm.Code.ToUpper(),
                    Name = vm.Name,
                    Description = vm.Description,
                    StartDate = vm.StartDate,
                    EndDate = vm.EndDate,
                    UsageLimit = vm.UsageLimit,
                    IsActive = true
                };
                _context.Promotions.Add(promotion);
                await _context.SaveChangesAsync(); // Cần Save để sinh ra PromotionId

                // 2. Tạo Quy luật tính toán
                var rule = new PromotionRules
                {
                    PromotionId = promotion.PromotionId,
                    MinOrderValue = vm.MinOrderValue,
                    DiscountType = vm.DiscountType,
                    DiscountValue = vm.DiscountValue,
                    MaxDiscountAmount = vm.MaxDiscountAmount
                };
                _context.PromotionRules.Add(rule);
                await _context.SaveChangesAsync();

                // ========================================================
                // 3. ĐỘNG CƠ AIRDROP (TỰ ĐỘNG BƠM MÃ VÀO VÍ KHÁCH HÀNG)
                // ========================================================
                if (vm.TargetAudience > 0)
                {
                    List<int> targetCustomerIds = new List<int>();

                    if (vm.TargetAudience == 1) // Phát toàn bộ khách hàng
                    {
                        targetCustomerIds = await _context.Customer.Select(c => c.CustomerId).ToListAsync();
                    }
                    else if (vm.TargetAudience == 2 && !string.IsNullOrEmpty(vm.TargetCustomerType)) // Phát theo phân khúc CustomerType
                    {
                        targetCustomerIds = await _context.Customer
                                                  .Where(c => c.CustomerType == vm.TargetCustomerType)
                                                  .Select(c => c.CustomerId).ToListAsync();
                    }
                    else if (vm.TargetAudience == 3 && !string.IsNullOrWhiteSpace(vm.TargetEmails)) // Phát đích danh qua Email
                    {
                        var emails = vm.TargetEmails.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(e => e.Trim().ToLower()).ToList();

                        // SỬA LỖI Ở ĐÂY: Nối sang bảng Account để tìm theo Email
                        targetCustomerIds = await _context.Customer
                                                  .Include(c => c.Account)
                                                  .Where(c => emails.Contains(c.Account.Email.ToLower()))
                                                  .Select(c => c.CustomerId).ToListAsync();
                    }

                    // Thực thi nạp hàng loạt (Bulk Insert) vào bảng CustomerWallets
                    if (targetCustomerIds.Any())
                    {
                        var wallets = targetCustomerIds.Select(id => new CustomerWallet
                        {
                            CustomerId = id,
                            PromotionId = promotion.PromotionId,
                            Status = 0, // 0 = Trạng thái: Đã lưu vào ví
                            SavedAt = DateTime.Now
                        }).ToList();

                        _context.CustomerWallet.AddRange(wallets);
                        await _context.SaveChangesAsync();
                    }
                }

                await transaction.CommitAsync();
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", "Đã xảy ra lỗi hệ thống khi khởi tạo chiến dịch hoặc phân phối mã. Vui lòng thử lại.");
                return View(vm);
            }
        }

        // =======================================================
        // API TÌM KIẾM ĐÍCH DANH EMAIL KHÁCH HÀNG (AUTOCOMPLETE)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> SearchCustomerEmails(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return Json(new List<string>());

            term = term.ToLower().Trim();

            // Tìm kiếm Email thông qua bảng Account được liên kết với bảng Customer
            var emails = await _context.Customer
                .Include(c => c.Account)
                .Where(c => c.Account.Email.ToLower().Contains(term))
                .Select(c => c.Account.Email)
                .Take(10) // Tối ưu hiệu năng: Chỉ trả về tối đa 10 gợi ý gần đúng nhất
                .ToListAsync();

            return Json(emails);
        }
    }
}