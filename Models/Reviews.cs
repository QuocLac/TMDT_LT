using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public partial class Reviews
    {
        [Key]
        public int ReviewId { get; set; }

        public int? ProductId { get; set; }
        [ForeignKey("ProductId")]
        public virtual Products? Product { get; set; }

        public int? CustomerId { get; set; }
        [ForeignKey("CustomerId")]
        public virtual Customer? Customer { get; set; }

        // --- BỔ SUNG MÃ ĐƠN HÀNG ĐỂ ADMIN ĐỐI SOÁT XÁC THỰC ---
        public int? OrderId { get; set; }
        [ForeignKey("OrderId")]
        public virtual Orders? Order { get; set; } // Liên kết đến bảng Đơn hàng của hệ thống

        public int? Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Các cờ trạng thái nghiệp vụ Admin
        public bool IsRead { get; set; } = false;
        public bool IsHidden { get; set; } = false;
        public string? AdminReply { get; set; }

        public virtual ICollection<ReviewDetails> ReviewDetails { get; set; } = new List<ReviewDetails>();
    }
}