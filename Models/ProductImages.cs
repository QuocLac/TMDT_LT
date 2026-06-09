using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class ProductImages
    {
        [Key]
        public int ImageId { get; set; }

        public int ProductId { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        [ForeignKey("ProductId")]
        public virtual Products? Product { get; set; }
    }
}