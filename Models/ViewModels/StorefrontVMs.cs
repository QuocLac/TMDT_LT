using System.Collections.Generic;
using TMDT_LT.Models;

namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class HomeStorefrontVM
    {
        public List<Products> BestSellers { get; set; } = new List<Products>();
        public List<Products> Recommendations { get; set; } = new List<Products>();

        // Thêm danh sách Banner tiếp thị
        public List<CampaignBanners> ActiveBanners { get; set; } = new List<CampaignBanners>();
    }

    public class SearchResultItemVM
    {
        public Products Product { get; set; } = null!;
        public int RelevanceScore { get; set; }
    }
}