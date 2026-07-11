using System.Collections.Generic;
using TMDT_LT.Models;

namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class HomeStorefrontVM
    {
        public List<Products> BestSellers { get; set; } = new List<Products>();
        public List<Products> Recommendations { get; set; } = new List<Products>();

        // Danh sách Banner tiếp thị
        public List<CampaignBanners> ActiveBanners { get; set; } = new List<CampaignBanners>();

        // Danh sách Voucher công khai đẩy ra trang chủ
        public List<Promotions> TopVouchers { get; set; } = new List<Promotions>();

        // BỔ SUNG: Đẩy chiến dịch Flash Sale đang chạy ra Storefront
        public TMDT_LT.Models.FlashSales? ActiveFlashSale { get; set; }
    }

    public class SearchResultItemVM
    {
        public Products Product { get; set; } = null!;
        public int RelevanceScore { get; set; }
    }
}
