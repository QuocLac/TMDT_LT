using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models.ViewModels;
using TMDT_LT.Models.ViewModels.Storefront;

namespace TMDT_LT.Services
{
    public class PromotionEngine
    {
        private readonly ApplicationDbContext _context;
        public PromotionEngine(ApplicationDbContext context) => _context = context;

        /// <summary>
        /// Thuật toán rà quét toàn bộ hệ thống Voucher để tính toán độ chênh lệch tiền ngoài Giỏ hàng
        /// </summary>
        // <param name="cartSubtotal">Tổng tiền tạm tính hiện tại của giỏ hàng khách hàng</param>
        public async Task<List<CartVoucherStateVM>> EvaluateCartPromotionsAsync(decimal cartSubtotal, int? customerId = null)
        {
            var now = DateTime.Now;
            var results = new List<CartVoucherStateVM>();

            // 1. Chỉ quét các chiến dịch đang chạy, còn lượt dùng và được bật IsActive
            //    Voucher công khai được hiển thị cho mọi khách. Voucher phân phối riêng chỉ được áp dụng nếu có trong ví khách hàng.
            var savedPromotionIds = new HashSet<int>();
            if (customerId.HasValue && customerId.Value > 0)
            {
                savedPromotionIds = (await _context.CustomerWallet
                    .Where(w => w.CustomerId == customerId.Value && w.Status == 0)
                    .Select(w => w.PromotionId)
                    .ToListAsync()).ToHashSet();
            }

            var activePromotions = await _context.Promotions
                .Include(p => p.PromotionRules)
                .Where(p => p.IsActive && p.StartDate <= now && p.EndDate >= now && p.UsedCount < p.UsageLimit)
                .ToListAsync();

            foreach (var promo in activePromotions)
            {
                bool isPublicVoucher = promo.TargetAudience == 0;
                bool isSavedInWallet = customerId.HasValue && savedPromotionIds.Contains(promo.PromotionId);

                if (!isPublicVoucher && !isSavedInWallet)
                {
                    continue;
                }

                // Lấy quy luật tính tiền đi kèm chiến dịch
                var rule = promo.PromotionRules.FirstOrDefault();
                if (rule == null) continue;

                var state = new CartVoucherStateVM
                {
                    PromotionId = promo.PromotionId,
                    Code = promo.Code,
                    Name = promo.Name,
                    Description = promo.Description,
                    MinOrderValue = rule.MinOrderValue,
                    DiscountType = rule.DiscountType,
                    DiscountValue = rule.DiscountValue,
                    MaxDiscountAmount = rule.MaxDiscountAmount // BỔ SUNG: Truyền dữ liệu trần giảm giá vào Model
                };

                // 2. THUẬT TOÁN ĐỐI SOÁT ĐIỀU KIỆN (SMART VALIDATION)
                if (cartSubtotal >= rule.MinOrderValue)
                {
                    state.IsEligible = true;
                    state.GapAmount = 0;

                    // Tính toán số tiền thực tế sẽ giảm dựa trên loại cấu hình
                    if (rule.DiscountType == 1) // Giảm tiền mặt thẳng
                    {
                        state.EstimatedDiscountAmount = rule.DiscountValue;
                    }
                    else if (rule.DiscountType == 0) // Giảm theo tỷ lệ %
                    {
                        decimal calculated = cartSubtotal * (rule.DiscountValue / 100);
                        // Khóa trần mức giảm tối đa nếu Admin cấu hình MaxDiscountAmount
                        if (rule.MaxDiscountAmount.HasValue && calculated > rule.MaxDiscountAmount.Value)
                        {
                            state.EstimatedDiscountAmount = rule.MaxDiscountAmount.Value;
                        }
                        else
                        {
                            state.EstimatedDiscountAmount = calculated;
                        }
                    }

                    // BẢO VỆ DÒNG TIỀN: Không bao giờ được giảm âm tiền đơn hàng
                    if (state.EstimatedDiscountAmount > cartSubtotal)
                    {
                        state.EstimatedDiscountAmount = cartSubtotal;
                    }
                }
                else
                {
                    // TRẠNG THÁI: CHƯA ĐỦ TIỀN - TÍNH TOÁN KHOẢNG TRỐNG (GAP AMOUNT)
                    state.IsEligible = false;
                    state.GapAmount = rule.MinOrderValue - cartSubtotal;
                    state.EstimatedDiscountAmount = 0;
                }

                results.Add(state);
            }

            // =======================================================================
            // 3. ĐỘNG CƠ TỐI ƯU HÓA LỢI ÍCH (Khắc phục lỗi xếp hạng Voucher sai)
            // =======================================================================
            return results
                .OrderByDescending(x => x.IsEligible)                     // Ưu tiên 1: Mã nào đủ điều kiện thì vớt lên trước
                .ThenByDescending(x => x.EstimatedDiscountAmount)         // Ưu tiên 2: Trong các mã đủ điều kiện, mã nào TRỪ ĐƯỢC NHIỀU TIỀN NHẤT xếp Top 1
                .ThenBy(x => x.GapAmount)                                 // Ưu tiên 3: Nếu chưa đủ điều kiện, mã nào cần mua thêm ít tiền nhất xếp trên
                .ToList();
        }
    }
}