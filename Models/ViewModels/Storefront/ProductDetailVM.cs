using System.Collections.Generic;
using TMDT_LT.Models;

namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class ProductDetailVM
    {
        public Products Product { get; set; } = null!;
        public List<CrossSellRecommendationVM> AprioriCrossSellProducts { get; set; } = new List<CrossSellRecommendationVM>();
        public List<Products> UpSellProducts { get; set; } = new List<Products>();
        public List<Products> RecentlyViewed { get; set; } = new List<Products>();

        // BỔ SUNG CHO MODULE ĐÁNH GIÁ:
        public List<Reviews> ApprovedReviews { get; set; } = new List<Reviews>();
        public double AverageRating { get; set; }
        public int TotalReviews { get; set; }

        // Mảng đếm số lượng đánh giá theo từng mức sao (Index 0: 1 sao, Index 4: 5 sao)
        public Dictionary<int, int> StarCounts { get; set; } = new Dictionary<int, int>();

        public TMDT_LT.Models.FlashSales? ActiveFlashSale { get; set; }
    }
}
