using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models
{
    public class FlashSales
    {
        [Key]
        public int FlashSaleId { get; set; }

        [Required(ErrorMessage = "Tên đợt Flash Sale là bắt buộc")]
        [StringLength(255)]
        public string Name { get; set; } = string.Empty; // VD: Giờ vàng giá sốc 20h - 22h

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public virtual ICollection<FlashSaleItems> FlashSaleItems { get; set; } = new List<FlashSaleItems>();
        public virtual ICollection<FlashSaleChangeLogs> ChangeLogs { get; set; } = new List<FlashSaleChangeLogs>();
    }
}
