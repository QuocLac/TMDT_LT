using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels.Admin
{
    public class DiscountRequest
    {
        [Required]
        public int VariantId { get; set; }

        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "Giá khuyến mãi phải lớn hơn hoặc bằng 0")]
        public decimal DiscountPrice { get; set; }
    }
}