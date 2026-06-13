using System;

namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class CartVoucherStateVM
    {
        public int PromotionId { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Description { get; set; }

        // --- CÁC THÔNG SỐ ĐỐI SOÁT ĐIỀU KIỆN ---
        public decimal MinOrderValue { get; set; }

        // True = Đã đủ tiền áp dụng, False = Chưa đủ tiền
        public bool IsEligible { get; set; }

        // Số tiền còn thiếu để kích hoạt mã (VD: Thiếu 50,000đ)
        public decimal GapAmount { get; set; }

        // --- PHẦN THƯỞNG DỰ KIẾN KHI THỎA ĐIỀU KIỆN ---
        // 0: Giảm %, 1: Giảm Tiền mặt, 2: Freeship
        public int DiscountType { get; set; }
        public decimal DiscountValue { get; set; }

        // BỔ SUNG: Trần giảm giá tối đa (Đồng bộ để đẩy ra cho Frontend JS đọc)
        public decimal? MaxDiscountAmount { get; set; }

        // Số tiền mặt sẽ được trừ thẳng (Tính toán ngầm từ Engine)
        public decimal EstimatedDiscountAmount { get; set; }
    }
}