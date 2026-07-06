using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class FlashSaleChangeLogs
    {
        [Key]
        public int ChangeLogId { get; set; }

        public int FlashSaleId { get; set; }
        [ForeignKey("FlashSaleId")]
        public virtual FlashSales? FlashSale { get; set; }

        public int? ItemId { get; set; }
        [ForeignKey("ItemId")]
        public virtual FlashSaleItems? FlashSaleItem { get; set; }

        public int? AdminAccountId { get; set; }
        [ForeignKey("AdminAccountId")]
        public virtual Account? AdminAccount { get; set; }

        [Required]
        [StringLength(80)]
        public string ActionType { get; set; } = string.Empty; // CREATE, UPDATE, END_EARLY, DISABLE, ENABLE...

        [Required]
        [StringLength(100)]
        public string FieldName { get; set; } = string.Empty;

        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        [StringLength(500)]
        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
