using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
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

        public async Task<IActionResult> Index()
        {
            var promotions = await _context.Promotions
                .Include(p => p.PromotionRules)
                .OrderByDescending(p => p.PromotionId)
                .ToListAsync();

            return View(promotions);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new PromotionCreateVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PromotionCreateVM vm)
        {
            vm.Code = (vm.Code ?? string.Empty).Trim().ToUpperInvariant();
            vm.Name = (vm.Name ?? string.Empty).Trim();
            vm.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
            vm.TargetCustomerType = string.IsNullOrWhiteSpace(vm.TargetCustomerType) ? null : vm.TargetCustomerType.Trim();
            vm.TargetEmails = string.IsNullOrWhiteSpace(vm.TargetEmails) ? null : vm.TargetEmails.Trim();

            ModelState.Clear();
            TryValidateModel(vm);

            if (!ModelState.IsValid) return View(vm);

            if (vm.EndDate <= vm.StartDate)
            {
                ModelState.AddModelError("EndDate", "Thời gian kết thúc phải sau thời gian bắt đầu.");
                return View(vm);
            }

            if (vm.TargetAudience == 2 && string.IsNullOrWhiteSpace(vm.TargetCustomerType))
            {
                ModelState.AddModelError("TargetCustomerType", "Vui lòng chọn phân khúc khách hàng nhận mã.");
                return View(vm);
            }

            if (vm.TargetAudience == 3 && string.IsNullOrWhiteSpace(vm.TargetEmails))
            {
                ModelState.AddModelError("TargetEmails", "Vui lòng nhập ít nhất một email khách hàng nhận mã.");
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

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var promotion = new Promotions
                {
                    Code = vm.Code,
                    Name = vm.Name,
                    Description = vm.Description,
                    StartDate = vm.StartDate,
                    EndDate = vm.EndDate,
                    UsageLimit = vm.UsageLimit,
                    UsedCount = 0,
                    IsActive = true,
                    TargetAudience = vm.TargetAudience
                };

                _context.Promotions.Add(promotion);
                await _context.SaveChangesAsync();

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

                if (vm.TargetAudience > 0)
                {
                    List<int> targetCustomerIds = new List<int>();

                    if (vm.TargetAudience == 1)
                    {
                        targetCustomerIds = await _context.Customer
                            .Select(c => c.CustomerId)
                            .ToListAsync();
                    }
                    else if (vm.TargetAudience == 2)
                    {
                        targetCustomerIds = await _context.Customer
                            .Where(c => c.CustomerType == vm.TargetCustomerType)
                            .Select(c => c.CustomerId)
                            .ToListAsync();
                    }
                    else if (vm.TargetAudience == 3)
                    {
                        var emails = vm.TargetEmails!
                            .Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(e => e.Trim().ToLowerInvariant())
                            .Where(e => !string.IsNullOrWhiteSpace(e))
                            .Distinct()
                            .ToList();

                        targetCustomerIds = await _context.Customer
                            .Include(c => c.Account)
                            .Where(c => c.Account != null && emails.Contains(c.Account.Email.ToLower()))
                            .Select(c => c.CustomerId)
                            .ToListAsync();
                    }

                    targetCustomerIds = targetCustomerIds.Distinct().ToList();

                    if (!targetCustomerIds.Any())
                    {
                        ModelState.AddModelError("", "Không tìm thấy khách hàng phù hợp để phân phối mã. Vui lòng kiểm tra phân khúc hoặc danh sách email.");
                        await transaction.RollbackAsync();
                        return View(vm);
                    }

                    var existingWalletCustomerIds = await _context.CustomerWallet
                        .Where(w => w.PromotionId == promotion.PromotionId)
                        .Select(w => w.CustomerId)
                        .ToListAsync();

                    var finalCustomerIds = targetCustomerIds.Except(existingWalletCustomerIds).ToList();

                    if (finalCustomerIds.Any())
                    {
                        var wallets = finalCustomerIds.Select(id => new CustomerWallet
                        {
                            CustomerId = id,
                            PromotionId = promotion.PromotionId,
                            Status = 0,
                            SavedAt = DateTime.Now,
                            UsedAt = null
                        }).ToList();

                        _context.CustomerWallet.AddRange(wallets);
                        await _context.SaveChangesAsync();
                    }
                }

                await transaction.CommitAsync();
                TempData["Success"] = vm.TargetAudience == 0
                    ? "Đã tạo mã khuyến mãi công khai. Khách hàng có thể lưu mã từ kho voucher."
                    : "Đã tạo mã khuyến mãi và phân phối vào ví khách hàng phù hợp.";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", "Chưa thể lưu chương trình khuyến mãi. Chi tiết kỹ thuật: " + (ex.InnerException?.Message ?? ex.Message));
                return View(vm);
            }
        }

        [HttpGet]
        public async Task<IActionResult> SearchCustomerEmails(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return Json(new List<string>());

            term = term.ToLower().Trim();

            var emails = await _context.Customer
                .Include(c => c.Account)
                .Where(c => c.Account != null && c.Account.Email.ToLower().Contains(term))
                .Select(c => c.Account.Email)
                .Take(10)
                .ToListAsync();

            return Json(emails);
        }
    }
}
