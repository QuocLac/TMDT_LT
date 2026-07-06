namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class CrossSellRecommendationVM
    {
        public int ProductId { get; set; }
        public int PrimaryVariantId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string BrandName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string ProductUrl => $"/Store/Product/{ProductId}";

        public decimal Price { get; set; }
        public decimal? OldPrice { get; set; }

        public bool IsBundleDiscountApplied { get; set; }
        public decimal BundleOriginalPrice { get; set; }
        public decimal BundleDiscountAmount { get; set; }
        public string BundleDiscountLabel { get; set; } = string.Empty;
        public string BundleDiscountText => IsBundleDiscountApplied && BundleDiscountAmount > 0
            ? $"{BundleDiscountLabel}: tiết kiệm {BundleDiscountAmount:N0} đ"
            : string.Empty;

        public int SupportCount { get; set; }
        public decimal SupportPercent { get; set; }
        public decimal Confidence { get; set; }
        public decimal ConfidencePercent => Confidence * 100m;
        public decimal Lift { get; set; }

        public string ReasonText => SupportCount > 0
            ? $"Thường được chọn cùng trong {SupportCount} đơn hàng"
            : "Gợi ý phù hợp cùng danh mục";
    }
}
