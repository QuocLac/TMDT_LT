using System.Collections.Generic;
using TMDT_LT.Models;

namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class CheckoutVM
    {
        // 1. Dữ liệu Giỏ hàng & Khuyến mãi (Kế thừa từ Session)
        public List<CartItemVM> CartItems { get; set; } = new List<CartItemVM>();
        public decimal Subtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public string? AppliedVoucherCode { get; set; }

        // 2. Dữ liệu Phí vận chuyển & Tổng thanh toán
        public decimal ShippingFee { get; set; }
        public decimal FinalTotal { get; set; }

        // 3. Dữ liệu Tài khoản & Địa chỉ
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;

        // Danh sách địa chỉ cũ để khách chọn (Shopee Style)
        public List<Address> SavedAddresses { get; set; } = new List<Address>();

        // ID địa chỉ khách đang chọn (0 nếu chọn thêm địa chỉ mới)
        public int SelectedAddressId { get; set; }

        // 4. Các phương thức hỗ trợ
        public List<ShippingCarriers> AvailableCarriers { get; set; } = new List<ShippingCarriers>();
    }
}