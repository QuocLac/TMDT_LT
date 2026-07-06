using System.Collections.Generic;

namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class CartItemVM
    {
        public int VariantId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public string Color { get; set; } = "";
        public string Storage { get; set; } = "";
        public string ImageUrl { get; set; } = "";

        // Tổng số lượng khách muốn mua và Tồn kho vật lý
        public int Quantity { get; set; }
        public int Stock { get; set; }

        // --- NHÓM THÔNG SỐ TÁCH GIÁ (MỚI) ---
        public decimal RegularPrice { get; set; }
        public int RegularQty { get; set; }

        public bool IsBundleDiscount { get; set; }
        public decimal BundleOriginalRegularPrice { get; set; }
        public decimal BundleDiscountAmountPerUnit { get; set; }
        public string BundleDiscountLabel { get; set; } = string.Empty;

        public bool IsFlashSale { get; set; }
        public decimal FlashSalePrice { get; set; }
        public int FlashSaleQty { get; set; }
        public int MaxPerUser { get; set; }

        // Tính tổng tiền thông minh (Gộp cả giá Sale và giá Thường)
        public decimal TotalPrice => (RegularPrice * RegularQty) + (FlashSalePrice * FlashSaleQty);

        // Cầu nối tương thích ngược cho các file UI cũ chưa kịp cập nhật
        public decimal Price => IsFlashSale ? FlashSalePrice : RegularPrice;
        public decimal OriginalPrice => IsBundleDiscount && BundleOriginalRegularPrice > RegularPrice ? BundleOriginalRegularPrice : RegularPrice;
        public decimal BundleSavedTotal => IsBundleDiscount ? BundleDiscountAmountPerUnit * RegularQty : 0m;

        public List<CrossSellRecommendationVM> CrossSellRecommendations { get; set; } = new List<CrossSellRecommendationVM>();
    }
}