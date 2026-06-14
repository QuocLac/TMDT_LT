using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class OrderHistory
    {
        [Key]
        public int HistoryId { get; set; }

        public int OrderId { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; }

        public string? Note { get; set; } // Chứa thông báo hủy đơn, lý do, số tiền đã hoàn,...

        [ForeignKey("OrderId")]
        public virtual Orders? Order { get; set; }
    }
}